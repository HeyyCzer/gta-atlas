using GtaAtlas.App.Browsing;

namespace GtaAtlas.App.Ui.Controls;

/// <summary>
/// Left navigation: brand, grouped categories with icons and counts, and a footer with index status and, while the
/// game files load for 3D previews, a progress bar.
/// </summary>
internal sealed class NavSidebar : ThemedControl
{
    private static readonly (string Header, Category[] Items)[] Sections =
    [
        ("Browse", [Category.Props, Category.Vehicles, Category.Peds, Category.Clothes, Category.Animations, Category.AnimDicts]),
        ("World", [Category.Mlos, Category.Ipls, Category.Ymaps]),
        ("Library", [Category.All, Category.Dlcs]),
    ];

    private readonly List<(Category Category, Rectangle Bounds)> _rows = [];
    private readonly PillButton _reindex = new() { Icon = Glyph.Sync, Text = "Reindex" };
    private Category _selected = Category.Props;
    private Category? _hover;
    private Func<Category, int?> _count = _ => null;
    private string _footer = "";
    private string? _loading;
    private string? _loadingDetail;
    private double? _loadingValue;

    public NavSidebar()
    {
        BackColor = Theme.Surface;
        Width = Px(232);
        _reindex.BackColor = Theme.Surface;
        _reindex.Click += (_, _) => ReindexClicked?.Invoke(this, EventArgs.Empty);
        Controls.Add(_reindex);
    }

    public event EventHandler? SelectedChanged;
    public event EventHandler? ReindexClicked;

    public Category Selected
    {
        get => _selected;
        set
        {
            if (value == _selected)
                return;
            _selected = value;
            Invalidate();
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetCounts(Func<Category, int?> count, string footer)
    {
        _count = count;
        _footer = footer;
        Invalidate();
    }

    /// <summary>
    /// Shows a background load above the footer: title and percentage, bar, detail line. <paramref name="value"/>
    /// null = indeterminate bar; <paramref name="title"/> null hides it all.
    /// </summary>
    public void SetLoading(string? title, string? detail = null, double? value = null)
    {
        _loading = title;
        _loadingDetail = detail;
        _loadingValue = value;
        Invalidate();
    }

    private static string IconOf(Category c) => c.Id switch
    {
        "props" => Glyph.Package,
        "vehicles" => Glyph.Car,
        "peds" => Glyph.Person,
        "clothes" => Glyph.Shop,
        "anims" => Glyph.Play,
        "animdicts" => Glyph.Library,
        "mlos" => Glyph.Home,
        "ipls" => Glyph.Layers,
        "ymaps" => Glyph.MapPin,
        "all" => Glyph.Apps,
        "dlcs" => Glyph.Puzzle,
        _ => Glyph.Cube,
    };

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        _reindex.Location = new Point(Px(16), Height - _reindex.Height - Px(16));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        using (var pen = new Pen(Theme.Border))
            g.DrawLine(pen, Width - 1, 0, Width - 1, Height);

        // Brand
        var logo = new Rectangle(Px(18), Px(18), Px(30), Px(30));
        g.DrawIcon(AppIcon.Get(), logo);
        TextRenderer.DrawText(g, "GTA Atlas", Theme.Title, new Rectangle(logo.Right + Px(10), logo.Y - Px(2), Width, logo.Height + Px(4)), Theme.Text, Theme.Line);

        _rows.Clear();
        var y = Px(70);
        var rowHeight = Px(36);
        foreach (var (header, items) in Sections)
        {
            y += Px(10);
            TextRenderer.DrawText(g, header.ToUpperInvariant(), Theme.Caption, new Rectangle(Px(22), y, Width, Px(20)), Theme.Faint, Theme.Line);
            y += Px(24);
            foreach (var c in items)
            {
                var r = new Rectangle(Px(10), y, Width - Px(20), rowHeight);
                _rows.Add((c, r));
                var selected = c == _selected;
                if (selected)
                    Theme.FillRounded(g, r, Px(8), Theme.AccentSoft);
                else if (c == _hover)
                    Theme.FillRounded(g, r, Px(8), Theme.Hover);

                var color = selected ? Theme.Accent : c == _hover ? Theme.Text : Theme.Muted;
                Theme.DrawIcon(g, IconOf(c), new Rectangle(r.X + Px(8), r.Y, Px(24), r.Height), color);
                TextRenderer.DrawText(g, c.Title, selected ? Theme.Bold : Theme.UiFont,
                    new Rectangle(r.X + Px(40), r.Y, r.Width - Px(100), r.Height), selected ? Theme.Text : color, Theme.Line);
                if (_count(c) is { } n)
                    TextRenderer.DrawText(g, Compact(n), Theme.Small, new Rectangle(r.Right - Px(64), r.Y, Px(56), r.Height),
                        selected ? Theme.Accent : Theme.Faint, Theme.Line | TextFormatFlags.Right);
                y += rowHeight + Px(2);
            }
        }

        var footer = new Rectangle(Px(18), _reindex.Top - Px(50), Width - Px(36), Px(44));
        TextRenderer.DrawText(g, _footer, Theme.Small, footer, Theme.Faint,
            TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        if (_loading is not null)
            PaintLoading(g, new Rectangle(footer.X, footer.Y - Px(58), footer.Width, Px(48)));
    }

    private void PaintLoading(Graphics g, Rectangle r)
    {
        var line = new Rectangle(r.X, r.Y, r.Width, Px(18));
        var percent = _loadingValue is { } p ? $"{p * 100:0}%" : null;
        var percentWidth = percent is null ? 0 : TextRenderer.MeasureText(percent, Theme.Small).Width + Px(6);
        TextRenderer.DrawText(g, _loading, Theme.Small, line with { Width = line.Width - percentWidth }, Theme.Muted, Theme.Line | TextFormatFlags.EndEllipsis);
        if (percent is not null)
            TextRenderer.DrawText(g, percent, Theme.Small, line, Theme.Faint, Theme.Line | TextFormatFlags.Right);
        var track = new Rectangle(r.X, line.Bottom + Px(4), r.Width, Px(4));
        if (_loadingDetail is not null)
            TextRenderer.DrawText(g, _loadingDetail, Theme.Small, new Rectangle(r.X, track.Bottom + Px(4), r.Width, Px(18)), Theme.Faint,
                Theme.Line | TextFormatFlags.EndEllipsis);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Theme.FillRounded(g, track, Px(2), Theme.Raised);
        Rectangle fill;
        if (_loadingValue is { } v)
            fill = track with { Width = (int)(track.Width * Math.Clamp(v, 0, 1)) };
        else
        {
            // Indeterminate: a third of the track sweeping across; the owner repaints while loading.
            var phase = Environment.TickCount64 % 1400 / 1400d;
            var w = track.Width / 3;
            var x = track.X - w + (int)((track.Width + w) * phase);
            fill = Rectangle.Intersect(track, track with { X = x, Width = w });
        }
        if (fill.Width > 0)
            Theme.FillRounded(g, fill, Px(2), Theme.Accent);
    }

    /// <summary>158,989 → 159K; small numbers stay exact.</summary>
    internal static string Compact(int n) => n switch
    {
        >= 1_000_000 => (n / 1_000_000d).ToString("0.#") + "M",
        >= 10_000 => (n / 1000d).ToString("0") + "K",
        _ => n.ToString("N0"),
    };

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = _rows.FirstOrDefault(r => r.Bounds.Contains(e.Location)).Category;
        if (hit != _hover)
        {
            _hover = hit;
            Cursor = hit is null ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = null;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (_hover is { } c)
            Selected = c;
        base.OnMouseDown(e);
    }
}
