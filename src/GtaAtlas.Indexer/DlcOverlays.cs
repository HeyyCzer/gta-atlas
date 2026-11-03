using System.Xml.Linq;
using CodeWalker.GameFiles;

namespace GtaAtlas.Indexer;

/// <summary>
/// RPFs that packs mount as overlays (content.xml <c>overlay=true</c>). Their files only replace files
/// that already exist elsewhere; they never introduce new content (e.g. new clothing collections).
/// </summary>
public sealed class DlcOverlays
{
    private readonly List<string> _prefixes;

    private DlcOverlays(List<string> prefixes)
    {
        _prefixes = prefixes;
    }

    public int Count => _prefixes.Count;

    public bool Contains(string entryPath) =>
        _prefixes.Any(p => entryPath.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    public static DlcOverlays Find(GameArchive archive)
    {
        var prefixes = new List<string>();
        foreach (var dlc in archive.Manager.AllRpfs.Where(r => r.NameLower == "dlc.rpf"))
        {
            var name = SourcePaths.DlcOf(dlc.Path);
            // A title update can patch the pack's content.xml.
            var xml = ReadXml(archive.Manager, $"update\\update.rpf\\dlc_patch\\{name}\\content.xml")
                      ?? ReadXml(archive.Manager, dlc.Path + "\\content.xml");
            if (xml is null)
                continue;
            prefixes.AddRange(ParseOverlayRpfs(xml).Select(rel => dlc.Path + "\\" + rel + "\\"));
        }
        return new DlcOverlays(prefixes);
    }

    /// <summary>
    /// Paths inside dlc.rpf of overlay RPF_FILE items:
    /// "dlc_patchDay5NG:/%PLATFORM%/models/cdimages/patchday5ng_male.rpf" → "x64\models\cdimages\patchday5ng_male.rpf".
    /// </summary>
    internal static IEnumerable<string> ParseOverlayRpfs(string xml)
    {
        var items = XDocument.Parse(xml).Root?.Element("dataFiles")?.Elements("Item") ?? [];
        foreach (var item in items)
        {
            if (item.Element("fileType")?.Value.Trim() != "RPF_FILE")
                continue;
            if (!string.Equals(item.Element("overlay")?.Attribute("value")?.Value, "true", StringComparison.OrdinalIgnoreCase))
                continue;
            var file = item.Element("filename")?.Value.Trim();
            var colon = file?.IndexOf(":/", StringComparison.Ordinal) ?? -1;
            if (file is null || colon < 0)
                continue;
            yield return file[(colon + 2)..].Replace("%PLATFORM%", "x64", StringComparison.OrdinalIgnoreCase).Replace('/', '\\').ToLowerInvariant();
        }
    }

    private static string? ReadXml(RpfManager manager, string path) =>
        manager.GetEntry(path) is RpfFileEntry entry ? GameArchive.ReadText(entry) : null;
}
