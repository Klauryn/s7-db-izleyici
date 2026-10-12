using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using S7.Net;

namespace DbIzleyici.Vm;

public partial class MainViewModel : ObservableObject
{
    readonly PlcClient _plc = new();
    readonly string _varsFile = Path.Combine(AppContext.BaseDirectory, "variables.json");

    public string[] CpuTypes { get; } = { "S71200", "S71500", "S7300", "S7400" };
    public VarType[] VarTypes { get; } = Enum.GetValues<VarType>();

    [ObservableProperty] private string _ip = "192.168.0.1";
    [ObservableProperty] private string _cpu = "S71500";
    [ObservableProperty] private int _rack;
    [ObservableProperty] private int _slot = 1;
    [ObservableProperty] private int _intervalMs = 200;
    [ObservableProperty] private bool _allowWrite;
    [ObservableProperty] private string _connectText = "Bağlan";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private Brush _statusBrush = Brushes.Gray;
    [ObservableProperty] private string _statsText = "Okuma: - ms";
    [ObservableProperty] private PlcVariable? _selectedVariable;
    [ObservableProperty] private string _writeValue = "";

    public BoolArrayVm Bool { get; }
    public NumberArrayVm Number { get; }
    public StringArrayVm Str { get; }
    public IEnumerable<ArrayChannelVm> Channels => new ArrayChannelVm[] { Bool, Number, Str };

    public ObservableCollection<PlcVariable> Vars { get; } = new();
    public ObservableCollection<string> Log { get; } = new();

    CancellationTokenSource? _cts;
    int _errors, _reconnects;
    double _avgMs;

    public MainViewModel()
    {
        Bool = new BoolArrayVm(_plc, 57, 0, 32);
        Number = new NumberArrayVm(_plc, 58, 0, 128, 16);
        Str = new StringArrayVm(_plc, 59, 0, 2048, 8);
        foreach (var ch in Channels)
        {
            ch.WriteAllowed = () => AllowWrite;
            ch.Log = AddLog;
        }
        SetStatus("Bağlı değil", "Bad");
        LoadVars();
    }

    static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    void SetStatus(string text, string brushKey)
    {
        StatusText = "● " + text;
        StatusBrush = Res(brushKey);
    }

    void AddLog(string msg)
    {
        Log.Add($"{DateTime.Now:HH:mm:ss.fff}  {msg}");
        if (Log.Count > 300) Log.RemoveAt(0);
    }

    // Aynı hata her turda log'u doldurmasın diye yalnız değiştiğinde yaz.
    string _lastLogged = "";
    void LogOnce(string msg)
    {
        if (msg == _lastLogged) return;
        _lastLogged = msg;
        AddLog(msg);
    }

