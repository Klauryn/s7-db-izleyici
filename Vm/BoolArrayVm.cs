using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using S7.Net;

namespace DbIzleyici.Vm;

// 16x16 bit ızgarası: Array[0..255] of Bool = 32 bayt. Hücre i = bayt i/8, bit i%8.
public partial class BoolArrayVm : ArrayChannelVm
{
    public const int Bits = 256;

    public ObservableCollection<BitCellVm> From { get; } = new();
    public ObservableCollection<BitCellVm> To { get; } = new();

    public BoolArrayVm(PlcClient plc, int db, int from, int to)
        : base("Bool (16×16)", plc, db, from, to, Bits, read: true)
    {
        for (int i = 0; i < Bits; i++)
        {
            From.Add(new BitCellVm(i));
            To.Add(new BitCellVm(i));
        }
    }

    protected override async Task ReadCoreAsync(CancellationToken ct)
    {
        Apply(From, await Plc.ReadRangeAsync(Db, FromStart, Bits / 8, ct));
        Apply(To, await Plc.ReadRangeAsync(Db, ToStart, Bits / 8, ct));
    }

    static void Apply(ObservableCollection<BitCellVm> cells, byte[] buf)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            cells[i].IsOn = ((buf[i / 8] >> (i % 8)) & 1) == 1;
            cells[i].Known = true;
        }
    }

    // To hücresini ters çevirip PLC'ye yaz.
    public async Task ToggleAsync(int i)
    {
        if (!CanWrite()) return;
        bool v = !To[i].IsOn;
        try
        {
            await Plc.WriteBitAsync(Db, ToStart + i / 8, i % 8, v, CancellationToken.None);
            To[i].IsOn = v;
            Log($"Yazıldı: DB{Db} To[{i}] = {(v ? "TRUE" : "FALSE")}");
        }
        catch (Exception ex) { Log($"Yazma hatası: {ex.Message}"); }
    }
}

public partial class BitCellVm : ObservableObject
{
    public int Index { get; }
    public BitCellVm(int index) => Index = index;

    [ObservableProperty] private bool _isOn;
    [ObservableProperty] private bool _known;
}
