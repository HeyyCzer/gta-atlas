namespace GtaAtlas.App.Ui.Controls;

internal sealed record PickerItem(object? Value, string Label, string? Detail = null);

/// <summary>Pill showing "Label  value ▾"; opens a themed popup list, with a filter box for long lists.</summary>
internal sealed class Dropdown : PillButton
{
    private IReadOnlyList<PickerItem> _items = [];
    private int _selected;

    public event EventHandler? SelectedChanged;

    public IReadOnlyList<PickerItem> Items
    {
        get => _items;
        set
        {
            _items = value;
            _selected = Math.Clamp(_selected, 0, Math.Max(0, value.Count - 1));
            AutoWidth();
            Invalidate();
        }
    }

    public PickerItem? SelectedItem => _selected < _items.Count ? _items[_selected] : null;

    public object? SelectedValue => SelectedItem?.Value;

    /// <summary>Selects without raising <see cref="SelectedChanged"/>.</summary>
    public void Select(object? value)
    {
        var index = _items.ToList().FindIndex(i => Equals(i.Value, value));
        _selected = Math.Max(0, index);
        AutoWidth();
        Invalidate();
    }

    public override string? Trailing => SelectedItem?.Label;

    protected override int ExtraWidth => Px(22);

    protected override int MaxAutoWidth => Px(340);

    protected override void PaintExtra(Graphics g) =>
        Theme.DrawIcon(g, Glyph.ChevronDown, new Rectangle(Width - Px(28), 0, Px(20), Height), Theme.Muted, Theme.IconSmall);

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (_items.Count == 0)
            return;
        var popup = new PickerPopup(_items, _selected, filter: _items.Count > 12, width: Math.Max(Width, LogicalToDeviceUnits(300)));
        popup.Picked += (_, index) =>
        {
            if (index == _selected)
                return;
            _selected = index;
            AutoWidth();
            Invalidate();
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        };
        popup.Show(this, new Point(0, Height + LogicalToDeviceUnits(4)));
    }
}

/// <summary>Themed popup with an optional filter field over an owner-drawn list.</summary>
internal sealed class PickerPopup : ToolStripDropDown
{
    private readonly PickerList _list;
    private readonly SearchBox? _filter;

    public PickerPopup(IReadOnlyList<PickerItem> items, int selected, bool filter, int width)
    {
        AutoSize = false;
        Padding = Padding.Empty;
        Margin = Padding.Empty;
        DropShadowEnabled = true;
        BackColor = Theme.Surface;
        Renderer = new BorderRenderer();

        var panel = new Panel { BackColor = Theme.Surface, Padding = new Padding(6) };
        _list = new PickerList(items, selected) { Dock = DockStyle.Fill };
        panel.Controls.Add(_list);
        if (filter)
        {
            _filter = new SearchBox { Dock = DockStyle.Top, Placeholder = "Filter", Hint = "", BackColor = Theme.Surface };
            var gap = new Panel { Dock = DockStyle.Top, Height = 6, BackColor = Theme.Surface };
            panel.Controls.Add(gap);
            panel.Controls.Add(_filter);
            _filter.TextChanged += (_, _) => _list.Filter(_filter.Text);
            _filter.KeyDown += (_, e) => _list.HandleKey(e);
        }

        var rowsHeight = Math.Min(items.Count, 11) * _list.RowHeight;
        panel.Size = new Size(width, rowsHeight + (filter ? _filter!.Height + 6 : 0) + 14);
        var host = new ToolStripControlHost(panel) { AutoSize = false, Size = panel.Size, Margin = Padding.Empty, Padding = Padding.Empty };
        Items.Add(host);
        Size = panel.Size;

        _list.Picked += (_, index) =>
        {
            Close(ToolStripDropDownCloseReason.ItemClicked);
            Picked?.Invoke(this, index);
        };
        Opened += (_, _) =>
        {
            if (_filter is not null)
                _filter.FocusText();
            else
                _list.Focus();
        };
        Closed += (_, _) => BeginInvoke(Dispose);
    }

    public event EventHandler<int>? Picked;

    private sealed class BorderRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Theme.BorderStrong);
            e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Theme.Surface);
    }
}

/// <summary>Scrollable owner-drawn list used by <see cref="PickerPopup"/>; indexes reported are into the unfiltered items.</summary>
internal sealed class PickerList : ThemedControl
{
    private readonly IReadOnlyList<PickerItem> _items;
    private readonly int _selected;
    private List<int> _visible;
    private int _highlight;
    private readonly SmoothScroll _smooth;

