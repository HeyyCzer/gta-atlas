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

    public int Count => _pager.Total;

    public AssetSummary? SelectedItem => _pager[_selected];

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

    private int Gap => Px(8);
    private int CardWidth => Px(300);
    private int CardHeight => Px(92);
    private int ListRowHeight => Px(28);
    private int ViewWidth => ClientSize.Width - _scrollBar.Width;

    private int Columns => _mode == ViewMode.List ? 1 : Math.Max(1, (ViewWidth - Gap) / (CardWidth + Gap));
    private int RowHeight => _mode == ViewMode.List ? ListRowHeight : CardHeight + Gap;
    private int Rows => (Count + Columns - 1) / Columns;
    private int VisibleRows => Math.Max(1, ClientSize.Height / RowHeight);

    /// <summary>Grid cards stretch to fill the row so there is no ragged gap on the right.</summary>
    private int CellWidth => _mode == ViewMode.List ? ViewWidth : (ViewWidth - Gap) / Columns - Gap;

    private Rectangle ItemBounds(int index)
    {
        var row = index / Columns;
        var col = index % Columns;
        var y = row * RowHeight - _scrollBar.Value;
        return _mode == ViewMode.List
            ? new Rectangle(0, y, ViewWidth, ListRowHeight)
            : new Rectangle(Gap + col * (CellWidth + Gap), y + Gap, CellWidth, CardHeight);
    }

    private Rectangle CopyBounds(Rectangle item)
    {
        var s = Px(22);
        return _mode == ViewMode.List
            ? new Rectangle(item.Right - s - Px(4), item.Y + (item.Height - s) / 2, s, s)
            : new Rectangle(item.Right - s - Px(4), item.Y + Px(4), s, s);
    }

    private int HitTest(Point p)
    {
        if (p.X >= ViewWidth)
            return -1;
        var y = p.Y + _scrollBar.Value;
        var row = y / RowHeight;
        var col = _mode == ViewMode.List ? 0 : Math.Min(Columns - 1, (p.X - Gap / 2) / (CellWidth + Gap));
        var index = row * Columns + col;
        return index >= 0 && index < Count && ItemBounds(index).Contains(p) ? index : -1;
    }

    private void UpdateScroll()
    {
        var content = Rows * RowHeight + (_mode == ViewMode.Grid ? Gap : 0);
        _scrollBar.Minimum = 0;
        _scrollBar.LargeChange = Math.Max(1, ClientSize.Height);
        _scrollBar.SmallChange = RowHeight;
        _scrollBar.Maximum = Math.Max(0, content);
        _scrollBar.Enabled = content > ClientSize.Height;
        ClampScroll(_scrollBar.Value);
    }

    private void ClampScroll(int value) =>
        _scrollBar.Value = Math.Clamp(value, 0, Math.Max(0, _scrollBar.Maximum - _scrollBar.LargeChange + 1));

    public void EnsureVisible(int index)
    {
        if (index < 0)
            return;
        var top = index / Columns * RowHeight;
        var bottom = top + RowHeight + (_mode == ViewMode.Grid ? Gap : 0);
        if (top < _scrollBar.Value)
            ClampScroll(top);
        else if (bottom > _scrollBar.Value + ClientSize.Height)
            ClampScroll(bottom - ClientSize.Height);
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
            TextRenderer.DrawText(g, "No results", Theme.UiFont, new Rectangle(0, 0, ViewWidth, Px(80)), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var first = _scrollBar.Value / RowHeight * Columns;
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
    }

    private void PaintCard(Graphics g, AssetSummary item, int index, Rectangle r)
    {
        var selected = index == _selected;
        using (var path = RoundedRect(r, Px(6)))
        {
            using var fill = new SolidBrush(selected ? Theme.Selected : index == _hover ? Theme.CardHover : Theme.Card);
            g.FillPath(fill, path);
            using var pen = new Pen(selected && Focused ? Theme.Accent : Theme.Border);
            g.DrawPath(pen, path);
        }

        // Placeholder tile until thumbnails exist (Phase 4).
        var pad = Px(10);
        var tile = new Rectangle(r.X + pad, r.Y + pad, r.Height - pad * 2, r.Height - pad * 2);
        using (var path = RoundedRect(tile, Px(4)))
        using (var brush = new SolidBrush(Color.FromArgb(60, Theme.KindColor(item.Kind))))
            g.FillPath(brush, path);
        TextRenderer.DrawText(g, KindGlyph(item.Kind), Theme.Tile, tile, Theme.KindColor(item.Kind),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        var x = tile.Right + pad;
        var copy = CopyBounds(r);
        var textWidth = copy.Left - x - Px(2);
        var line = TextRenderer.MeasureText("Ag", Theme.Bold).Height;
        var y = r.Y + Px(8);
        DrawText(g, item.Name, Theme.Bold, new Rectangle(x, y, textWidth, line), Theme.Text);
        y += line;

        var small = TextRenderer.MeasureText("Ag", Theme.Small).Height;
        if (AssetText.Subtitle(item) is { } sub)
            DrawText(g, sub, Theme.UiFont, new Rectangle(x, y, r.Right - x - pad, small + Px(2)), Theme.Muted);
        y += small + Px(3);
        if (item.Tags.Count > 0)
            DrawText(g, string.Join(" · ", item.Tags), Theme.Small, new Rectangle(x, y, r.Right - x - pad, small), Theme.Muted);

        PaintBadge(g, item.Dlc, x, r.Bottom - small - Px(10));
        PaintCopy(g, copy, index);
    }

    private void PaintRow(Graphics g, AssetSummary item, int index, Rectangle r)
    {
        var selected = index == _selected;
        if (selected || index == _hover)
        {
            using var fill = new SolidBrush(selected ? Theme.Selected : Theme.CardHover);
            g.FillRectangle(fill, r);
        }
        if (selected && Focused)
        {
            using var accent = new SolidBrush(Theme.Accent);
            g.FillRectangle(accent, r.X, r.Y, Px(3), r.Height);
        }

        var copy = CopyBounds(r);
        var w = copy.Left - Px(8);
        var cols = new (string? Text, float Share, Font Font, Color Color)[]
        {
            (item.Name, 0.32f, Theme.Bold, Theme.Text),
            (AssetText.Subtitle(item), 0.26f, Theme.UiFont, Theme.Muted),
            (AssetText.KindTitle(item.Kind), 0.10f, Theme.UiFont, Theme.Muted),
            (item.Dlc, 0.14f, Theme.UiFont, Theme.Muted),
            (HashText.ToHex(item.Hash), 0.18f, Theme.UiFont, Theme.Muted),
        };
        var x = Px(12);
        foreach (var (text, share, font, color) in cols)
        {
            var cw = (int)(w * share);
            if (text is not null)
                DrawText(g, text, font, new Rectangle(x, r.Y, cw - Px(8), r.Height), color);
            x += cw;
        }
        PaintCopy(g, copy, index);
    }

    private void PaintBadge(Graphics g, string text, int x, int y)
    {
        var size = TextRenderer.MeasureText(text, Theme.Small);
        var r = new Rectangle(x, y, size.Width + Px(4), size.Height + Px(2));
        using (var path = RoundedRect(r, Px(4)))
        using (var brush = new SolidBrush(Theme.Badge))
            g.FillPath(brush, path);
        TextRenderer.DrawText(g, text, Theme.Small, r, Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private void PaintCopy(Graphics g, Rectangle r, int index)
    {
        if (index != _hover && index != _selected)
            return;
        if (index == _hover && _hoverCopy)
        {
            using var path = RoundedRect(r, Px(4));
            using var brush = new SolidBrush(Theme.Badge);
            g.FillPath(brush, path);
        }
        // Two overlapping squares: the usual "copy" glyph.
        var s = Px(8);
        var o = Px(3);
        var cx = r.X + (r.Width - s - o) / 2;
        var cy = r.Y + (r.Height - s - o) / 2;
        using var pen = new Pen(index == _hover && _hoverCopy ? Theme.Accent : Theme.Muted, Px(1));
        g.DrawRectangle(pen, cx + o, cy, s, s);
        using (var back = new SolidBrush(index == _hover && _hoverCopy ? Theme.Badge : index == _selected ? Theme.Selected : Theme.CardHover))
            g.FillRectangle(back, cx, cy + o, s, s);
        g.DrawRectangle(pen, cx, cy + o, s, s);
    }

    private static void DrawText(Graphics g, string text, Font font, Rectangle r, Color color) =>
        TextRenderer.DrawText(g, text, font, r, color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

    private static string KindGlyph(string kind) => kind switch
    {
        AssetKind.Prop => "PR",
        AssetKind.Vehicle => "VE",
        AssetKind.Ped => "PD",
        AssetKind.Clothing => "CL",
        AssetKind.Anim => "AN",
        AssetKind.AnimDict => "AD",
        AssetKind.Mlo => "ML",
        AssetKind.Ymap => "YM",
        _ => "?",
    };

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        path.AddRoundedRectangle(r, new Size(radius * 2, radius * 2));
        return path;
    }

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
        ClampScroll(_scrollBar.Value - e.Delta / 120 * RowHeight * (_mode == ViewMode.List ? 3 : 1));
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
            Cursor = onCopy ? Cursors.Hand : Cursors.Default;
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
        if (HitTest(e.Location) >= 0)
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
