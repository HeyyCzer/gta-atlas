namespace GtaAtlas.App.Ui.Controls;

internal sealed record Segment(string? Icon, string? Text, string? ToolTip = null);

/// <summary>Pill of mutually exclusive options (e.g. All | Favorites, grid | list).</summary>
internal sealed class Segmented : ThemedControl
{
    private readonly ToolTip _toolTip = new();
    private IReadOnlyList<Segment> _segments = [];
    private int _selected;
    private int _hover = -1;

    public Segmented()
    {
        Cursor = Cursors.Hand;
        Height = Px(38);
    }

    public event EventHandler? SelectedChanged;

    public IReadOnlyList<Segment> Segments
    {
        get => _segments;
        set
        {
            _segments = value;
            Width = PreferredWidth;
            Invalidate();
        }
    }

    public int Selected
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

    /// <summary>Changes a segment's label without raising events (e.g. a count).</summary>
    public void SetText(int index, string text)
    {
        if (_segments[index].Text == text)
            return;
        _segments = _segments.Select((s, i) => i == index ? s with { Text = text } : s).ToList();
        Width = PreferredWidth;
        Invalidate();
    }

    private int SegmentWidth(Segment s)
    {
        var w = Px(14);
        if (s.Icon is not null)
            w += Px(18);
        if (s.Text is not null)
            w += TextRenderer.MeasureText(s.Text, Theme.Bold).Width + (s.Icon is not null ? Px(4) : 0);
        return Math.Max(w + Px(8), Px(36));
    }

    private int PreferredWidth => _segments.Sum(SegmentWidth) + Px(6);

    private Rectangle SegmentBounds(int index)
    {
        var x = Px(3);
        for (var i = 0; i < index; i++)
            x += SegmentWidth(_segments[i]);
        return new Rectangle(x, Px(3), SegmentWidth(_segments[index]), Height - Px(7));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Theme.FillRounded(g, new Rectangle(0, 0, Width - 1, Height - 1), Px(10), Theme.Raised, Theme.Border);
        for (var i = 0; i < _segments.Count; i++)
        {
            var r = SegmentBounds(i);
            var selected = i == _selected;
            if (selected)
                Theme.FillRounded(g, r, Px(7), Theme.Hover, Theme.BorderStrong);
            else if (i == _hover)
                Theme.FillRounded(g, r, Px(7), Color.FromArgb(0x21, 0x23, 0x29));

            var s = _segments[i];
            var color = selected ? Theme.Text : Theme.Muted;
            var textWidth = s.Text is null ? 0 : TextRenderer.MeasureText(s.Text, Theme.Bold).Width;
            var content = (s.Icon is null ? 0 : Px(18)) + (s.Icon is not null && s.Text is not null ? Px(4) : 0) + textWidth;
            var x = r.X + (r.Width - content) / 2;
            if (s.Icon is not null)
            {
                var iconColor = selected && s.Icon == Glyph.StarFilled ? Theme.Star : color;
                Theme.DrawIcon(g, s.Icon, new Rectangle(x, r.Y, Px(18), r.Height), iconColor);
                x += Px(22);
            }
            if (s.Text is not null)
                TextRenderer.DrawText(g, s.Text, Theme.Bold, new Rectangle(x, r.Y, textWidth + Px(2), r.Height), color, Theme.Line);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = Enumerable.Range(0, _segments.Count).FirstOrDefault(i => SegmentBounds(i).Contains(e.Location), -1);
        if (hit != _hover)
        {
            _hover = hit;
            _toolTip.SetToolTip(this, hit >= 0 ? _segments[hit].ToolTip : null);
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (_hover >= 0)
            Selected = _hover;
        base.OnMouseDown(e);
    }
}
