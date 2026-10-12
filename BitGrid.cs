namespace DbIzleyici;

// 16x16 bit ızgarası: hücre i = dizi elemanı i (satır i/16, sütun i%16).
public class BitGrid : Control
{
    public const int Count = 256;
    public bool[] Values { get; } = new bool[Count];
    public bool Known { get; set; }          // PLC'den hiç veri geldi mi
    public bool Interactive { get; set; }
    public event Action<int>? CellClicked;

    int _hover = -1;

    public BitGrid()
    {
        DoubleBuffered = true;
        MinimumSize = new Size(320, 320);
    }

    Rectangle Cell(int i, int w, int h) => new((i % 16) * w, (i / 16) * h, w, h);

    int HitTest(Point p)
    {
        int w = Width / 16, h = Height / 16;
        if (w == 0 || h == 0) return -1;
        int c = p.X / w, r = p.Y / h;
        return c is < 0 or >= 16 || r is < 0 or >= 16 ? -1 : r * 16 + c;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Bg);
        int w = Width / 16, h = Height / 16;
        using var font = new Font(Theme.UiFont.FontFamily, Math.Max(6f, Math.Min(w, h) / 3.4f));
        using var on = new SolidBrush(Theme.Accent);
        using var off = new SolidBrush(Theme.Surface);
        using var unk = new SolidBrush(Theme.Surface2);
        using var txtOn = new SolidBrush(Color.FromArgb(12, 30, 22));
        using var txtOff = new SolidBrush(Theme.TextMuted);
        using var pen = new Pen(Theme.Bg, 2);
        using var hover = new Pen(Theme.Warn, 2);
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (int i = 0; i < Count; i++)
        {
            var rc = Cell(i, w, h);
            bool isOn = Known && Values[i];
            g.FillRectangle(!Known ? unk : isOn ? on : off, rc);
            g.DrawRectangle(pen, rc);
            g.DrawString(i.ToString(), font, isOn ? txtOn : txtOff, rc, fmt);
            if (Interactive && i == _hover) g.DrawRectangle(hover, rc.X + 1, rc.Y + 1, rc.Width - 3, rc.Height - 3);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = HitTest(e.Location);
        if (i != _hover) { _hover = i; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        int i = HitTest(e.Location);
        if (Interactive && i >= 0 && e.Button == MouseButtons.Left) CellClicked?.Invoke(i);
        base.OnMouseUp(e);
    }
}
