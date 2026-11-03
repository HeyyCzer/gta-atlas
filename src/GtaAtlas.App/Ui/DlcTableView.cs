using GtaAtlas.App.Browsing;
using GtaAtlas.App.Ui.Controls;
using GtaAtlas.Data;

namespace GtaAtlas.App.Ui;

/// <summary>DLC catalog table: click a header to sort, double-click (or Enter) a pack to browse its assets.</summary>
internal sealed class DlcTableView : ThemedControl
{
    private IReadOnlyList<DlcSummary> _all = [];
    private IReadOnlyList<DlcSummary> _rows = [];
    private int _sortColumn = DlcTable.DefaultColumn;
    private bool _descending = DlcTable.DefaultDescending;
    private readonly SmoothScroll _smooth;
    private int _hoverRow = -1;
    private int _hoverHeader = -1;
    private int _selected = -1;

    public DlcTableView()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        _smooth = new SmoothScroll(this, () => MaxScroll);
        _smooth.Changed += (_, _) =>
        {
            Invalidate();
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _smooth.Dispose();
        base.Dispose(disposing);
    }

    public event EventHandler<DlcSummary>? DlcActivated;

    public int Count => _rows.Count;

    public void SetDlcs(IReadOnlyList<DlcSummary> dlcs)
    {
        _all = dlcs;
        Resort();
    }

    private void Resort()
    {
        _rows = DlcTable.Sort(_all, _sortColumn, _descending);
        _smooth.Clamp();
        Invalidate();
    }

    private int Inset => Px(24);
    private int HeaderHeight => Px(40);
    private int RowHeight => Px(38);
    private int MaxScroll => Math.Max(0, _rows.Count * RowHeight - (Height - HeaderHeight) + Px(16));

    /// <summary>Pack name gets the slack; numeric columns share the rest.</summary>
    private (int X, int Width)[] ColumnLayout()
    {
        var cols = DlcTable.Columns;
        var total = Width - Inset * 2;
        var first = Px(56);
        var numeric = Math.Max(Px(70), (total - first - Px(220)) / (cols.Count - 2));
        var name = total - first - numeric * (cols.Count - 2);
        var x = Inset;
        var layout = new (int, int)[cols.Count];
        for (var i = 0; i < cols.Count; i++)
        {
            var w = i == 0 ? first : i == 1 ? name : numeric;
            layout[i] = (x, w);
            x += w;
        }
        return layout;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var layout = ColumnLayout();
        var cols = DlcTable.Columns;

        g.SetClip(new Rectangle(0, HeaderHeight, Width, Height - HeaderHeight));
        var first = _smooth.Value / RowHeight;
        for (var i = first; i < _rows.Count; i++)
        {
            var y = HeaderHeight + i * RowHeight - _smooth.Value;
            if (y > Height)
                break;
            var d = _rows[i];
            var row = new Rectangle(Inset - Px(8), y, Width - Inset * 2 + Px(16), RowHeight);
            if (i == _selected)
                Theme.FillRounded(g, Rectangle.Inflate(row, 0, -Px(2)), Px(8), Theme.AccentSoft);
            else if (i == _hoverRow)
                Theme.FillRounded(g, Rectangle.Inflate(row, 0, -Px(2)), Px(8), Theme.Raised);

            for (var c = 0; c < cols.Count; c++)
            {
                var (x, w) = layout[c];
                var cell = new Rectangle(x, y, w - Px(12), RowHeight);
                if (c == 1)
                {
                    TextRenderer.DrawText(g, d.Name, Theme.Bold, cell, Theme.Text, Theme.Line);
                    continue;
                }
                var value = (IComparable)cols[c].Key(d);
                var n = value is int i2 ? i2 : 0;
                var text = c == 0 ? n.ToString() : n == 0 ? "–" : n.ToString("N0");
                var color = c == 0 || n == 0 ? Theme.Faint : c == cols.Count - 1 ? Theme.Text : Theme.Muted;
                TextRenderer.DrawText(g, text, c == cols.Count - 1 ? Theme.Bold : Theme.UiFont, cell, color,
                    c == 0 ? Theme.Line : Theme.Line | TextFormatFlags.Right);
            }
        }
        g.ResetClip();

        // Header
        using (var pen = new Pen(Theme.Border))
            g.DrawLine(pen, Inset, HeaderHeight - 1, Width - Inset, HeaderHeight - 1);
        for (var c = 0; c < cols.Count; c++)
        {
            var (x, w) = layout[c];
            var active = c == _sortColumn;
            var right = c >= 2;
            var color = active ? Theme.Accent : c == _hoverHeader ? Theme.Text : Theme.Faint;
            var title = cols[c].Title.ToUpperInvariant();
            var tw = TextRenderer.MeasureText(title, Theme.Caption).Width;
            var cell = new Rectangle(x, 0, w - Px(12), HeaderHeight);
            var textX = right ? cell.Right - tw - (active ? Px(16) : 0) : cell.X;
            TextRenderer.DrawText(g, title, Theme.Caption, new Rectangle(textX, 0, tw + Px(2), HeaderHeight), color, Theme.Line);
            if (active)
                Theme.DrawIcon(g, _descending ? Glyph.SortDown : Glyph.SortUp, new Rectangle(textX + tw + Px(2), 0, Px(14), HeaderHeight), color, Theme.IconSmall);
        }

        if (MaxScroll > 0)
        {
            var view = Height - HeaderHeight;
            var total = _rows.Count * RowHeight + Px(16);
            var h = Math.Max(Px(30), view * view / total);
            var y = HeaderHeight + (view - h) * _smooth.Value / MaxScroll;
            Theme.FillRounded(g, new Rectangle(Width - Px(8), y, Px(3), h), Px(1), Theme.BorderStrong);
        }
    }

