namespace GtaAtlas.App.Ui.Controls;

/// <summary>Rounded search field: magnifier, borderless text box, clear button and a shortcut hint.</summary>
internal sealed class SearchBox : ThemedControl
{
    private readonly TextBox _text = new()
    {
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Raised,
        ForeColor = Theme.Text,
        Font = Theme.UiFont,
    };
    private bool _clearHover;

    public SearchBox()
    {
        BackColor = Theme.Back;
        Cursor = Cursors.IBeam;
        Controls.Add(_text);
        _text.TextChanged += (_, _) =>
        {
            Invalidate();
            OnTextChanged(EventArgs.Empty);
        };
        _text.GotFocus += (_, _) => Invalidate();
        _text.LostFocus += (_, _) => Invalidate();
        _text.KeyDown += (_, e) => OnKeyDown(e);
        Height = Px(38);
    }

    public string Placeholder
    {
        get => _text.PlaceholderText;
        set => _text.PlaceholderText = value;
    }

    public string Hint { get; set; } = "Ctrl+F";

    public override string Text
    {
        get => _text.Text;
#pragma warning disable CS8765 // matches Control.Text
        set => _text.Text = value;
#pragma warning restore CS8765
    }

    public void FocusText()
    {
        _text.Focus();
        _text.SelectAll();
    }

    public bool TextFocused => _text.Focused;

    public void Clear() => _text.Clear();

    private Rectangle Box => new(0, 0, Width - 1, Height - 1);
    private Rectangle ClearBounds => new(Width - Px(36), (Height - Px(28)) / 2, Px(28), Px(28));

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var left = Px(40);
        var right = Px(_text.TextLength > 0 ? 44 : 70);
        _text.SetBounds(left, (Height - _text.PreferredHeight) / 2 + 1, Math.Max(10, Width - left - right), _text.PreferredHeight);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        PerformLayout();
        base.OnTextChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var focused = _text.Focused;
        Theme.FillRounded(g, Box, Px(10), Theme.Raised, focused ? Theme.Accent : Hovered ? Theme.BorderStrong : Theme.Border);
        Theme.DrawIcon(g, Glyph.Search, new Rectangle(Px(10), 0, Px(24), Height), focused ? Theme.Accent : Theme.Muted);

        if (_text.TextLength == 0)
        {
            if (Hint.Length == 0)
                return;
            var hintSize = TextRenderer.MeasureText(Hint, Theme.Caption);
            var hint = new Rectangle(Width - hintSize.Width - Px(18), (Height - Px(20)) / 2, hintSize.Width + Px(8), Px(20));
            Theme.FillRounded(g, hint, Px(4), Theme.Hover);
            TextRenderer.DrawText(g, Hint, Theme.Caption, hint, Theme.Muted, Theme.Centered);
        }
        else
        {
            if (_clearHover)
                Theme.FillRounded(g, ClearBounds, Px(6), Theme.Hover);
            Theme.DrawIcon(g, Glyph.Clear, ClearBounds, _clearHover ? Theme.Text : Theme.Muted, Theme.IconSmall);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var over = _text.TextLength > 0 && ClearBounds.Contains(e.Location);
        if (over != _clearHover)
        {
            _clearHover = over;
            Cursor = over ? Cursors.Hand : Cursors.IBeam;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (_text.TextLength > 0 && ClearBounds.Contains(e.Location))
            _text.Clear();
        _text.Focus();
        base.OnMouseDown(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        _text.Focus();
        base.OnGotFocus(e);
    }
}
