using CommunityToolkit.Mvvm.ComponentModel;
using S7.Net;

namespace DbIzleyici.Vm;

// Bir From/To dizisi çifti: From yalnız okunur, To okunur ve (izin varsa) yazılır.
public abstract partial class ArrayChannelVm : ObservableObject
{
    public string Title { get; }
    public PlcClient Plc { get; }
    public Func<bool> WriteAllowed { get; set; } = () => false;
    public Action<string> Log { get; set; } = _ => { };

    [ObservableProperty] private int _db;
    [ObservableProperty] private int _fromStart;
    [ObservableProperty] private int _toStart;
    [ObservableProperty] private int _count;
    [ObservableProperty] private bool _isReadEnabled;
    [ObservableProperty] private string _error = "";

    private string _lastError = "";

    protected ArrayChannelVm(string title, PlcClient plc, int db, int from, int to, int count, bool read)
    {
        Title = title;
        Plc = plc;
        _db = db;
        _fromStart = from;
        _toStart = to;
        _count = count;
        _isReadEnabled = read;
    }

    // Bir kanalın hatası (ör. DB yok) bağlantı döngüsünü bozmasın: bağlantı açıksa yalnız bu kanalda gösterilir.
    public async Task PollAsync(CancellationToken ct)
    {
        if (!IsReadEnabled) return;
        try
        {
            await ReadCoreAsync(ct);
            Error = "";
            _lastError = "";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (Plc.IsConnected)
        {
            Error = ex.Message;
            if (ex.Message != _lastError)
            {
                _lastError = ex.Message;
                Log($"{Title}: {ex.Message}");
            }
        }
    }

    protected abstract Task ReadCoreAsync(CancellationToken ct);

    protected bool CanWrite()
    {
        if (!WriteAllowed()) { Log("Yazma kapalı: önce 'Yazmaya izin ver'i işaretle."); return false; }
        if (!Plc.IsConnected) { Log("Önce bağlan."); return false; }
        return true;
    }
}
