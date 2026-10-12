using S7.Net;

namespace DbIzleyici;

// Plc nesnesi thread-safe değil: tüm çağrılar tek kilit üzerinden geçer.
public sealed class PlcClient : IDisposable
{
    readonly SemaphoreSlim _gate = new(1, 1);
    Plc? _plc;

    public bool IsConnected => _plc?.IsConnected == true;

    public async Task ConnectAsync(CpuType cpu, string ip, short rack, short slot, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _plc?.Close();
            _plc = new Plc(cpu, ip, rack, slot);
            await _plc.OpenAsync(ct);
        }
        finally { _gate.Release(); }
    }

    public async Task DisconnectAsync()
    {
        await _gate.WaitAsync();
        try { _plc?.Close(); }
        finally { _gate.Release(); }
    }

    // Tek DB için [start, start+count) aralığını tek istekte okur (S7.Net büyük aralığı PDU'ya böler).
    public async Task<byte[]> ReadRangeAsync(int db, int start, int count, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_plc is null || !_plc.IsConnected) throw new InvalidOperationException("Bağlı değil.");
            return await _plc.ReadBytesAsync(DataType.DataBlock, db, start, count, ct);
        }
        finally { _gate.Release(); }
    }

    public async Task WriteBytesAsync(int db, int offset, byte[] data, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_plc is null || !_plc.IsConnected) throw new InvalidOperationException("Bağlı değil.");
            await _plc.WriteBytesAsync(DataType.DataBlock, db, offset, data, ct);
        }
        finally { _gate.Release(); }
    }

    public async Task WriteBitAsync(int db, int byteOffset, int bit, bool value, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_plc is null || !_plc.IsConnected) throw new InvalidOperationException("Bağlı değil.");
            await _plc.WriteBitAsync(DataType.DataBlock, db, byteOffset, bit, value, ct);
        }
        finally { _gate.Release(); }
    }

    public async Task WriteAsync(PlcVariable v, string text, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_plc is null || !_plc.IsConnected) throw new InvalidOperationException("Bağlı değil.");
            if (v.Type == VarType.Bool)
            {
                bool value = text.Trim().ToLowerInvariant() is "1" or "true";
                await _plc.WriteBitAsync(DataType.DataBlock, v.Db, v.Offset, v.Bit, value, ct);
            }
            else
            {
                await _plc.WriteBytesAsync(DataType.DataBlock, v.Db, v.Offset, v.Encode(text), ct);
            }
        }
        finally { _gate.Release(); }
    }

    public void Dispose() { _plc?.Close(); _gate.Dispose(); }
}
