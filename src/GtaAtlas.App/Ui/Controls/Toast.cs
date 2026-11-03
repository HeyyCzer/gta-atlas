namespace GtaAtlas.App.Ui.Controls;

/// <summary>Short confirmation pill ("Copied …") centered at the bottom of its parent; hides itself.</summary>
internal sealed class Toast : ThemedControl
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1800 };
    private string _icon = Glyph.Check;

    public Toast()
    {
        Visible = false;
        Height = Px(40);
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Visible = false;
        };
    }

    public void Show(string text, string? icon = null)
    {
        if (Parent is null)
            return;
        Text = text.Length > 90 ? text[..87] + "…" : text;
        _icon = icon ?? Glyph.Check;
        BackColor = Parent.BackColor;
        Width = TextRenderer.MeasureText(Text, Theme.UiFont).Width + Px(60);
        Location = new Point((Parent.ClientSize.Width - Width) / 2, Parent.ClientSize.Height - Height - Px(24));
        Visible = true;
        BringToFront();
        Invalidate();
        _timer.Stop();
        _timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Theme.FillRounded(g, new Rectangle(0, 0, Width - 1, Height - 1), Height / 2, Theme.Hover, Theme.BorderStrong);
        Theme.DrawIcon(g, _icon, new Rectangle(Px(14), 0, Px(20), Height), Theme.Accent);
        TextRenderer.DrawText(g, Text, Theme.UiFont, new Rectangle(Px(40), 0, Width - Px(52), Height), Theme.Text, Theme.Line);
    }
}