    [RelayCommand]
    private async Task ToggleConnectAsync()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            _cts = null;
            await _plc.DisconnectAsync();
            ConnectText = "Bağlan";
            SetStatus("Bağlı değil", "Bad");
            return;
        }
        _cts = new CancellationTokenSource();
        ConnectText = "Kes";
        _ = PollLoopAsync(_cts.Token);
    }

    // Döngü: bağlı değilse üstel bekleme ile yeniden bağlan, bağlıysa değişkenleri ve kanalları oku.
    async Task PollLoopAsync(CancellationToken ct)
    {
        int backoff = 500;
        var sw = new Stopwatch();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_plc.IsConnected)
                {
                    SetStatus("Bağlanıyor...", "Warn");
                    using var tcts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    tcts.CancelAfter(3000);
                    await _plc.ConnectAsync(Enum.Parse<CpuType>(Cpu), Ip.Trim(), (short)Rack, (short)Slot, tcts.Token);
                    SetStatus("Bağlı", "Accent");
                    AddLog("Bağlandı.");
                    backoff = 500;
                }

                sw.Restart();
                foreach (var g in Plan())
                {
                    // Veri hatası (ör. DB yok) yalnız bu grubu etkiler; bağlantıyı kesmez.
                    try
                    {
                        var buf = await _plc.ReadRangeAsync(g.Db, g.Start, g.Count, ct);
                        foreach (var v in g.Vars) v.Value = v.Decode(buf, g.Start);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex) when (_plc.IsConnected)
                    {
                        foreach (var v in g.Vars) v.Value = "Hata";
                        LogOnce($"DB{g.Db}: {ex.Message}");
                    }
                }
                foreach (var ch in Channels) await ch.PollAsync(ct);
                sw.Stop();

                double ms = sw.Elapsed.TotalMilliseconds;
                _avgMs = _avgMs == 0 ? ms : _avgMs * 0.9 + ms * 0.1;
                StatsText = $"Okuma: {ms:0.0} ms (ort. {_avgMs:0.0}) | Hata: {_errors} | Yeniden bağlanma: {_reconnects}";
                await Task.Delay(IntervalMs, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _errors++;
                _reconnects++;
                SetStatus("Bağlantı sorunu", "Bad");
                AddLog($"Hata: {ex.Message} (yeniden denenecek: {backoff} ms)");
                try { await _plc.DisconnectAsync(); } catch { }
                try { await Task.Delay(backoff, ct); }
                catch (OperationCanceledException) { break; }
                backoff = Math.Min(backoff * 2, 5000);
            }
        }
    }

    // Aynı DB'deki değişkenleri tek aralıkta okumak için grupla.
    List<(int Db, int Start, int Count, List<PlcVariable> Vars)> Plan() =>
        Vars.Where(v => v.Db > 0 && !string.IsNullOrWhiteSpace(v.Name))
            .GroupBy(v => v.Db)
            .Select(g =>
            {
                int start = g.Min(v => v.Offset);
                int end = g.Max(v => v.Offset + v.Size);
                return (g.Key, start, end - start, g.ToList());
            }).ToList();

    [RelayCommand]
    private async Task WriteSelectedAsync()
    {
        if (SelectedVariable is null) { AddLog("Önce bir satır seç."); return; }
        if (!AllowWrite) { AddLog("Yazma kapalı: önce 'Yazmaya izin ver'i işaretle."); return; }
        if (!_plc.IsConnected) { AddLog("Önce bağlan."); return; }
        try
        {
            await _plc.WriteAsync(SelectedVariable, WriteValue, CancellationToken.None);
            AddLog($"Yazıldı: {SelectedVariable.Name} = {WriteValue}");
        }
        catch (Exception ex) { AddLog($"Yazma hatası: {ex.Message}"); }
    }

    // Bool dizisi (256 eleman) değişken olarak eklenir; ad From[i], DB ve başlangıç Bool kanalından.
    [RelayCommand]
    private void AddBoolArray()
    {
        for (int i = 0; i < BoolArrayVm.Bits; i++)
            Vars.Add(new PlcVariable
            {
                Name = $"From[{i}]", Type = VarType.Bool, Db = Bool.Db,
                Offset = Bool.FromStart + i / 8, Bit = i % 8
            });
        AddLog($"From: {BoolArrayVm.Bits} Bool eklendi (DB{Bool.Db}, bayt {Bool.FromStart}...).");
    }

    // Aynı değişkenleri 50 tur toplu, 50 tur tek tek okur; süreleri karşılaştırır.
    [RelayCommand]
    private async Task BenchAsync()
    {
        if (!_plc.IsConnected) { AddLog("Önce bağlan."); return; }
        var plan = Plan();
        if (plan.Count == 0) { AddLog("Değişken yok."); return; }
        const int rounds = 50;
        try
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < rounds; i++)
                foreach (var g in plan) await _plc.ReadRangeAsync(g.Db, g.Start, g.Count, CancellationToken.None);
            double bulk = sw.Elapsed.TotalMilliseconds / rounds;

            sw.Restart();
            for (int i = 0; i < rounds; i++)
                foreach (var g in plan)
                    foreach (var v in g.Vars) await _plc.ReadRangeAsync(v.Db, v.Offset, v.Size, CancellationToken.None);
            double single = sw.Elapsed.TotalMilliseconds / rounds;

            AddLog($"Ölçüm ({plan.Sum(g => g.Vars.Count)} değişken, {rounds} tur): toplu {bulk:0.0} ms/tur, tek tek {single:0.0} ms/tur, oran {single / bulk:0.0}x");
        }
        catch (Exception ex) { AddLog($"Ölçüm hatası: {ex.Message}"); }
    }

    void LoadVars()
    {
        try
        {
            if (File.Exists(_varsFile))
                foreach (var v in JsonSerializer.Deserialize<List<PlcVariable>>(File.ReadAllText(_varsFile)) ?? new())
                    Vars.Add(v);
        }
        catch { /* bozuk dosya: boş başla */ }
    }

    public void SaveVars()
    {
        try { File.WriteAllText(_varsFile, JsonSerializer.Serialize(Vars.Where(v => v.Name != "").ToList())); }
        catch { }
    }

    public void Shutdown()
    {
        _cts?.Cancel();
        SaveVars();
        _plc.Dispose();
    }
}
