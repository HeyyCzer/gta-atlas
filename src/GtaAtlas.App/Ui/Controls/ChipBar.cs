namespace GtaAtlas.App.Ui.Controls;

internal sealed record Chip(string Value, string? ToolTip = null);

/// <summary>One row of toggleable pill chips; scrolls horizontally with the mouse wheel when it overflows.</summary>
internal sealed class ChipBar : ThemedControl
{
    private readonly ToolTip _toolTip = new();
    private IReadOnlyList<Chip> _chips = [];
    private HashSet<string> _active = [];
    private int _hover = -1;
    private int _scroll;

    public ChipBar()
    {
        Height = Px(34);
    }

    public event EventHandler<string>? Toggled;

    public void SetChips(IReadOnlyList<Chip> chips, IEnumerable<string> active)
    {
        _chips = chips;
        _active = [.. active];
        _scroll = 0;
        _hover = -1;
        Invalidate();
    }

    private int ChipWidth(Chip c) => TextRenderer.MeasureText(c.Value, Theme.UiFont).Width + Px(_active.Contains(c.Value) ? 34 : 20);

    private Rectangle ChipBounds(int index)
    {
        var x = -_scroll;
        for (var i = 0; i < index; i++)
            x += ChipWidth(_chips[i]) + Px(6);
        return new Rectangle(x, Px(2), ChipWidth(_chips[index]), Height - Px(5));
    }

    private int ContentWidth => _chips.Count == 0 ? 0 : _chips.Sum(c => ChipWidth(c) + Px(6)) - Px(6);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        for (var i = 0; i < _chips.Count; i++)
        {
            var r = ChipBounds(i);
            if (r.Right < 0)
                continue;
            if (r.Left > Width)
                break;
            var on = _active.Contains(_chips[i].Value);
            var fill = on ? Theme.AccentSoft : i == _hover ? Theme.Hover : Theme.Raised;
            Theme.FillRounded(g, r, r.Height / 2, fill, on ? Theme.Accent : Theme.Border);
            var text = r;
            if (on)
            {
                Theme.DrawIcon(g, Glyph.Clear, new Rectangle(r.Right - Px(24), r.Y, Px(16), r.Height), Theme.Accent, Theme.IconSmall);
                text.Width -= Px(14);
            }
            TextRenderer.DrawText(g, _chips[i].Value, Theme.UiFont, text, on ? Theme.Accent : Theme.Text, Theme.Centered);
        }

        // Fade the clipped edge so overflow reads as "scroll for more".
        if (ContentWidth - _scroll > Width)
        {
            var fade = new Rectangle(Width - Px(40), 0, Px(40), Height);
            using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(fade, Color.FromArgb(0, BackColor), BackColor, 0f);
            g.FillRectangle(brush, fade);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        _scroll = Math.Clamp(_scroll - e.Delta / 2, 0, Math.Max(0, ContentWidth - Width + Px(40)));
        Invalidate();
        base.OnMouseWheel(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = Enumerable.Range(0, _chips.Count).FirstOrDefault(i => ChipBounds(i).Contains(e.Location), -1);
        if (hit != _hover)
        {
            _hover = hit;
            Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
            _toolTip.SetToolTip(this, hit >= 0 ? _chips[hit].ToolTip : null);
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
        {
            var value = _chips[_hover].Value;
            if (!_active.Remove(value))
                _active.Add(value);
            Invalidate();
            Toggled?.Invoke(this, value);
        }
        base.OnMouseDown(e);
    }
}
