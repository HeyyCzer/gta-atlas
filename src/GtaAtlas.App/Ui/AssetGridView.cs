using System.Drawing.Drawing2D;
using GtaAtlas.App.Browsing;
using GtaAtlas.Data;

namespace GtaAtlas.App.Ui;

public enum ViewMode
{
    Grid,
    List,
}

/// <summary>
/// Virtualized, owner-drawn result view: only the visible cards/rows are fetched and painted,
/// so it scrolls through hundreds of thousands of assets.
/// </summary>
internal sealed class AssetGridView : Control
{
    private readonly VScrollBar _scrollBar = new() { Dock = DockStyle.Right };
    private readonly ToolTip _toolTip = new() { InitialDelay = 600 };
    private ResultPager _pager = ResultPager.Empty;
    private ViewMode _mode = ViewMode.Grid;
    private int _selected = -1;
    private int _hover = -1;
    private bool _hoverCopy;

    public AssetGridView()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        TabStop = true;
        Controls.Add(_scrollBar);
        _scrollBar.ValueChanged += (_, _) => Invalidate();
    }

    public event EventHandler? SelectionChanged;
    public event EventHandler? ItemActivated;
    public event EventHandler<AssetSummary>? CopyRequested;

    /// <summary>Shown when there are no results.</summary>
    public string EmptyText { get; set; } = "No results";

    public int Count => _pager.Total;

    public AssetSummary? SelectedItem => _pager[_selected];

    public AssetSummary? ItemAt(int index) => _pager[index];

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            var v = Count == 0 ? -1 : Math.Clamp(value, 0, Count - 1);
            if (v == _selected)
                return;
            _selected = v;
            EnsureVisible(v);
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ViewMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            UpdateScroll();
            EnsureVisible(_selected);
            Invalidate();
        }
    }

    /// <summary>Shows a new result. Keeps the selected asset selected if it is on the first page.</summary>
    public void SetItems(ResultPager pager)
    {
        var keep = SelectedItem?.Id;
        _pager = pager;
        _hover = -1;
        _scrollBar.Value = 0;
        UpdateScroll();
        var index = keep is { } id ? pager.IndexOf(id) : -1;
        _selected = -1;
        SelectedIndex = index >= 0 ? index : (Count > 0 ? 0 : -1);
        if (_selected == -1)
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    #region Layout

    private int Px(int logical) => LogicalToDeviceUnits(logical);

    private int Gap => Px(14);
    private int Inset => Px(24);
    private int MinCardWidth => Px(200);
    private int ImageHeight => Px(132);
    private int InfoHeight => Px(78);
    private int CardHeight => ImageHeight + InfoHeight;
    private int ListRowHeight => Px(40);
    private int ListHeaderHeight => Px(34);
    private int ViewWidth => ClientSize.Width - (_scrollBar.Visible ? _scrollBar.Width : 0);
    private int ContentTop => _mode == ViewMode.List ? ListHeaderHeight : Px(4);

    private int Columns => _mode == ViewMode.List ? 1 : Math.Max(1, (ViewWidth - Inset * 2 + Gap) / (MinCardWidth + Gap));
    private int RowHeight => _mode == ViewMode.List ? ListRowHeight : CardHeight + Gap;
    private int Rows => (Count + Columns - 1) / Columns;
    private int VisibleRows => Math.Max(1, (ClientSize.Height - ContentTop) / RowHeight);

    /// <summary>Cards stretch to fill the row so there is no ragged gap on the right.</summary>
    private int CellWidth => _mode == ViewMode.List ? ViewWidth - Inset * 2 : (ViewWidth - Inset * 2 - Gap * (Columns - 1)) / Columns;

    private Rectangle ItemBounds(int index)
    {
        var row = index / Columns;
        var col = index % Columns;
        var y = ContentTop + row * RowHeight - _scrollBar.Value;
        return _mode == ViewMode.List
            ? new Rectangle(Inset, y, CellWidth, ListRowHeight)
            : new Rectangle(Inset + col * (CellWidth + Gap), y, CellWidth, CardHeight);
    }

    private Rectangle CopyBounds(Rectangle item)
    {
        var s = Px(30);
        return _mode == ViewMode.List
            ? new Rectangle(item.Right - s - Px(6), item.Y + (item.Height - s) / 2, s, s)
            : new Rectangle(item.Right - s - Px(8), item.Y + Px(8), s, s);
    }

    private int HitTest(Point p)
    {
        if (p.X >= ViewWidth || p.Y < ContentTop)
            return -1;
        var row = (p.Y - ContentTop + _scrollBar.Value) / RowHeight;
        var col = _mode == ViewMode.List ? 0 : Math.Clamp((p.X - Inset) / (CellWidth + Gap), 0, Columns - 1);
        var index = row * Columns + col;
        return index >= 0 && index < Count && ItemBounds(index).Contains(p) ? index : -1;
    }

    private void UpdateScroll()
    {
        var content = Rows * RowHeight + Inset;
        var view = Math.Max(1, ClientSize.Height - ContentTop);
        _scrollBar.Minimum = 0;
        _scrollBar.LargeChange = view;
        _scrollBar.SmallChange = RowHeight;
        _scrollBar.Maximum = Math.Max(0, content);
        _scrollBar.Visible = content > view;
        ClampScroll(_scrollBar.Value);
    }

    private void ClampScroll(int value) =>
        _scrollBar.Value = Math.Clamp(value, 0, Math.Max(0, _scrollBar.Maximum - _scrollBar.LargeChange + 1));

    public void EnsureVisible(int index)
    {
        if (index < 0)
            return;
        var view = ClientSize.Height - ContentTop;
        var top = index / Columns * RowHeight;
        var bottom = top + RowHeight;
        if (top < _scrollBar.Value)
            ClampScroll(top);
        else if (bottom > _scrollBar.Value + view)
            ClampScroll(bottom - view);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScroll();
    }

    #endregion

    #region Painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (Count == 0)
        {
            var r = new Rectangle(0, Px(60), ViewWidth, Px(120));
            Theme.DrawIcon(g, Glyph.Search, new Rectangle(r.X, r.Y, r.Width, Px(60)), Theme.Faint, Theme.IconLarge);
            TextRenderer.DrawText(g, EmptyText, Theme.Bold, new Rectangle(r.X, r.Y + Px(64), r.Width, Px(24)), Theme.Muted, Theme.Centered);
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var firstRow = Math.Max(0, _scrollBar.Value / RowHeight);
        var first = firstRow * Columns;
        var last = Math.Min(Count - 1, ((_scrollBar.Value + ClientSize.Height) / RowHeight + 1) * Columns - 1);
        for (var i = first; i <= last; i++)
        {
            if (_pager[i] is not { } item)
                continue;
            var bounds = ItemBounds(i);
            if (_mode == ViewMode.List)
                PaintRow(g, item, i, bounds);
            else
                PaintCard(g, item, i, bounds);
        }

        if (_mode == ViewMode.List)
            PaintListHeader(g);
    }

    private void PaintCard(Graphics g, AssetSummary item, int index, Rectangle r)
    {
        var selected = index == _selected;
        var hover = index == _hover;
        var radius = Px(12);
        Theme.FillRounded(g, r, radius, hover || selected ? Theme.Raised : Theme.Surface,
            selected ? Theme.Accent : hover ? Theme.BorderStrong : Theme.Border);

        // Image area: placeholder tile until thumbnails exist (Phase 4).
        var kindColor = Theme.KindColor(item.Kind);
        var image = new Rectangle(r.X + Px(6), r.Y + Px(6), r.Width - Px(12), ImageHeight - Px(6));
        using (var path = Theme.RoundedRect(image, Px(8)))
        using (var brush = new LinearGradientBrush(image, Color.FromArgb(0x22, kindColor), Color.FromArgb(0x08, kindColor), 90f))
        {
            using var back = new SolidBrush(Theme.Back);
            g.FillPath(back, path);
            g.FillPath(brush, path);
        }
        Theme.DrawIcon(g, KindGlyph(item.Kind), image, Color.FromArgb(0xB0, kindColor), Theme.IconLarge);

        // Kind label on the image, bottom-left.
        var kind = AssetText.KindTitle(item.Kind).ToUpperInvariant();
        var kindSize = TextRenderer.MeasureText(kind, Theme.Caption);
        var kindRect = new Rectangle(image.X + Px(8), image.Bottom - Px(26), kindSize.Width + Px(6), Px(18));
        Theme.FillRounded(g, kindRect, Px(4), Color.FromArgb(0xC0, Theme.Back));
        TextRenderer.DrawText(g, kind, Theme.Caption, kindRect, kindColor, Theme.Centered);

        if (hover || selected)
            PaintCopy(g, CopyBounds(r), index, Color.FromArgb(0xD0, Theme.Back));

        var x = r.X + Px(14);
        var w = r.Width - Px(28);
        var y = r.Y + ImageHeight + Px(6);
        TextRenderer.DrawText(g, item.Name, Theme.Bold, new Rectangle(x, y, w, Px(22)), Theme.Text, Theme.Line);
        y += Px(21);
        var sub = AssetText.Subtitle(item) ?? (item.Tags.Count > 0 ? string.Join(" · ", item.Tags) : null);
        if (sub is not null)
            TextRenderer.DrawText(g, sub, Theme.Small, new Rectangle(x, y, w, Px(18)), Theme.Muted, Theme.Line);
        y += Px(22);
        PaintBadge(g, item.Dlc, x, y, w);
    }

    private void PaintListHeader(Graphics g)
    {
        var header = new Rectangle(0, 0, ViewWidth, ListHeaderHeight);
        using (var back = new SolidBrush(BackColor))
            g.FillRectangle(back, header);
        using (var pen = new Pen(Theme.Border))
            g.DrawLine(pen, Inset, header.Bottom - 1, ViewWidth - Inset, header.Bottom - 1);
        var x = Inset + Px(48);
        foreach (var (title, share) in ListColumns)
        {
            var cw = (int)((CellWidth - Px(48 + 44)) * share);
            TextRenderer.DrawText(g, title.ToUpperInvariant(), Theme.Caption, new Rectangle(x, 0, cw, header.Height), Theme.Faint, Theme.Line);
            x += cw;
        }
    }

    private static readonly (string Title, float Share)[] ListColumns =
        [("Name", 0.34f), ("Details", 0.28f), ("Kind", 0.11f), ("DLC", 0.13f), ("Hash", 0.14f)];

    private void PaintRow(Graphics g, AssetSummary item, int index, Rectangle r)
    {
        var selected = index == _selected;
        var hover = index == _hover;
        if (selected || hover)
            Theme.FillRounded(g, Rectangle.Inflate(r, 0, -Px(2)), Px(8), selected ? Theme.AccentSoft : Theme.Raised);

        var kindColor = Theme.KindColor(item.Kind);
        var icon = new Rectangle(r.X + Px(8), r.Y + (r.Height - Px(28)) / 2, Px(28), Px(28));
        Theme.FillRounded(g, icon, Px(6), Color.FromArgb(0x26, kindColor));
        Theme.DrawIcon(g, KindGlyph(item.Kind), icon, kindColor);

        var values = new (string? Text, Font Font, Color Color)[]
        {
            (item.Name, Theme.Bold, Theme.Text),
            (AssetText.Subtitle(item) ?? string.Join(" · ", item.Tags), Theme.UiFont, Theme.Muted),
            (AssetText.KindTitle(item.Kind), Theme.UiFont, Theme.Muted),
            (item.Dlc, Theme.UiFont, Theme.Muted),
            (HashText.ToHex(item.Hash), Theme.Mono, Theme.Muted),
        };
        var x = r.X + Px(48);
        for (var c = 0; c < values.Length; c++)
        {
            var cw = (int)((r.Width - Px(48 + 44)) * ListColumns[c].Share);
            if (values[c].Text is { } text)
                TextRenderer.DrawText(g, text, values[c].Font, new Rectangle(x, r.Y, cw - Px(12), r.Height), values[c].Color, Theme.Line);
            x += cw;
        }
        if (selected || hover)
            PaintCopy(g, CopyBounds(r), index, Theme.Hover);
    }

    private void PaintBadge(Graphics g, string text, int x, int y, int maxWidth)
    {
        var size = TextRenderer.MeasureText(text, Theme.Caption);
        var r = new Rectangle(x, y, Math.Min(maxWidth, size.Width + Px(10)), Px(20));
        Theme.FillRounded(g, r, Px(5), Theme.Hover);
        TextRenderer.DrawText(g, text, Theme.Caption, r, Theme.Muted, Theme.Centered | TextFormatFlags.EndEllipsis);
    }

    private void PaintCopy(Graphics g, Rectangle r, int index, Color back)
    {
        var active = index == _hover && _hoverCopy;
        Theme.FillRounded(g, r, Px(8), active ? Theme.Accent : back, active ? null : Theme.Border);
        Theme.DrawIcon(g, Glyph.Copy, r, active ? Theme.Back : Theme.Text, Theme.IconSmall);
    }

    public static string KindGlyph(string kind) => kind switch
    {
        AssetKind.Prop => Glyph.Package,
        AssetKind.Vehicle => Glyph.Car,
        AssetKind.Ped => Glyph.Person,
        AssetKind.Clothing => Glyph.Shop,
        AssetKind.Anim => Glyph.Play,
        AssetKind.AnimDict => Glyph.Library,
        AssetKind.Mlo => Glyph.Home,
        AssetKind.Ymap => Glyph.MapPin,
        _ => Glyph.Cube,
    };

    #endregion

    #region Input

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End
        || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var page = VisibleRows * Columns;
        int? target = e.KeyCode switch
        {
            Keys.Left when _mode == ViewMode.Grid => _selected - 1,
            Keys.Right when _mode == ViewMode.Grid => _selected + 1,
            Keys.Up => _selected - Columns,
            Keys.Down => _selected + Columns,
            Keys.PageUp => _selected - page,
            Keys.PageDown => _selected + page,
            Keys.Home => 0,
            Keys.End => Count - 1,
            _ => null,
        };
        if (target is { } t)
        {
            SelectedIndex = Math.Max(0, t);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter && _selected >= 0)
        {
            ItemActivated?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ClampScroll(_scrollBar.Value - e.Delta / 120 * (_mode == ViewMode.List ? RowHeight * 3 : RowHeight / 2));
        base.OnMouseWheel(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = HitTest(e.Location);
        var onCopy = hit >= 0 && CopyBounds(ItemBounds(hit)).Contains(e.Location);
        if (hit != _hover || onCopy != _hoverCopy)
        {
            if (hit != _hover)
                _toolTip.SetToolTip(this, _pager[hit] is { } item ? $"{item.Name}\n{AssetText.Subtitle(item)}".TrimEnd() : null);
            _hover = hit;
            _hoverCopy = onCopy;
            Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _toolTip.SetToolTip(this, null);
        _hover = -1;
        _hoverCopy = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        var hit = HitTest(e.Location);
        if (hit >= 0)
        {
            SelectedIndex = hit;
            if (e.Button == MouseButtons.Left && CopyBounds(ItemBounds(hit)).Contains(e.Location) && _pager[hit] is { } item)
                CopyRequested?.Invoke(this, item);
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        var hit = HitTest(e.Location);
        if (hit >= 0 && !CopyBounds(ItemBounds(hit)).Contains(e.Location))
            ItemActivated?.Invoke(this, EventArgs.Empty);
        base.OnMouseDoubleClick(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    #endregion
}
