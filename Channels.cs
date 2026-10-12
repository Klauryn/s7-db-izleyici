using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace DbIzleyici;

// Bir "kanal" = PLC'de bir From dizisi + bir To dizisi (aynı DB, non-optimized).
// From yalnız okunur; To okunur ve (izin varsa) yazılır.
public abstract class ArrayChannel : Panel
{
    protected readonly NumericUpDown DbBox = new() { Width = 60, Maximum = 65535 };
    protected readonly NumericUpDown FromBox = new() { Width = 60, Maximum = 65535 };
    protected readonly NumericUpDown ToBox = new() { Width = 60, Maximum = 65535 };
    protected readonly NumericUpDown CountBox = new() { Width = 60, Minimum = 1, Maximum = 2000 };
    protected readonly CheckBox ReadBox = new() { Text = "Oku", AutoSize = true, Margin = new Padding(12, 5, 0, 0) };
    protected readonly Label StatusLabel = new() { AutoSize = true, Margin = new Padding(12, 7, 0, 0) };
    protected readonly FlowLayoutPanel Config = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };

    public Func<bool> WriteAllowed { get; set; } = () => false;
    public Action<string> Log { get; set; } = _ => { };
    public bool ReadEnabled => ReadBox.Checked;

    protected PlcClient? Plc;
    string _lastError = "";

    protected ArrayChannel(int db, int from, int to, int count, bool readByDefault)
    {
        Dock = DockStyle.Fill;
        DbBox.Value = db; FromBox.Value = from; ToBox.Value = to; CountBox.Value = count;
        ReadBox.Checked = readByDefault;
    }

    protected static Label L(string t) => new() { Text = t, AutoSize = true, Margin = new Padding(8, 7, 0, 0) };

    // content önce eklenir (Fill), ayar çubuğu sonra (Top).
    protected void Build(Control content)
    {
        Config.Controls.AddRange(new Control[] { L("DB"), DbBox, L("From bayt"), FromBox, L("To bayt"), ToBox, L("Adet"), CountBox, ReadBox, StatusLabel });
        Controls.Add(content);
        Controls.Add(Config);
    }

    // Kanala özel ayarları "Oku" kutusundan önce ekler.
    protected void AddConfig(params Control[] controls)
    {
        Config.Controls.AddRange(controls);
        int at = Config.Controls.IndexOf(ReadBox);
        foreach (var c in controls) Config.Controls.SetChildIndex(c, at++);
    }

    protected int Db => (int)DbBox.Value;
    protected int FromStart => (int)FromBox.Value;
    protected int ToStart => (int)ToBox.Value;
    protected int Count => (int)CountBox.Value;

    // Bir kanalın hatası (ör. DB yok) bağlantı döngüsünü bozmasın: bağlantı hâlâ açıksa kanalda göster.
    public async Task PollAsync(PlcClient plc, CancellationToken ct)
    {
        Plc = plc;
        try
        {
            await ReadCoreAsync(ct);
            StatusLabel.Text = "";
            _lastError = "";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (plc.IsConnected)
        {
            StatusLabel.Text = "Hata: " + ex.Message;
            StatusLabel.ForeColor = Color.Firebrick;
            if (ex.Message != _lastError) { _lastError = ex.Message; Log($"{Name}: {ex.Message}"); }
        }
    }

    protected abstract Task ReadCoreAsync(CancellationToken ct);

    protected bool CanWrite()
    {
        if (!WriteAllowed()) { Log("Yazma kapalı: önce 'Yazmaya izin ver'i işaretle."); return false; }
        if (Plc is null || !Plc.IsConnected) { Log("Önce bağlan."); return false; }
        return true;
    }
}

// ---- Bool: 16x16 bit ızgarası ----
public sealed class BoolChannel : ArrayChannel
{
    readonly BitGrid _from = new() { Dock = DockStyle.Fill };
    readonly BitGrid _to = new() { Dock = DockStyle.Fill, Interactive = true };

    public BoolChannel(int db, int from, int to) : base(db, from, to, BitGrid.Count, readByDefault: true)
    {
        Name = "Bool";
        CountBox.Enabled = false; // 16x16 sabit
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        split.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        split.Controls.Add(new Label { Text = "From (yalnız okuma)", AutoSize = true }, 0, 0);
        split.Controls.Add(new Label { Text = "To (tıkla: ters çevir ve yaz)", AutoSize = true }, 1, 0);
        split.Controls.Add(_from, 0, 1);
        split.Controls.Add(_to, 1, 1);
        _to.CellClicked += async i => await ToggleAsync(i);
        Build(split);
    }

    protected override async Task ReadCoreAsync(CancellationToken ct)
    {
        Fill(_from, await Plc!.ReadRangeAsync(Db, FromStart, 32, ct));
        Fill(_to, await Plc!.ReadRangeAsync(Db, ToStart, 32, ct));
    }

    static void Fill(BitGrid g, byte[] buf)
    {
        for (int i = 0; i < BitGrid.Count; i++) g.Values[i] = ((buf[i / 8] >> (i % 8)) & 1) == 1;
        g.Known = true;
        g.Invalidate();
    }

    async Task ToggleAsync(int i)
    {
        if (!CanWrite()) return;
        bool v = !_to.Values[i];
        try
        {
            await Plc!.WriteBitAsync(Db, ToStart + i / 8, i % 8, v, CancellationToken.None);
            _to.Values[i] = v; _to.Invalidate();
            Log($"Yazıldı: DB{Db} To[{i}] = {(v ? "TRUE" : "FALSE")}");
        }
        catch (Exception ex) { Log($"Yazma hatası: {ex.Message}"); }
    }
}

