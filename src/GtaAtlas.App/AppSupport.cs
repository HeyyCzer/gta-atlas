using GtaAtlas.Core;

namespace GtaAtlas.App;

internal static class AppIcon
{
    private static Icon? _icon;

    public static Icon Get() => _icon ??= new Icon(typeof(AppIcon).Assembly.GetManifestResourceStream("atlas.ico")!);
}

internal static class AppLog
{
    /// <summary>Appends lines to logs\{name}.log under the user data folder; logging never throws.</summary>
    public static void Write(string name, IEnumerable<string> lines)
    {
        try
        {
            var paths = AppPaths.Default;
            paths.EnsureCreated();
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            File.AppendAllLines(Path.Combine(paths.Logs, name + ".log"), lines.Select(l => $"{stamp} {l}"));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
