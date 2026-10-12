namespace DbIzleyici;

public enum VarType { Bool, Byte, Int, Word, DInt, Real }

public class PlcVariable
{
    public string Name { get; set; } = "";
    public VarType Type { get; set; } = VarType.Int;
    public int Db { get; set; } = 1;
    public int Offset { get; set; }
    public int Bit { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string Value { get; set; } = "";

    public int Size => Type switch
    {
        VarType.Bool or VarType.Byte => 1,
        VarType.Int or VarType.Word => 2,
        _ => 4
    };

    // S7 verisi big-endian; bayt dizisinden (aralığın başlangıcı start) değeri çöz.
    public string Decode(byte[] buf, int start)
    {
        int i = Offset - start;
        if (i < 0 || i + Size > buf.Length) return "?";
        return Type switch
        {
            VarType.Bool => ((buf[i] >> Bit) & 1) == 1 ? "TRUE" : "FALSE",
            VarType.Byte => buf[i].ToString(),
            VarType.Word => ((ushort)(buf[i] << 8 | buf[i + 1])).ToString(),
            VarType.Int => ((short)(buf[i] << 8 | buf[i + 1])).ToString(),
            VarType.DInt => ((int)(buf[i] << 24 | buf[i + 1] << 16 | buf[i + 2] << 8 | buf[i + 3])).ToString(),
            VarType.Real => BitConverter.Int32BitsToSingle(buf[i] << 24 | buf[i + 1] << 16 | buf[i + 2] << 8 | buf[i + 3])
                                .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            _ => "?"
        };
    }

    // Yazılacak değeri bayt dizisine çevir (Bool için kullanılmaz, bit yazılır).
    public byte[] Encode(string text)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        text = text.Trim().Replace(',', '.');
        switch (Type)
        {
            case VarType.Byte: return new[] { byte.Parse(text, inv) };
            case VarType.Word: { var v = ushort.Parse(text, inv); return new[] { (byte)(v >> 8), (byte)v }; }
            case VarType.Int: { var v = short.Parse(text, inv); return new[] { (byte)(v >> 8), (byte)v }; }
            case VarType.DInt: { var v = int.Parse(text, inv); return new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v }; }
            case VarType.Real:
                {
                    var v = BitConverter.SingleToInt32Bits(float.Parse(text, inv));
                    return new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
                }
            default: throw new InvalidOperationException("Bool için bit yazılır.");
        }
    }
}
