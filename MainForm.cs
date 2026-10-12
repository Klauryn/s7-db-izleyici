using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using S7.Net;

namespace DbIzleyici;

public class MainForm : Form
{
    readonly PlcClient _plc = new();
    readonly BindingList<PlcVariable> _vars = new();
    readonly string _varsFile = Path.Combine(AppContext.BaseDirectory, "variables.json");

    readonly TextBox _ip = new() { Text = "192.168.0.1", Width = 110 };
    readonly ComboBox _cpu = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    readonly NumericUpDown _rack = new() { Width = 45, Maximum = 7 };
    readonly NumericUpDown _slot = new() { Width = 45, Maximum = 31, Value = 1 };
    readonly NumericUpDown _interval = new() { Width = 70, Minimum = 20, Maximum = 5000, Value = 200, Increment = 50 };
    readonly Button _connect = new() { Text = "Bağlan", AutoSize = true };
    readonly Label _status = new() { AutoSize = true, Text = "● Bağlı değil", ForeColor = Theme.Bad };
    readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = true };
    readonly TextBox _writeValue = new() { Width = 100 };
    readonly Button _write = new() { Text = "Seçiliye yaz", AutoSize = true };
    readonly Button _bench = new() { Text = "Toplu / tek tek süre ölç", AutoSize = true };
    readonly Button _addArray = new() { Text = "Bool dizisi ekle...", AutoSize = true };
    readonly Label _stats = new() { AutoSize = true, Text = "Okuma: - ms" };
    readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };

    // From/To kanalları: Bool (16x16), Sayı (LReal/Real/DInt/Int), String. Sayı ve String varsayılan olarak kapalı;
    // DB numarasını kendi PLC'ne göre ayarlayıp "Oku"yu işaretle.
    readonly List<ArrayChannel> _channels = new()
    {
        new BoolChannel(db: 57, from: 0, to: 32),
        new NumberChannel(db: 58, from: 0, to: 128, count: 16),
        new StringChannel(db: 59, from: 0, to: 2048, count: 8),
    };
    readonly CheckBox _allowWrite = new() { Text = "Yazmaya izin ver", AutoSize = true, Margin = new Padding(16, 5, 0, 0) };

    CancellationTokenSource? _cts;
    int _errors, _reconnects;
    double _avgMs;

    public MainForm()
    {
        Text = "DB İzleyici (S7.Net+)";
        Width = 960; Height = 760;
        MinimumSize = new Size(900, 640);

        _cpu.Items.AddRange(new object[] { "S71200", "S71500", "S7300", "S7400" });
        _cpu.SelectedIndex = 1;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        top.Controls.AddRange(new Control[]
        {
            L("IP"), _ip, L("CPU"), _cpu, L("Rack"), _rack, L("Slot"), _slot,
            L("Periyot ms"), _interval, _connect, _status
        });

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        bottom.Controls.AddRange(new Control[] { L("Değer"), _writeValue, _write, _addArray, _bench, _allowWrite, _stats });

        var logBox = new Panel { Dock = DockStyle.Bottom, Height = 140, Padding = new Padding(10, 0, 10, 10) };
        _log.Font = Theme.MonoFont;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = Theme.Surface;
        _log.ForeColor = Theme.TextMuted;
        logBox.Controls.Add(_log);

        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Ad", DataPropertyName = nameof(PlcVariable.Name), Width = 160 });
        _grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            HeaderText = "Tip", DataPropertyName = nameof(PlcVariable.Type), Width = 70,
            DataSource = Enum.GetValues<VarType>()
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "DB", DataPropertyName = nameof(PlcVariable.Db), Width = 50 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Ofset", DataPropertyName = nameof(PlcVariable.Offset), Width = 60 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Bit", DataPropertyName = nameof(PlcVariable.Bit), Width = 40 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Değer", DataPropertyName = nameof(PlcVariable.Value), ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.DataSource = _vars;
        _grid.DataError += (_, e) => e.ThrowException = false;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var titles = new Dictionary<string, string> { ["Bool"] = "Bool (16x16)", ["Sayı"] = "Sayı (LReal/Real/DInt/Int)", ["String"] = "String" };
        foreach (var ch in _channels)
        {
            ch.WriteAllowed = () => _allowWrite.Checked;
            ch.Log = Log;
            var page = new TabPage(titles[ch.Name]);
            page.Controls.Add(ch);
            tabs.TabPages.Add(page);
        }
        var varPage = new TabPage("Değişkenler");
        varPage.Controls.Add(_grid);
        tabs.TabPages.Add(varPage);

        Controls.Add(tabs);
        Controls.Add(bottom);
        Controls.Add(logBox);
        Controls.Add(top);

        Theme.Apply(this);
        // Bağlan düğmesi vurgulu (mint), diğer eylemler nötr
        _connect.BackColor = Theme.AccentDim;
        _connect.ForeColor = Color.White;
        _connect.FlatAppearance.BorderColor = Theme.Accent;
        _status.Padding = new Padding(10, 0, 0, 0);

        LoadVars();
        _connect.Click += async (_, _) => await ToggleConnectAsync();
        _write.Click += async (_, _) => await WriteSelectedAsync();
        _bench.Click += async (_, _) => await BenchAsync();
        _addArray.Click += (_, _) => AddBoolArray();
        FormClosing += (_, _) => { SaveVars(); _cts?.Cancel(); _plc.Dispose(); };
    }

    static Label L(string t) => new() { Text = t, AutoSize = true, Margin = new Padding(8, 7, 0, 0) };

    // Windows 10/11: başlık çubuğunu koyu temaya uydur (desteklemeyen sürümde sessizce atlanır).
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int dark = 1;
        try { DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); } catch { }
    }

    void Log(string msg)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(msg)); return; }
        _log.AppendText($"{DateTime.Now:HH:mm:ss.fff}  {msg}{Environment.NewLine}");
    }

    // Aynı hata her turda log'u doldurmasın diye yalnız değiştiğinde yaz.
    string _lastLogged = "";
    void LogOnce(string msg)
    {
        if (msg == _lastLogged) return;
        _lastLogged = msg;
        Log(msg);
    }

    void SetStatus(string text, Color c)
    {
        if (InvokeRequired) { BeginInvoke(() => SetStatus(text, c)); return; }
        _status.Text = "● " + text; _status.ForeColor = c;
    }

    // Aynı DB'deki değişkenleri tek aralıkta okumak için grupla.
    List<(int Db, int Start, int Count, List<PlcVariable> Vars)> Plan()
    {
        return _vars.Where(v => v.Db > 0 && !string.IsNullOrWhiteSpace(v.Name))
            .GroupBy(v => v.Db)
            .Select(g =>
            {
                int start = g.Min(v => v.Offset);
                int end = g.Max(v => v.Offset + v.Size);
                return (g.Key, start, end - start, g.ToList());
            }).ToList();
    }

    async Task ToggleConnectAsync()
    {
        if (_cts is not null)
        {
            _cts.Cancel(); _cts = null;
            await _plc.DisconnectAsync();
            _connect.Text = "Bağlan";
            SetStatus("Bağlı değil", Theme.Bad);
            return;
        }
        _cts = new CancellationTokenSource();
        _connect.Text = "Kes";
        _ = PollLoopAsync(_cts.Token);
    }

    CpuType SelectedCpu() => Enum.Parse<CpuType>((string)_cpu.SelectedItem!);

    // Döngü: bağlı değilse üstel bekleme ile yeniden bağlan, bağlıysa toplu oku.
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
                    SetStatus("Bağlanıyor...", Theme.Warn);
                    using var tcts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    tcts.CancelAfter(3000);
                    await _plc.ConnectAsync(SelectedCpu(), _ip.Text.Trim(), (short)_rack.Value, (short)_slot.Value, tcts.Token);
                    SetStatus("Bağlı", Theme.Accent);
                    Log("Bağlandı.");
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
                foreach (var ch in _channels)
                    if (ch.ReadEnabled) await ch.PollAsync(_plc, ct);
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds;
                _avgMs = _avgMs == 0 ? ms : _avgMs * 0.9 + ms * 0.1;
                BeginInvoke(() =>
                {
                    _grid.Invalidate();
                    _stats.Text = $"Okuma: {ms:0.0} ms (ort. {_avgMs:0.0}) | Hata: {_errors} | Yeniden bağlanma: {_reconnects}";
                });
                await Task.Delay((int)_interval.Value, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _errors++;
                _reconnects++;
                SetStatus("Bağlantı sorunu", Theme.Bad);
                Log($"Hata: {ex.Message} (yeniden denenecek: {backoff} ms)");
                try { await _plc.DisconnectAsync(); } catch { }
                try { await Task.Delay(backoff, ct); } catch (OperationCanceledException) { break; }
                backoff = Math.Min(backoff * 2, 5000);
            }
        }
    }

    async Task WriteSelectedAsync()
    {
        if (_grid.CurrentRow?.DataBoundItem is not PlcVariable v) { Log("Önce bir satır seç."); return; }
        try
        {
            await _plc.WriteAsync(v, _writeValue.Text, CancellationToken.None);
            Log($"Yazıldı: {v.Name} = {_writeValue.Text}");
        }
        catch (Exception ex) { Log($"Yazma hatası: {ex.Message}"); }
    }

    // Aynı değişkenleri 50 tur toplu, 50 tur tek tek okur; süreleri karşılaştırır.
    // Not: izleme döngüsü açıkken de çalışır; kilit sayesinde çakışmaz ama ölçüm şişebilir.
    async Task BenchAsync()
    {
        if (!_plc.IsConnected) { Log("Önce bağlan."); return; }
        const int rounds = 50;
        var plan = Plan();
        if (plan.Count == 0) { Log("Değişken yok."); return; }
        _bench.Enabled = false;
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

            int n = plan.Sum(g => g.Vars.Count);
            Log($"Ölçüm ({n} değişken, {rounds} tur): toplu {bulk:0.0} ms/tur, tek tek {single:0.0} ms/tur, oran {single / bulk:0.0}x");
        }
        catch (Exception ex) { Log($"Ölçüm hatası: {ex.Message}"); }
        finally { _bench.Enabled = true; }
    }

    // Array[0..N-1] of Bool: eleman i, başlangıç baytından i/8. bayt ve i%8. bitte durur (non-optimized DB).
    void AddBoolArray()
    {
        using var f = new Form { Text = "Bool dizisi ekle", Width = 300, Height = 230, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false };
        var name = new TextBox { Text = "From", Left = 130, Top = 12, Width = 130 };
        var db = new NumericUpDown { Left = 130, Top = 42, Width = 130, Maximum = 65535, Value = 57 };
        var start = new NumericUpDown { Left = 130, Top = 72, Width = 130, Maximum = 65535 };
        var count = new NumericUpDown { Left = 130, Top = 102, Width = 130, Minimum = 1, Maximum = 4096, Value = 256 };
        var ok = new Button { Text = "Ekle", Left = 130, Top = 140, DialogResult = DialogResult.OK };
        f.Controls.AddRange(new Control[]
        {
            new Label { Text = "Dizi adı", Left = 10, Top = 15, AutoSize = true }, name,
            new Label { Text = "DB no", Left = 10, Top = 45, AutoSize = true }, db,
            new Label { Text = "Başlangıç baytı", Left = 10, Top = 75, AutoSize = true }, start,
            new Label { Text = "Eleman sayısı", Left = 10, Top = 105, AutoSize = true }, count, ok
        });
        f.AcceptButton = ok;
        if (f.ShowDialog(this) != DialogResult.OK) return;
        for (int i = 0; i < (int)count.Value; i++)
            _vars.Add(new PlcVariable
            {
                Name = $"{name.Text}[{i}]", Type = VarType.Bool, Db = (int)db.Value,
                Offset = (int)start.Value + i / 8, Bit = i % 8
            });
        Log($"{name.Text}: {(int)count.Value} Bool eklendi (DB{db.Value}, bayt {start.Value}...).");
    }

    void LoadVars()
    {
        try
        {
            if (File.Exists(_varsFile))
                foreach (var v in JsonSerializer.Deserialize<List<PlcVariable>>(File.ReadAllText(_varsFile)) ?? new()) _vars.Add(v);
        }
        catch { /* bozuk dosya: boş başla */ }
    }

    void SaveVars()
    {
        try { File.WriteAllText(_varsFile, JsonSerializer.Serialize(_vars.Where(v => v.Name != "").ToList())); }
        catch { }
    }
}