// ---- Tablo kanalları: satır = dizi elemanı, sütunlar No / From / To (To düzenlenebilir) ----
public abstract class TableChannel : ArrayChannel
{
    protected readonly DataGridView Grid = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect
    };

    protected TableChannel(int db, int from, int to, int count, bool readByDefault)
        : base(db, from, to, count, readByDefault)
    {
        Grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "No", Width = 50, ReadOnly = true });
        Grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "From", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        Grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "To (düzenle: Enter ile yaz)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        Grid.CellEndEdit += async (_, e) => { if (e.ColumnIndex == 2 && e.RowIndex >= 0) await WriteRowAsync(e.RowIndex); };
        Build(Grid);
    }

    protected abstract int ElemSize { get; }
    protected abstract string Decode(byte[] buf, int offset);
    // Elemanın başından (ofset, yazılacak baytlar). Geçersiz değerde FormatException/OverflowException.
    protected abstract (int Offset, byte[] Data) Encode(string text);

    protected override async Task ReadCoreAsync(CancellationToken ct)
    {
        int n = Count, es = ElemSize;
        while (Grid.Rows.Count < n) Grid.Rows.Add(Grid.Rows.Count.ToString(), "", "");
        while (Grid.Rows.Count > n) Grid.Rows.RemoveAt(Grid.Rows.Count - 1);

        var f = await Plc!.ReadRangeAsync(Db, FromStart, n * es, ct);
        var t = await Plc!.ReadRangeAsync(Db, ToStart, n * es, ct);
        var editing = Grid.CurrentCell is { IsInEditMode: true } c ? c.RowIndex : -1;
        for (int i = 0; i < n; i++)
        {
            Grid.Rows[i].Cells[1].Value = Decode(f, i * es);
            if (i != editing) Grid.Rows[i].Cells[2].Value = Decode(t, i * es);
        }
    }

    async Task WriteRowAsync(int i)
    {
        if (!CanWrite()) return;
        var text = Grid.Rows[i].Cells[2].Value?.ToString() ?? "";
        try
        {
            var (off, data) = Encode(text);
            await Plc!.WriteBytesAsync(Db, ToStart + i * ElemSize + off, data, CancellationToken.None);
            Log($"Yazıldı: DB{Db} To[{i}] = {text}");
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            Log($"Geçersiz değer (To[{i}]): {text}");
        }
        catch (Exception ex) { Log($"Yazma hatası: {ex.Message}"); }
    }
}

// ---- Sayı: LReal (double), Real, DInt, Int ----
public sealed class NumberChannel : TableChannel
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };

    public NumberChannel(int db, int from, int to, int count)
        : base(db, from, to, count, readByDefault: false)
    {
        Name = "Sayı";
        _type.Items.AddRange(new object[] { "LReal (double)", "Real (float)", "DInt", "Int" });
        _type.SelectedIndex = 0;
        AddConfig(L("Tip"), _type);
    }

    protected override int ElemSize => _type.SelectedIndex switch { 0 => 8, 1 => 4, 2 => 4, _ => 2 };

    protected override string Decode(byte[] b, int o)
    {
        var s = b.AsSpan(o);
        return _type.SelectedIndex switch
        {
            0 => BinaryPrimitives.ReadDoubleBigEndian(s).ToString("G15", Inv),
            1 => BinaryPrimitives.ReadSingleBigEndian(s).ToString("G9", Inv),
            2 => BinaryPrimitives.ReadInt32BigEndian(s).ToString(Inv),
            _ => BinaryPrimitives.ReadInt16BigEndian(s).ToString(Inv)
        };
    }

    protected override (int, byte[]) Encode(string text)
    {
        text = text.Trim().Replace(',', '.');
        byte[] d = new byte[ElemSize];
        switch (_type.SelectedIndex)
        {
            case 0: BinaryPrimitives.WriteDoubleBigEndian(d, double.Parse(text, Inv)); break;
            case 1: BinaryPrimitives.WriteSingleBigEndian(d, float.Parse(text, Inv)); break;
            case 2: BinaryPrimitives.WriteInt32BigEndian(d, int.Parse(text, Inv)); break;
            default: BinaryPrimitives.WriteInt16BigEndian(d, short.Parse(text, Inv)); break;
        }
        return (0, d);
    }
}

// ---- String: Array of String[MaxLen]; eleman = 2 başlık baytı + MaxLen bayt ----
public sealed class StringChannel : TableChannel
{
    readonly NumericUpDown _maxLen = new() { Width = 55, Minimum = 1, Maximum = 254, Value = 254 };

    public StringChannel(int db, int from, int to, int count)
        : base(db, from, to, count, readByDefault: false)
    {
        Name = "String";
        AddConfig(L("String[N], N ="), _maxLen);
    }

    protected override int ElemSize => 2 + (int)_maxLen.Value;

    protected override string Decode(byte[] b, int o)
    {
        int len = Math.Min(b[o + 1], (int)_maxLen.Value);
        return Encoding.Latin1.GetString(b, o + 2, len);
    }

    // Yalnız "gerçek uzunluk" baytı ve karakterler yazılır; maksimum uzunluk baytına dokunulmaz.
    protected override (int, byte[]) Encode(string text)
    {
        var chars = Encoding.Latin1.GetBytes(text); // Latin1 dışı karakterler '?' olur
        int max = (int)_maxLen.Value;
        if (chars.Length > max) { Log($"Metin {max} karaktere kısaltıldı."); chars = chars[..max]; }
        var data = new byte[1 + chars.Length];
        data[0] = (byte)chars.Length;
        chars.CopyTo(data, 1);
        return (1, data);
    }
}
