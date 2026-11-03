using GtaAtlas.App.Browsing;
using GtaAtlas.App.Ui.Controls;
using GtaAtlas.Data;

namespace GtaAtlas.App.Ui;

/// <summary>
/// Scrollable info column of the detail window: hashes, tags, kind-specific properties and source files.
/// Every value row copies on click.
/// </summary>
internal sealed class DetailView : ThemedControl
{
    private abstract record Block(Rectangle Bounds);
    private sealed record HeaderBlock(Rectangle Bounds, string Text) : Block(Bounds);
    private sealed record RowBlock(Rectangle Bounds, string Label, string Value, bool Mono) : Block(Bounds);
    private sealed record ChipsBlock(IReadOnlyList<(string Text, Rectangle Bounds)> Chips) : Block(Rectangle.Empty);

    private const TextFormatFlags Wrap = TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;

    private readonly List<Block> _blocks = [];
    private AssetDetail? _detail;
    private readonly SmoothScroll _smooth;
    private int _contentHeight;
    private RowBlock? _hover;

    public DetailView()
    {
        BackColor = Theme.Surface;
        _smooth = new SmoothScroll(this, () => MaxScroll);
        _smooth.Changed += (_, _) =>
        {
            UpdateHover(PointToClient(MousePosition));
            Invalidate();
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _smooth.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>Raised with the value the user clicked.</summary>
    public event EventHandler<string>? Copied;

    public void Display(AssetDetail? detail)
    {
        _detail = detail;
        Rebuild();
        _smooth.Jump(0);
        Invalidate();
    }

    private int LabelWidth => Px(118);
    private int Pad => Px(24);

    private void Rebuild()
    {
        _blocks.Clear();
        _hover = null;
        if (_detail is null || Width <= 0)
            return;

        var a = _detail.Asset;
        var y = Px(4);
        var width = Width - Pad * 2;

        void Header(string text)
        {
            y += Px(14);
            _blocks.Add(new HeaderBlock(new Rectangle(Pad, y, width, Px(22)), text.ToUpperInvariant()));
            y += Px(26);
        }

        void Row(string label, string value, bool mono = false)
        {
            var font = mono ? Theme.Mono : Theme.UiFont;
            var valueWidth = width - LabelWidth - Px(40);
            var h = Math.Max(Px(34), TextRenderer.MeasureText(value, font, new Size(valueWidth, 0), Wrap).Height + Px(16));
            _blocks.Add(new RowBlock(new Rectangle(Pad, y, width, h), label, value, mono));
            y += h;
        }

        Header("Hash");
        Row("Joaat", a.Hash.ToString(), true);
        Row("Signed", HashText.ToSigned(a.Hash), true);
        Row("Hex", HashText.ToHex(a.Hash), true);

        Header("Identity");
        Row("Name", a.Name);
        if (a.Key != a.Name)
            Row("Key", a.Key);
        if (a.DisplayName is not null)
            Row("Display name", a.DisplayName);
        Row("DLC", a.Dlc);
        if (_detail.Parent is { } parent)
            Row(AssetText.KindTitle(parent.Kind), parent.Name);

        if (_detail.Tags.Count > 0)
        {
            Header("Tags");
            var chips = new List<(string, Rectangle)>();
            var x = Pad;
            var chipHeight = Px(26);
            foreach (var tag in _detail.Tags)
            {
                var w = TextRenderer.MeasureText(tag, Theme.Small).Width + Px(18);
                if (x + w > Pad + width && x > Pad)
                {
                    x = Pad;
                    y += chipHeight + Px(6);
                }
                chips.Add((tag, new Rectangle(x, y, w, chipHeight)));
                x += w + Px(6);
            }
            y += chipHeight + Px(4);
            _blocks.Add(new ChipsBlock(chips));
        }

        var data = AssetText.DataFields(_detail.Data);
        if (data.Count > 0)
        {
            Header("Properties");
            foreach (var f in data)
                Row(f.Label, f.Value);
        }

        Header(_detail.Sources.Count > 1 ? $"Sources · {_detail.Sources.Count}, later override earlier" : "Source");
        foreach (var s in _detail.Sources)
            Row(s.Dlc, s.Path, true);

        _contentHeight = y + Px(24);
        _smooth?.Clamp();
    }

    private int MaxScroll => Math.Max(0, _contentHeight - Height);

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Rebuild();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.TranslateTransform(0, -_smooth.Value);

        foreach (var block in _blocks)
        {
            switch (block)
            {
                case HeaderBlock h:
                    TextRenderer.DrawText(g, h.Text, Theme.Caption, h.Bounds, Theme.Faint, Theme.Line);
                    break;
                case RowBlock r:
                    PaintRow(g, r);
                    break;
                case ChipsBlock c:
                    foreach (var (text, bounds) in c.Chips)
                    {
                        Theme.FillRounded(g, bounds, bounds.Height / 2, Theme.Raised, Theme.Border);
                        TextRenderer.DrawText(g, text, Theme.Small, bounds, Theme.Text, Theme.Centered);
                    }
                    break;
            }
        }

        g.ResetTransform();
        if (MaxScroll > 0)
        {
            var h = Math.Max(Px(30), Height * Height / _contentHeight);
            var y = (Height - h) * _smooth.Value / MaxScroll;
            Theme.FillRounded(g, new Rectangle(Width - Px(6), y, Px(3), h), Px(1), Theme.BorderStrong);
        }
    }

    private void PaintRow(Graphics g, RowBlock r)
    {
        var hover = r == _hover;
        if (hover)
        {
            Theme.FillRounded(g, Rectangle.Inflate(r.Bounds, Px(10), 0), Px(8), Theme.Hover);
        }
        else
        {
            using var pen = new Pen(Color.FromArgb(0x22, 0x24, 0x2A));
            g.DrawLine(pen, r.Bounds.Left, r.Bounds.Bottom - 1, r.Bounds.Right, r.Bounds.Bottom - 1);
        }

        var label = new Rectangle(r.Bounds.X, r.Bounds.Y + Px(8), LabelWidth - Px(8), r.Bounds.Height - Px(16));
        TextRenderer.DrawText(g, r.Label, Theme.UiFont, label, Theme.Muted, Wrap | TextFormatFlags.EndEllipsis);
        var value = new Rectangle(r.Bounds.X + LabelWidth, r.Bounds.Y + Px(8), r.Bounds.Width - LabelWidth - Px(40), r.Bounds.Height - Px(8));
        TextRenderer.DrawText(g, r.Value, r.Mono ? Theme.Mono : Theme.UiFont, value, Theme.Text, Wrap);
        if (hover)
            Theme.DrawIcon(g, Glyph.Copy, new Rectangle(r.Bounds.Right - Px(28), r.Bounds.Y, Px(24), Math.Min(r.Bounds.Height, Px(34))), Theme.Accent, Theme.IconSmall);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        _smooth.By(-e.Delta * Px(100) / 120);
        base.OnMouseWheel(e);
    }

    private void UpdateHover(Point p)
    {
        var content = p with { Y = p.Y + _smooth.Value };
        var hit = _blocks.OfType<RowBlock>().FirstOrDefault(r => Rectangle.Inflate(r.Bounds, Px(10), 0).Contains(content));
        if (hit != _hover)
        {
            _hover = hit;
            Cursor = hit is null ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        UpdateHover(e.Location);
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = null;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _hover is { } r)
            Copied?.Invoke(this, r.Value);
        base.OnMouseClick(e);
    }
}