    public PickerList(IReadOnlyList<PickerItem> items, int selected)
    {
        _items = items;
        _selected = selected;
        _visible = Enumerable.Range(0, items.Count).ToList();
        _highlight = selected;
        BackColor = Theme.Surface;
        SetStyle(ControlStyles.Selectable, true);
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

    public event EventHandler<int>? Picked;

    public int RowHeight => Px(32);

    private int MaxScroll => Math.Max(0, _visible.Count * RowHeight - Height);

    public void Filter(string text)
    {
        var t = text.Trim();
        _visible = Enumerable.Range(0, _items.Count)
            .Where(i => t.Length == 0 || _items[i].Label.Contains(t, StringComparison.OrdinalIgnoreCase)
                        || (_items[i].Detail?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
        _highlight = _visible.FirstOrDefault(-1);
        _smooth.Jump(0);
        Invalidate();
    }

    public void HandleKey(KeyEventArgs e)
    {
        var pos = _visible.IndexOf(_highlight);
        switch (e.KeyCode)
        {
            case Keys.Down:
                MoveHighlight(Math.Min(_visible.Count - 1, pos + 1));
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Up:
                MoveHighlight(Math.Max(0, pos - 1));
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Enter when _highlight >= 0:
                Picked?.Invoke(this, _highlight);
                e.Handled = e.SuppressKeyPress = true;
                break;
        }
    }

    private void MoveHighlight(int pos)
    {
        if (pos < 0 || pos >= _visible.Count)
            return;
        _highlight = _visible[pos];
        var top = pos * RowHeight;
        _smooth.Reveal(top, top + RowHeight, Height);
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        MoveHighlight(_visible.IndexOf(_selected));
        _smooth.Jump(_smooth.Target); // open already scrolled to the selection
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        HandleKey(e);
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (_visible.Count == 0)
        {
            TextRenderer.DrawText(g, "No matches", Theme.UiFont, new Rectangle(0, 0, Width, RowHeight), Theme.Muted, Theme.Centered);
            return;
        }

        var first = _smooth.Value / RowHeight;
        for (var pos = first; pos < _visible.Count && pos * RowHeight - _smooth.Value < Height; pos++)
        {
            var index = _visible[pos];
            var item = _items[index];
            var r = new Rectangle(0, pos * RowHeight - _smooth.Value, Width - Px(6), RowHeight);
            if (index == _highlight)
                Theme.FillRounded(g, Rectangle.Inflate(r, 0, -Px(1)), Px(6), Theme.Hover);
            if (index == _selected)
                Theme.DrawIcon(g, Glyph.Check, new Rectangle(r.X + Px(6), r.Y, Px(18), r.Height), Theme.Accent, Theme.IconSmall);

            var detailWidth = item.Detail is null ? 0 : TextRenderer.MeasureText(item.Detail, Theme.Small).Width + Px(8);
            TextRenderer.DrawText(g, item.Label, index == _selected ? Theme.Bold : Theme.UiFont,
                new Rectangle(r.X + Px(30), r.Y, r.Width - Px(38) - detailWidth, r.Height), Theme.Text, Theme.Line);
            if (item.Detail is not null)
                TextRenderer.DrawText(g, item.Detail, Theme.Small, new Rectangle(r.Right - detailWidth - Px(4), r.Y, detailWidth, r.Height),
                    Theme.Muted, Theme.Line | TextFormatFlags.Right);
        }

        if (MaxScroll > 0)
        {
            var total = _visible.Count * RowHeight;
            var h = Math.Max(Px(24), Height * Height / total);
            var y = (Height - h) * _smooth.Value / MaxScroll;
            Theme.FillRounded(g, new Rectangle(Width - Px(4), y, Px(3), h), Px(1), Theme.BorderStrong);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        _smooth.By(-e.Delta * RowHeight * 3 / 120);
        base.OnMouseWheel(e);
    }

    private int HitTest(Point p)
    {
        var pos = (p.Y + _smooth.Value) / RowHeight;
        return pos >= 0 && pos < _visible.Count ? _visible[pos] : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = HitTest(e.Location);
        if (hit >= 0 && hit != _highlight)
        {
            _highlight = hit;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (HitTest(e.Location) is var hit and >= 0)
            Picked?.Invoke(this, hit);
        base.OnMouseUp(e);
    }
}
