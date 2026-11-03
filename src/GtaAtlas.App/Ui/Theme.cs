namespace GtaAtlas.App.Ui;

/// <summary>Palette for owner-drawn controls; stock controls follow the system dark color mode.</summary>
internal static class Theme
{
    public static readonly Color Back = Color.FromArgb(0x17, 0x18, 0x1C);
    public static readonly Color Panel = Color.FromArgb(0x1F, 0x20, 0x26);
    public static readonly Color Card = Color.FromArgb(0x26, 0x28, 0x2F);
    public static readonly Color CardHover = Color.FromArgb(0x2E, 0x30, 0x38);
    public static readonly Color Selected = Color.FromArgb(0x1C, 0x4A, 0x47);
    public static readonly Color Border = Color.FromArgb(0x34, 0x36, 0x3F);
    public static readonly Color Accent = Color.FromArgb(0x3F, 0xB8, 0xAF);
    public static readonly Color Text = Color.FromArgb(0xE6, 0xE6, 0xE6);
    public static readonly Color Muted = Color.FromArgb(0x9A, 0x9C, 0xA5);
    public static readonly Color Badge = Color.FromArgb(0x35, 0x38, 0x44);

    private static readonly Color[] KindColors =
    [
        Color.FromArgb(0x4E, 0x8F, 0xD6), Color.FromArgb(0xD6, 0x8A, 0x4E), Color.FromArgb(0x8F, 0x6E, 0xD6),
        Color.FromArgb(0xD6, 0x5E, 0x8A), Color.FromArgb(0x5E, 0xB8, 0x6E), Color.FromArgb(0xC9, 0xB8, 0x4E),
        Color.FromArgb(0x4E, 0xB8, 0xC9), Color.FromArgb(0xB8, 0x7A, 0x5E),
    ];

    /// <summary>Stable color per kind for placeholder tiles.</summary>
    public static Color KindColor(string kind)
    {
        var h = 0;
        foreach (var c in kind)
            h = h * 31 + c;
        return KindColors[(h & int.MaxValue) % KindColors.Length];
    }

    public static Font UiFont { get; } = new("Segoe UI", 9f);
    public static Font Bold { get; } = new("Segoe UI Semibold", 9.5f);
    public static Font Small { get; } = new("Segoe UI", 8f);
    public static Font Title { get; } = new("Segoe UI Semibold", 13f);
    public static Font Tile { get; } = new("Segoe UI Semibold", 11f);
}
