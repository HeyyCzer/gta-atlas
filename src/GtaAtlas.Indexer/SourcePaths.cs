using GtaAtlas.Data;

namespace GtaAtlas.Indexer;

public static class SourcePaths
{
    /// <summary>
    /// DLC that owns a file, from its path inside the game folder:
    /// <c>update\x64\dlcpacks\mpheist4\dlc.rpf\...</c> → mpheist4,
    /// <c>update\update.rpf\dlc_patch\mpheist4\...</c> → mpheist4 (title-update patch of that pack),
    /// other <c>update\...</c> → update, everything else → base.
    /// </summary>
    public static string DlcOf(string path)
    {
        var p = path.Replace('/', '\\').ToLowerInvariant();

        var packs = FolderAfter(p, "\\dlcpacks\\") ?? (p.StartsWith("dlcpacks\\") ? FolderAfter("\\" + p, "\\dlcpacks\\") : null);
        if (packs is not null)
            return packs;

        var patch = FolderAfter(p, "\\dlc_patch\\");
        if (patch is not null)
            return patch;

        return p.StartsWith("update\\") ? DlcNames.Update : DlcNames.Base;
    }

    private static string? FolderAfter(string path, string marker)
    {
        var i = path.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0)
            return null;
        var start = i + marker.Length;
        var end = path.IndexOf('\\', start);
        var name = end < 0 ? path[start..] : path[start..end];
        return name.Length > 0 ? name : null;
    }

    public static bool IsMod(string path) => path.StartsWith("mods\\", StringComparison.OrdinalIgnoreCase);
}