    private int RowAt(Point p) =>
        p.Y < HeaderHeight ? -1 : (p.Y - HeaderHeight + _smooth.Value) / RowHeight is var i && i < _rows.Count ? i : -1;

    private int HeaderAt(Point p)
    {
        if (p.Y >= HeaderHeight)
            return -1;
        var layout = ColumnLayout();
        return Array.FindIndex(layout, l => p.X >= l.X && p.X < l.X + l.Width);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var row = RowAt(e.Location);
        var header = HeaderAt(e.Location);
        if (row != _hoverRow || header != _hoverHeader)
        {
            _hoverRow = row;
            _hoverHeader = header;
            Cursor = row >= 0 || header >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverRow = _hoverHeader = -1;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (_hoverHeader >= 0)
        {
            (_sortColumn, _descending) = DlcTable.Toggle(_sortColumn, _descending, _hoverHeader);
            _selected = -1;
            Resort();
        }
        else if (_hoverRow >= 0)
        {
            _selected = _hoverRow;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (RowAt(e.Location) is var row and >= 0)
            DlcActivated?.Invoke(this, _rows[row]);
        base.OnMouseDoubleClick(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        _smooth.By(-e.Delta * RowHeight * 3 / 120);
        base.OnMouseWheel(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Enter or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var target = e.KeyCode switch
        {
            Keys.Up => _selected - 1,
            Keys.Down => _selected + 1,
            Keys.Home => 0,
            Keys.End => _rows.Count - 1,
            _ => int.MinValue,
        };
        if (target != int.MinValue && _rows.Count > 0)
        {
            _selected = Math.Clamp(target, 0, _rows.Count - 1);
            var top = _selected * RowHeight;
            var view = Height - HeaderHeight - Px(16);
            _smooth.Reveal(top, top + RowHeight, view);
            Invalidate();
        }
        else if (e.KeyCode == Keys.Enter && _selected >= 0)
        {
            DlcActivated?.Invoke(this, _rows[_selected]);
        }
        base.OnKeyDown(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _smooth?.Clamp();
    }
}
