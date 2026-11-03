namespace GtaAtlas.Core.Game;

/// <summary>Resolves the game install from settings (or auto-detection) and prepares decryption keys.</summary>
public sealed class GameSession
{
    private GameSession(GameInstall install)
    {
        Install = install;
    }

    public GameInstall Install { get; }

    /// <summary>Opens the configured install, auto-detecting and persisting it when unset. Throws if none is found.</summary>
    public static GameSession Open(AppPaths paths, AtlasSettings settings)
    {
        var install = GameInstall.TryFromFolder(settings.GameFolder)
                      ?? GameLocator.FindPreferred(settings.Gen9)
                      ?? throw new GameNotFoundException();

        var changed = settings.GameFolder != install.Folder || settings.Gen9 != install.Gen9;
        settings.GameFolder = install.Folder;
        settings.Gen9 = install.Gen9;

        changed |= GameKeys.EnsureLoaded(install, settings);
        if (changed)
            settings.Save(paths);

        return new GameSession(install);
    }
}

public sealed class GameNotFoundException() : Exception("GTA V install not found. Set the game folder in settings.");
