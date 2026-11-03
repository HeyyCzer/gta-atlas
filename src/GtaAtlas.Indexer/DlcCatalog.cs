using System.Xml;
using CodeWalker.GameFiles;
using GtaAtlas.Data;

namespace GtaAtlas.Indexer;

/// <summary>Builds the DLC load order from dlclist.xml + each pack's setup2.xml.</summary>
public static class DlcCatalog
{
    private const string DlcListPath = "update\\update.rpf\\common\\data\\dlclist.xml";

    public static List<DlcInput> Build(GameArchive archive, Action<string>? error = null)
    {
        var listed = ReadDlcList(archive.Manager, error);

        // Prefer the copy under update\ when a pack exists in both x64\dlcpacks and update\x64\dlcpacks.
        var packRpfs = archive.Manager.AllRpfs
            .Where(r => r.NameLower == "dlc.rpf")
            .GroupBy(r => SourcePaths.DlcOf(r.Path))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Path.StartsWith("update\\", StringComparison.OrdinalIgnoreCase)).First());

        var packs = new List<(string Name, int ListIndex, DlcSetupFile? Setup)>();
        for (var i = 0; i < listed.Count; i++)
        {
            var name = listed[i];
            if (!packRpfs.TryGetValue(name, out var rpf))
                continue;
            packs.Add((name, i, ReadSetup(archive.Manager, rpf, error)));
        }

        // The game mounts packs by setup2 "order" (dlclist position breaks ties); clothing collection
        // numbering confirms it. Patch packs carry low orders but mostly ship overlays, which
        // AssetMerger does not treat as introducing content.
        var ordered = packs
            .OrderBy(p => p.Setup?.order ?? int.MaxValue)
            .ThenBy(p => p.ListIndex)
            .ToList();

        var result = new List<DlcInput> { new(DlcNames.Base, 0) };
        for (var i = 0; i < ordered.Count; i++)
        {
            var (name, _, setup) = ordered[i];
            result.Add(new DlcInput(name, i + 1, setup?.deviceName, setup?.order, setup?.timeStamp));
        }
        result.Add(new DlcInput(DlcNames.Update, ordered.Count + 1));
        return result;
    }

    /// <summary>Appends any DLC referenced by sources but missing from dlclist.xml (unused/extra packs).</summary>
    public static List<DlcInput> WithUnlisted(List<DlcInput> dlcs, IEnumerable<string> used)
    {
        var known = dlcs.Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var next = dlcs.Max(d => d.OrderIndex) + 1;
        var result = dlcs.ToList();
        foreach (var name in used.Where(n => !known.Contains(n)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
            result.Add(new DlcInput(name, next++));
        return result;
    }

    /// <summary>Pack names in dlclist.xml order, e.g. "platform:/dlcPacks/mpBeach/" → "mpbeach".</summary>
    internal static List<string> ParseDlcList(XmlDocument doc)
    {
        var names = new List<string>();
        // The real file mixes <Item> and <item>.
        var items = (doc.SelectNodes("//Paths/*")?.Cast<XmlNode>() ?? [])
            .Where(n => n.NodeType == XmlNodeType.Element && n.Name.Equals("item", StringComparison.OrdinalIgnoreCase));
        foreach (var item in items)
        {
            var parts = item.InnerText.Trim().Replace('\\', '/').TrimEnd('/').Split('/');
            if (parts.Length > 0 && parts[^1].Length > 0)
                names.Add(parts[^1].ToLowerInvariant());
        }
        return names;
    }

    private static List<string> ReadDlcList(RpfManager manager, Action<string>? error)
    {
        var doc = manager.GetFileXml(DlcListPath);
        if (doc?.DocumentElement is null)
        {
            error?.Invoke($"DLC list not found at {DlcListPath}");
            return [];
        }
        return ParseDlcList(doc);
    }

    private static DlcSetupFile? ReadSetup(RpfManager manager, RpfFile rpf, Action<string>? error)
    {
        try
        {
            var doc = manager.GetFileXml(rpf.Path + "\\setup2.xml");
            if (doc?.DocumentElement is null)
                return null;
            var setup = new DlcSetupFile();
            setup.Load(doc);
            return setup;
        }
        catch (Exception e)
        {
            error?.Invoke($"setup2.xml of {rpf.Path}: {e.Message}");
            return null;
        }
    }
}
