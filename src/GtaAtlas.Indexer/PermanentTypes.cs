using System.Xml.Linq;

namespace GtaAtlas.Indexer;

/// <summary>
/// .ytyp files the game keeps resident all the time: <c>PERMANENT_ITYP_FILE</c> in the level meta (gta5.meta)
/// and <c>DLC_ITYP_REQUEST</c> in pack content.xml. Their archetypes can be spawned by scripts anywhere
/// (CREATE_OBJECT); every other ytyp only loads while the map section or interior that needs it streams in.
/// </summary>
public sealed class PermanentTypes
{
    private static readonly string[] FileTypes = ["PERMANENT_ITYP_FILE", "DLC_ITYP_REQUEST"];

    private readonly HashSet<string> _stems;

    private PermanentTypes(HashSet<string> stems)
    {
        _stems = stems;
    }

    public int Count => _stems.Count;

    /// <summary>True when the .ytyp at this path is always loaded.</summary>
    public bool Contains(string ytypPath) => _stems.Contains(Stem(ytypPath));

    public static PermanentTypes Find(GameArchive archive)
    {
        var stems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Every copy counts (title update patches of gta5.meta and content.xml add entries, never remove used ones).
        foreach (var entry in archive.Files.Where(f => f.NameLower is "gta5.meta" or "content.xml"))
        {
            string? xml;
            try
            {
                xml = GameArchive.ReadText(entry);
            }
            catch
            {
                continue;
            }
            if (xml is not null && xml.TrimStart().StartsWith('<'))
                stems.UnionWith(Parse(xml));
        }
        return new PermanentTypes(stems);
    }

    /// <summary>File stems of always-loaded ityp entries: "dlc_mpTuner:/%PLATFORM%/levels/gta5/props/tr_Prop_TR_Vehicles.ityp" → "tr_prop_tr_vehicles".</summary>
    internal static IEnumerable<string> Parse(string xml)
    {
        XDocument doc;
        try
        {
            // Level metas use <xi:include> without declaring the prefix, which XML parsers reject.
            doc = XDocument.Parse(xml.Replace("<xi:", "<", StringComparison.Ordinal));
        }
        catch (System.Xml.XmlException)
        {
            yield break;
        }

        foreach (var item in doc.Descendants("Item"))
        {
            if (!FileTypes.Contains(item.Element("fileType")?.Value.Trim()))
                continue;
            if (item.Element("filename")?.Value.Trim() is { Length: > 0 } file)
                yield return Stem(file);
        }
    }

    private static string Stem(string path)
    {
        var name = path[(path.LastIndexOfAny(['/', '\\']) + 1)..];
        var dot = name.LastIndexOf('.');
        return (dot > 0 ? name[..dot] : name).ToLowerInvariant();
    }
}
