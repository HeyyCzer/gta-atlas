namespace GtaAtlas.App.Ui.Controls;

/// <summary>Rounded button with an optional icon; <see cref="Primary"/> uses the accent fill.</summary>
internal class PillButton : ThemedControl
{
    private string? _icon;
    private bool _pressed;

    public PillButton()
    {
        Cursor = Cursors.Hand;
        Height = Px(38);
        TabStop = false;
    }

    public string? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            AutoWidth();
        }
    }

    public Color? IconColor { get; set; }

    public bool Primary { get; set; }

    /// <summary>On state of a toggle: accent border over the hover fill.</summary>
    public bool Checked
    {
        get;
        set
        {
            field = value;
            Invalidate();
        }
    }

    /// <summary>Text drawn after the label in muted color (e.g. the current value of a dropdown).</summary>
    public virtual string? Trailing => null;

    public override string Text
    {
        get => base.Text;
#pragma warning disable CS8765
        set
        {
            base.Text = value;
            AutoWidth();
            Invalidate();
        }
#pragma warning restore CS8765
    }

    protected void AutoWidth()
    {
        var w = Px(16);
        if (_icon is not null)
            w += Px(20);
        if (!string.IsNullOrEmpty(Text))
            w += TextRenderer.MeasureText(Text, Theme.Bold).Width + Px(4);
        if (Trailing is { } t)
            w += TextRenderer.MeasureText(t, Theme.UiFont).Width + Px(4);
        w += ExtraWidth;
        Width = Math.Min(Math.Max(w, Height), MaxAutoWidth);
    }

    protected virtual int MaxAutoWidth => int.MaxValue;

    protected virtual int ExtraWidth => 0;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        var fill = Primary
            ? Hovered ? ControlPaint.Light(Theme.Accent, 0.1f) : Theme.Accent
            : _pressed || Checked ? Theme.Hover : Hovered ? Color.FromArgb(0x23, 0x25, 0x2B) : Theme.Raised;
        Theme.FillRounded(g, r, Px(10), fill, Primary ? null : Checked ? Theme.Accent : Hovered ? Theme.BorderStrong : Theme.Border);

        var fore = Primary ? Theme.Back : Enabled ? Theme.Text : Theme.Faint;
        var x = Px(10);
        if (_icon is not null)
        {
            Theme.DrawIcon(g, _icon, new Rectangle(x, 0, Px(20), Height), IconColor ?? fore);
            x += Px(22);
        }
        else
        {
            x = Px(12);
        }
        if (!string.IsNullOrEmpty(Text))
        {
            var w = TextRenderer.MeasureText(Text, Theme.Bold).Width;
            TextRenderer.DrawText(g, Text, Theme.Bold, new Rectangle(x, 0, w + Px(2), Height), fore, Theme.Line);
            x += w + Px(4);
        }
        if (Trailing is { } t)
            TextRenderer.DrawText(g, t, Theme.UiFont, new Rectangle(x, 0, Width - x - Px(8) - ExtraWidth, Height), Theme.Muted, Theme.Line);
        PaintExtra(g);
    }

    protected virtual void PaintExtra(Graphics g)
    {
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }
}
