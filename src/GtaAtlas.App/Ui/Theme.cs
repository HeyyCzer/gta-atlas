using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace GtaAtlas.App.Ui;

/// <summary>Palette, fonts, icon glyphs and drawing helpers shared by the owner-drawn controls.</summary>
internal static partial class Theme
{
    public static readonly Color Back = Color.FromArgb(0x0F, 0x10, 0x13);
    public static readonly Color Surface = Color.FromArgb(0x16, 0x17, 0x1B);
    public static readonly Color Raised = Color.FromArgb(0x1D, 0x1F, 0x24);
    public static readonly Color Hover = Color.FromArgb(0x25, 0x27, 0x2E);
    public static readonly Color Border = Color.FromArgb(0x2A, 0x2C, 0x33);
    public static readonly Color BorderStrong = Color.FromArgb(0x3A, 0x3D, 0x46);
    public static readonly Color Accent = Color.FromArgb(0x4C, 0xC9, 0xBE);
    public static readonly Color AccentSoft = Color.FromArgb(0x16, 0x3A, 0x38);
    public static readonly Color Text = Color.FromArgb(0xEC, 0xED, 0xEF);
    public static readonly Color Muted = Color.FromArgb(0x8B, 0x8F, 0x99);
    public static readonly Color Faint = Color.FromArgb(0x5C, 0x60, 0x6A);
    public static readonly Color Star = Color.FromArgb(0xF2, 0xC1, 0x4E);

    private static readonly Color[] KindColors =
    [
        Color.FromArgb(0x5B, 0x9C, 0xF0), Color.FromArgb(0xF0, 0x9A, 0x5B), Color.FromArgb(0xA1, 0x84, 0xF0),
        Color.FromArgb(0xF0, 0x6E, 0x9C), Color.FromArgb(0x5B, 0xC9, 0x7A), Color.FromArgb(0xE0, 0xC4, 0x5B),
        Color.FromArgb(0x5B, 0xC9, 0xE0), Color.FromArgb(0xC9, 0x8A, 0x6E),
    ];

    /// <summary>Stable color per kind for placeholder tiles.</summary>
    public static Color KindColor(string kind)
    {
        var h = 0;
        foreach (var c in kind)
            h = h * 31 + c;
        return KindColors[(h & int.MaxValue) % KindColors.Length];
    }

    private static string Family(params string[] names)
    {
        using var installed = new InstalledFontCollection();
        return names.FirstOrDefault(n => installed.Families.Any(f => f.Name == n)) ?? "Segoe UI";
    }

    private static readonly string TextFamily = Family("Segoe UI Variable Text", "Segoe UI");
    private static readonly string SemiboldFamily = Family("Segoe UI Variable Text Semibold", "Segoe UI Semibold");
    private static readonly string DisplayFamily = Family("Segoe UI Variable Display Semib", "Segoe UI Semibold");
    private static readonly string IconFamily = Family("Segoe Fluent Icons", "Segoe MDL2 Assets");
    private static readonly string MonoFamily = Family("Cascadia Mono", "Consolas");

    public static Font UiFont { get; } = new(TextFamily, 9.5f);
    public static Font Small { get; } = new(TextFamily, 8.5f);
    public static Font Caption { get; } = new(SemiboldFamily, 7.5f);
    public static Font Bold { get; } = new(SemiboldFamily, 9.5f);
    public static Font Title { get; } = new(DisplayFamily, 15f);
    public static Font PageTitle { get; } = new(DisplayFamily, 18f);
    public static Font Mono { get; } = new(MonoFamily, 8.5f);
    public static Font Icon { get; } = new(IconFamily, 10f);
    public static Font IconSmall { get; } = new(IconFamily, 8f);
    public static Font IconLarge { get; } = new(IconFamily, 30f);

    public const TextFormatFlags Line =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

    public const TextFormatFlags Centered =
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
            path.AddRectangle(r);
        else
            path.AddRoundedRectangle(r, new Size(radius * 2, radius * 2));
        return path;
    }

    public static void FillRounded(Graphics g, Rectangle r, int radius, Color fill, Color? border = null)
    {
        var mode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(r, radius);
        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);
        if (border is { } b)
        {
            using var pen = new Pen(b);
            g.DrawPath(pen, path);
        }
        g.SmoothingMode = mode;
    }

    public static void DrawIcon(Graphics g, string glyph, Rectangle r, Color color, Font? font = null) =>
        TextRenderer.DrawText(g, glyph, font ?? Icon, r, color, Centered);

    /// <summary>Dark caption bar matching the window background (Windows 11; ignored elsewhere).</summary>
    public static void ApplyWindowChrome(Form form)
    {
        form.HandleCreated += (_, _) =>
        {
            var dark = 1;
            DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
            var caption = ColorTranslator.ToWin32(Back);
            DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int)); // DWMWA_CAPTION_COLOR
            var border = ColorTranslator.ToWin32(Border);
            DwmSetWindowAttribute(form.Handle, 34, ref border, sizeof(int)); // DWMWA_BORDER_COLOR
        };
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

/// <summary>Segoe Fluent Icons code points.</summary>
internal static class Glyph
{
    public const string Search = "\uE721";
    public const string Clear = "\uE711";
    public const string Copy = "\uE8C8";
    public const string Star = "\uE734";
    public const string StarFilled = "\uE735";
    public const string Grid = "\uF0E2";
    public const string List = "\uE8FD";
    public const string Sync = "\uE895";
    public const string ChevronDown = "\uE70D";
    public const string SortUp = "\uE70E";
    public const string SortDown = "\uE70D";
    public const string ChevronLeft = "\uE76B";
    public const string ChevronRight = "\uE76C";
    public const string Check = "\uE73E";
    public const string Package = "\uE7B8";
    public const string Car = "\uE804";
    public const string Person = "\uE77B";
    public const string Shop = "\uE719";
    public const string Play = "\uE768";
    public const string Library = "\uE8F1";
    public const string Home = "\uE80F";
    public const string Layers = "\uE81E";
    public const string MapPin = "\uE707";
    public const string Apps = "\uE71D";
    public const string Puzzle = "\uEA86";
    public const string Cube = "\uF158";
    public const string Photo = "\uE91B";
    public const string Stop = "\uE71A";
}
