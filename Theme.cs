using System.Drawing.Drawing2D;

namespace DbIzleyici;

// Koyu tema: tüm renkler ve kontrol stilleri burada. Form kurulduktan sonra Apply(this) çağrılır.
public static class Theme
{
    public static readonly Color Bg = Color.FromArgb(18, 22, 30);        // form zemini
    public static readonly Color Surface = Color.FromArgb(28, 34, 45);   // kart / panel
    public static readonly Color Surface2 = Color.FromArgb(38, 46, 60);  // giriş alanı, başlık
    public static readonly Color Border = Color.FromArgb(55, 65, 82);
    public static readonly Color Text = Color.FromArgb(230, 234, 240);
    public static readonly Color TextMuted = Color.FromArgb(140, 150, 168);
    public static readonly Color Accent = Color.FromArgb(61, 220, 151);  // mint: açık/bağlı/yazıldı
    public static readonly Color AccentDim = Color.FromArgb(38, 120, 88);
    public static readonly Color Bad = Color.FromArgb(255, 107, 107);
    public static readonly Color Warn = Color.FromArgb(255, 180, 84);

    public static readonly Font UiFont = new("Segoe UI", 9.5f);
    public static readonly Font MonoFont = new("Consolas", 9.5f);

    public static void Apply(Control root)
    {
        Style(root);
        foreach (Control c in root.Controls) Apply(c);
    }

    static void Style(Control c)
    {
        switch (c)
        {
            case Form f:
                f.BackColor = Bg; f.ForeColor = Text; f.Font = UiFont;
                break;
            case TabPage p:
                p.BackColor = Bg; p.ForeColor = Text;
                break;
            case Panel:
                c.BackColor = Surface; c.ForeColor = Text; c.Font = UiFont;
                break;
            case Label l:
                l.ForeColor = l.ForeColor == Bad || l.ForeColor == Warn || l.ForeColor == Accent ? l.ForeColor : Text;
                l.BackColor = Color.Transparent; l.Font = UiFont;
                break;
            case Button b:
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = Surface2; b.ForeColor = Text; b.Font = UiFont;
                b.FlatAppearance.BorderColor = Border; b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.MouseOverBackColor = Color.FromArgb(52, 62, 80);
                b.FlatAppearance.MouseDownBackColor = Color.FromArgb(30, 37, 50);
                b.Cursor = Cursors.Hand;
                break;
            case TextBox t:
                t.BackColor = Surface2; t.ForeColor = Text; t.BorderStyle = BorderStyle.FixedSingle; t.Font = UiFont;
                break;
            case NumericUpDown n:
                n.BackColor = Surface2; n.ForeColor = Text; n.BorderStyle = BorderStyle.FixedSingle; n.Font = UiFont;
                break;
            case ComboBox cb:
                cb.BackColor = Surface2; cb.ForeColor = Text; cb.FlatStyle = FlatStyle.Flat; cb.Font = UiFont;
                break;
            case CheckBox ck:
                ck.ForeColor = Text; ck.BackColor = Color.Transparent; ck.Font = UiFont;
                break;
            case DataGridView g:
                StyleGrid(g);
                break;
            case TabControl tc:
                tc.DrawMode = TabDrawMode.OwnerDrawFixed;
                tc.ItemSize = new Size(215, 32);
                tc.SizeMode = TabSizeMode.Fixed;
                tc.Padding = new Point(14, 6);
                tc.DrawItem -= DrawTab;
                tc.DrawItem += DrawTab;
                tc.BackColor = Bg;
                // Görsel stil açıkken sekme şeridinin boş kısmı açık gri kalıyor; stili bu denetim için kapat.
                if (tc.IsHandleCreated) NoVisualStyle(tc);
                else tc.HandleCreated += (_, _) => NoVisualStyle(tc);
                break;
        }
    }

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    static void NoVisualStyle(Control c)
    {
        try { SetWindowTheme(c.Handle, "", ""); } catch { }
    }

    static void StyleGrid(DataGridView g)
    {
        g.EnableHeadersVisualStyles = false;
        g.BackgroundColor = Surface;
        g.BorderStyle = BorderStyle.None;
        g.GridColor = Border;
        g.ForeColor = Text;
        g.Font = UiFont;
        g.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface2, ForeColor = TextMuted, SelectionBackColor = Surface2,
            Font = new Font(UiFont, FontStyle.Bold), Padding = new Padding(6, 0, 0, 0)
        };
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        g.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface, ForeColor = Text, SelectionBackColor = AccentDim, SelectionForeColor = Color.White,
            Padding = new Padding(4, 0, 0, 0)
        };
        g.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(33, 40, 53), ForeColor = Text, SelectionBackColor = AccentDim, SelectionForeColor = Color.White
        };
        g.RowHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Surface2, SelectionBackColor = Surface2 };
        g.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
    }

    // Sekme başlıkları: seçili olan mint çizgili, diğerleri sönük.
    static void DrawTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tc) return;
        var page = tc.TabPages[e.Index];
        bool selected = e.Index == tc.SelectedIndex;
        var rect = e.Bounds;
        using var back = new SolidBrush(selected ? Surface : Bg);
        e.Graphics.FillRectangle(back, rect);
        if (selected)
        {
            using var line = new SolidBrush(Accent);
            e.Graphics.FillRectangle(line, rect.X, rect.Bottom - 3, rect.Width, 3);
        }
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var txt = new SolidBrush(selected ? Text : TextMuted);
        e.Graphics.DrawString(page.Text, UiFont, txt, rect, fmt);
    }
}
