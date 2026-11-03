namespace GtaAtlas.Core;

/// <summary>Locations of user data (%LOCALAPPDATA%\GtaAtlas by default).</summary>
public sealed class AppPaths
{
    public AppPaths(string root)
    {
        Root = root;
    }

    public static AppPaths Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GtaAtlas"));

    public string Root { get; }
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string Database => Path.Combine(Root, "atlas.db");
    public string Thumbnails => Path.Combine(Root, "thumbs");
    public string Logs => Path.Combine(Root, "logs");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Thumbnails);
        Directory.CreateDirectory(Logs);
    }
}
