namespace GtaAtlas.Core.Game;

public sealed record GameInstall(string Folder, bool Gen9)
{
    public const string LegacyExe = "GTA5.exe";
    public const string EnhancedExe = "GTA5_Enhanced.exe";

    public string ExePath => Path.Combine(Folder, Gen9 ? EnhancedExe : LegacyExe);

    /// <summary>Returns the install found in <paramref name="folder"/>, or null if it holds no game exe.</summary>
    public static GameInstall? TryFromFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return null;
        if (File.Exists(Path.Combine(folder, LegacyExe)))
            return new GameInstall(folder, false);
        if (File.Exists(Path.Combine(folder, EnhancedExe)))
            return new GameInstall(folder, true);
        return null;
    }
}
