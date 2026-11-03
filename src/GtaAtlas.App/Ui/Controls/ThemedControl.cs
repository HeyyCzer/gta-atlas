namespace GtaAtlas.App.Ui.Controls;

/// <summary>Double-buffered owner-drawn control with DPI helpers and hover tracking.</summary>
internal abstract class ThemedControl : Control
{
    protected ThemedControl()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = Theme.UiFont;
    }

    protected bool Hovered { get; private set; }

    protected int Px(int logical) => LogicalToDeviceUnits(logical);

    protected override void OnMouseEnter(EventArgs e)
    {
        Hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        Hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }
}
