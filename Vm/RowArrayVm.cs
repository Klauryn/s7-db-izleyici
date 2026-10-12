using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using S7.Net;

namespace DbIzleyici.Vm;

public partial class ElemVm : ObservableObject
{
    public int Index { get; }
    public ElemVm(int index) => Index = index;

    [ObservableProperty] private string _from = "";
    [ObservableProperty] private string _to = "";
}

// Satır tablosu olan diziler (Sayı, String): satır = eleman; To düzenlenip yazılır.
public abstract partial class RowArrayVm : ArrayChannelVm
{
    public ObservableCollection<ElemVm> Rows { get; } = new();

    // Düzenlenen satırın To değeri okuma turunda üzerine yazılmasın diye.
    public int EditingIndex { get; set; } = -1;

    protected RowArrayVm(string title, PlcClient plc, int db, int from, int to, int count)
        : base(title, plc, db, from, to, count, read: false) { }

    public abstract int ElemSize { get; }
    protected abstract string Decode(byte[] buf, int offset);
    public abstract Task WriteRowAsync(int index, string text);

    protected override async Task ReadCoreAsync(CancellationToken ct)
    {
        EnsureRows();
        int es = ElemSize;
        var from = await Plc.ReadRangeAsync(Db, FromStart, Count * es, ct);
        var to = await Plc.ReadRangeAsync(Db, ToStart, Count * es, ct);
        for (int i = 0; i < Count; i++)
        {
            Rows[i].From = Decode(from, i * es);
            if (i != EditingIndex) Rows[i].To = Decode(to, i * es);
        }
    }

    void EnsureRows()
    {
        while (Rows.Count < Count) Rows.Add(new ElemVm(Rows.Count));
        while (Rows.Count > Count) Rows.RemoveAt(Rows.Count - 1);
    }
}

public enum NumberType { LReal, Real, DInt, Int }

public partial class NumberArrayVm : RowArrayVm
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public NumberType[] Types { get; } = Enum.GetValues<NumberType>();
    [ObservableProperty] private NumberType _type = NumberType.LReal;

    public NumberArrayVm(PlcClient plc, int db, int from, int to, int count)
        : base("Sayı", plc, db, from, to, count) { }

    public override int ElemSize => Type switch
    {
        NumberType.LReal => 8,
        NumberType.Real or NumberType.DInt => 4,
        _ => 2
    };

    protected override string Decode(byte[] buf, int o)
    {
        var s = buf.AsSpan(o);
        return Type switch
        {
            NumberType.LReal => BinaryPrimitives.ReadDoubleBigEndian(s).ToString("G15", Inv),
            NumberType.Real => BinaryPrimitives.ReadSingleBigEndian(s).ToString("G9", Inv),
            NumberType.DInt => BinaryPrimitives.ReadInt32BigEndian(s).ToString(Inv),
            _ => BinaryPrimitives.ReadInt16BigEndian(s).ToString(Inv)
        };
    }

    public override async Task WriteRowAsync(int i, string text)
    {
        if (!CanWrite()) return;
        try
        {
            var data = Encode(text);
            await Plc.WriteBytesAsync(Db, ToStart + i * ElemSize, data, CancellationToken.None);
            Log($"Yazıldı: DB{Db} To[{i}] = {text}");
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            Log($"Geçersiz değer (To[{i}]): {text}");
        }
        catch (Exception ex) { Log($"Yazma hatası: {ex.Message}"); }
    }

    byte[] Encode(string text)
    {
        text = text.Trim().Replace(',', '.');
        var d = new byte[ElemSize];
        switch (Type)
        {
            case NumberType.LReal: BinaryPrimitives.WriteDoubleBigEndian(d, double.Parse(text, Inv)); break;
            case NumberType.Real: BinaryPrimitives.WriteSingleBigEndian(d, float.Parse(text, Inv)); break;
            case NumberType.DInt: BinaryPrimitives.WriteInt32BigEndian(d, int.Parse(text, Inv)); break;
            default: BinaryPrimitives.WriteInt16BigEndian(d, short.Parse(text, Inv)); break;
        }
        return d;
    }
}

// Array of String[N]: eleman = 2 başlık baytı (maks. uzunluk, gerçek uzunluk) + N bayt.
public partial class StringArrayVm : RowArrayVm
{
    [ObservableProperty] private int _maxLen = 254;

    public StringArrayVm(PlcClient plc, int db, int from, int to, int count)
        : base("String", plc, db, from, to, count) { }

    public override int ElemSize => 2 + MaxLen;

    protected override string Decode(byte[] buf, int o)
    {
        int len = Math.Min(buf[o + 1], MaxLen);
        return Encoding.Latin1.GetString(buf, o + 2, len);
    }

    public override async Task WriteRowAsync(int i, string text)
    {
        if (!CanWrite()) return;
        var chars = Encoding.Latin1.GetBytes(text);   // Latin1 dışı karakterler '?' olur
        if (chars.Length > MaxLen) { Log($"Metin {MaxLen} karaktere kısaltıldı."); chars = chars[..MaxLen]; }
        var data = new byte[1 + chars.Length];
        data[0] = (byte)chars.Length;
        chars.CopyTo(data, 1);
        try
        {
            // Maksimum uzunluk baytına dokunulmaz; yalnız gerçek uzunluk ve karakterler yazılır.
            await Plc.WriteBytesAsync(Db, ToStart + i * ElemSize + 1, data, CancellationToken.None);
            Log($"Yazıldı: DB{Db} To[{i}] = {text}");
        }
        catch (Exception ex) { Log($"Yazma hatası: {ex.Message}"); }
    }
}
