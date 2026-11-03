using System.Drawing.Drawing2D;
using GtaAtlas.App.Browsing;
using GtaAtlas.App.Ui.Controls;
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
    private readonly SmoothScroll _smooth;
    private bool _syncingScrollBar;
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
        _smooth = new SmoothScroll(this, () => MaxScroll);
        _smooth.Changed += (_, _) =>
        {
            _syncingScrollBar = true;
            _scrollBar.Value = Math.Clamp(_smooth.Value, 0, MaxScroll);
            _syncingScrollBar = false;
            UpdateHover(PointToClient(MousePosition));
        };
        _scrollBar.ValueChanged += (_, _) =>
        {
            // Dragging the bar is direct; only wheel/keyboard animate.
            if (!_syncingScrollBar)
                _smooth.Jump(_scrollBar.Value);
            Invalidate();
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _smooth.Dispose();
            ClearRowCache();
            ClearTiles();
        }
        base.Dispose(disposing);
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
        UpdateScroll();
        _smooth.Jump(0);
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
        _smooth?.Clamp();
    }

    private int MaxScroll => Math.Max(0, _scrollBar.Maximum - _scrollBar.LargeChange + 1);

    public void EnsureVisible(int index)
    {
        if (index < 0)
            return;
        var view = ClientSize.Height - ContentTop;
        var top = index / Columns * RowHeight;
        _smooth.Reveal(top, top + RowHeight, view);
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

        // Rows come from the bitmap cache: a frame is a few opaque blits, cheap enough for high refresh rates.
        var scroll = _scrollBar.Value;
        var firstRow = Math.Max(0, scroll / RowHeight);
        var lastRow = Math.Min(Rows - 1, (scroll + ClientSize.Height - ContentTop) / RowHeight);
        for (var row = firstRow; row <= lastRow; row++)
            g.DrawImageUnscaled(RowBitmap(row), 0, RowTop(row));

        // Hovered and selected items change often: draw them live on top of the cached row.
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var i in new[] { _selected, _hover }.Distinct())
        {
            if (i < 0 || i / Columns < firstRow || i / Columns > lastRow || _pager[i] is not { } item)
                continue;
            if (_mode == ViewMode.List)
                PaintRow(g, item, i, ItemBounds(i));
            else
                PaintCard(g, item, i, ItemBounds(i));
        }

        if (_mode == ViewMode.List)
            PaintListHeader(g);
    }

    private const int MaxCachedRows = 16;
    private readonly Dictionary<int, Bitmap> _rowCache = [];
    private readonly LinkedList<int> _rowOrder = new();
    private (int Width, ViewMode Mode, int Columns, ResultPager Pager) _rowCacheKey;
    private bool _paintingCache;

    private int RowTop(int row) => ContentTop + row * RowHeight - _scrollBar.Value;

    /// <summary>One grid/list row rendered in its resting state (not hovered, not selected).</summary>
    private Bitmap RowBitmap(int row)
    {
        var key = (ViewWidth, _mode, Columns, _pager);
        if (key != _rowCacheKey)
        {
            ClearRowCache();
            _rowCacheKey = key;
        }
        if (_rowCache.TryGetValue(row, out var cached))
        {
            _rowOrder.Remove(row);
            _rowOrder.AddFirst(row);
            return cached;
        }

        // 32bpp RGB (no alpha): GDI text renders correctly into it and it blits without blending.
        var bitmap = new Bitmap(Math.Max(1, ViewWidth), RowHeight, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var top = RowTop(row);
            _paintingCache = true;
            for (var i = row * Columns; i < Math.Min(Count, (row + 1) * Columns); i++)
            {
                if (_pager[i] is not { } item)
                    continue;
                var r = ItemBounds(i);
                r.Y -= top;
                if (_mode == ViewMode.List)
                    PaintRow(g, item, i, r);
                else
                    PaintCard(g, item, i, r);
            }
            _paintingCache = false;
        }

        _rowCache[row] = bitmap;
        _rowOrder.AddFirst(row);
        while (_rowOrder.Count > MaxCachedRows)
        {
            _rowCache.Remove(_rowOrder.Last!.Value, out var old);
            old?.Dispose();
            _rowOrder.RemoveLast();
        }
        return bitmap;
    }

    private void ClearRowCache()
    {
        foreach (var b in _rowCache.Values)
            b.Dispose();
        _rowCache.Clear();
        _rowOrder.Clear();
    }

    private bool IsSelected(int index) => !_paintingCache && index == _selected;

    private bool IsHovered(int index) => !_paintingCache && index == _hover;

    private void PaintCard(Graphics g, AssetSummary item, int index, Rectangle r)
    {
        var selected = IsSelected(index);
        var hover = IsHovered(index);
        var radius = Px(12);
        Theme.FillRounded(g, r, radius, hover || selected ? Theme.Raised : Theme.Surface,
            selected ? Theme.Accent : hover ? Theme.BorderStrong : Theme.Border);

        // Image area: placeholder tile until thumbnails exist (Phase 4). Same for every card of a kind: cached.
        var image = new Rectangle(r.X + Px(6), r.Y + Px(6), r.Width - Px(12), ImageHeight - Px(6));
        var cardBack = hover || selected ? Theme.Raised : Theme.Surface;
        g.DrawImageUnscaled(Tile(item.Kind, image.Size, cardBack), image.Location);

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

    private readonly Dictionary<(string Kind, Size Size, Color Back), Bitmap> _tiles = [];

    private Bitmap Tile(string kind, Size size, Color cardBack)
    {
        if (_tiles.TryGetValue((kind, size, cardBack), out var tile))
            return tile;

        tile = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using var g = Graphics.FromImage(tile);
        g.Clear(cardBack);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var kindColor = Theme.KindColor(kind);
        var r = new Rectangle(0, 0, size.Width - 1, size.Height - 1);
        using (var path = Theme.RoundedRect(r, Px(8)))
        using (var brush = new LinearGradientBrush(r, Color.FromArgb(0x22, kindColor), Color.FromArgb(0x08, kindColor), 90f))
        {
            using var back = new SolidBrush(Theme.Back);
            g.FillPath(back, path);
            g.FillPath(brush, path);
        }
        Theme.DrawIcon(g, KindGlyph(kind), r, Color.FromArgb(0xB0, kindColor), Theme.IconLarge);

        var label = AssetText.KindTitle(kind).ToUpperInvariant();
        var labelSize = TextRenderer.MeasureText(label, Theme.Caption);
        var labelRect = new Rectangle(Px(8), r.Bottom - Px(26), labelSize.Width + Px(6), Px(18));
        Theme.FillRounded(g, labelRect, Px(4), Color.FromArgb(0xC0, Theme.Back));
        TextRenderer.DrawText(g, label, Theme.Caption, labelRect, kindColor, Theme.Centered);

        if (_tiles.Count > 64)
            ClearTiles();
        _tiles[(kind, size, cardBack)] = tile;
        return tile;
    }

    private void ClearTiles()
    {
        foreach (var b in _tiles.Values)
            b.Dispose();
        _tiles.Clear();
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
        var selected = IsSelected(index);
        var hover = IsHovered(index);
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
        _smooth.By(-e.Delta * Px(_mode == ViewMode.List ? 120 : 140) / 120);
        base.OnMouseWheel(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        UpdateHover(e.Location);
        base.OnMouseMove(e);
    }

    /// <summary>Hover follows content under a still mouse while the view scrolls.</summary>
    private void UpdateHover(Point location)
    {
        if (!ClientRectangle.Contains(location))
            return;
        var hit = HitTest(location);
        var onCopy = hit >= 0 && CopyBounds(ItemBounds(hit)).Contains(location);
        if (hit != _hover || onCopy != _hoverCopy)
        {
            if (hit != _hover)
                _toolTip.SetToolTip(this, _pager[hit] is { } item ? $"{item.Name}\n{AssetText.Subtitle(item)}".TrimEnd() : null);
            _hover = hit;
            _hoverCopy = onCopy;
            Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
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
