using CodeWalker.GameFiles;

namespace GtaAtlas.Core.Game;

/// <summary>Loads the RPF decryption keys CodeWalker needs, caching the AES key in settings.</summary>
public static class GameKeys
{
    private static readonly Lock Gate = new();
    private static GameInstall? _loadedFor;

    /// <summary>Loads keys for <paramref name="install"/>. Returns true if <paramref name="settings"/> changed and should be saved.</summary>
    public static bool EnsureLoaded(GameInstall install, AtlasSettings settings)
    {
        lock (Gate)
        {
            if (_loadedFor != install)
            {
                try
                {
                    GTA5Keys.LoadFromPath(install.Folder, install.Gen9, settings.AesKey);
                }
                catch when (settings.AesKey is not null)
                {
                    // Cached key is stale (game updated or switched install): rescan the exe.
                    GTA5Keys.LoadFromPath(install.Folder, install.Gen9, null);
                }
                _loadedFor = install;
            }

            var key = Convert.ToBase64String(GTA5Keys.PC_AES_KEY);
            if (key == settings.AesKey)
                return false;

            settings.AesKey = key;
            return true;
        }
    }
}
