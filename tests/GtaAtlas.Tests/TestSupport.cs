using GtaAtlas.Core;
using GtaAtlas.Core.Game;

namespace GtaAtlas.Tests;

internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GtaAtlasTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Sub(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch (IOException) { }
    }
}

internal static class TestEnv
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string CodeWalkerDir => Path.Combine(RepoRoot, "external", "CodeWalker");

    /// <summary>Real game install for integration tests, or null (tests skip) when the machine has none.</summary>
    public static GameInstall? Game { get; } =
        GameInstall.TryFromFolder(Environment.GetEnvironmentVariable("GTA_ATLAS_GAME_DIR")) ?? GameLocator.FindPreferred(gen9: false);

    public static GameInstall RequireGame()
    {
        Assert.SkipWhen(Game is null, "GTA V Legacy not installed (set GTA_ATLAS_GAME_DIR).");
        return Game!;
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GtaAtlas.slnx")))
                return dir.FullName;
        }
        throw new InvalidOperationException("Repo root not found.");
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GameCollection
{
    // CodeWalker keeps decryption keys in static state: game tests must not run in parallel.
    public const string Name = "Game";
}

internal static class AppPathsExtensions
{
    public static AppPaths ToAppPaths(this TempDir dir) => new(dir.Path);
}
