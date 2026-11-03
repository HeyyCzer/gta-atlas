using CodeWalker.GameFiles;

namespace GtaAtlas.Indexer;

/// <summary>English (american) GXT2 strings, keyed by label hash.</summary>
public sealed class GameText
{
    private readonly Dictionary<uint, string> _strings;

    private GameText(Dictionary<uint, string> strings)
    {
        _strings = strings;
    }

    public int Count => _strings.Count;

    public static GameText Load(GameArchive archive)
    {
        var strings = new Dictionary<uint, string>();
        // Base files first so DLC/update strings override them.
        var files = archive.Files
            .Where(e => e.NameLower.EndsWith(".gxt2") && e.Path.Contains("\\american", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => SourcePaths.DlcOf(e.Path) == Data.DlcNames.Base ? 0 : 1);

        foreach (var entry in files)
        {
            var gxt = GameArchive.Load<Gxt2File>(entry);
            if (gxt?.TextEntries is null)
                continue;
            foreach (var t in gxt.TextEntries)
                strings[t.Hash] = t.Text;
        }
        return new GameText(strings);
    }

    public string? Get(string? label)
    {
        if (string.IsNullOrEmpty(label))
            return null;
        return _strings.TryGetValue(Names.Joaat(label), out var s) && !string.IsNullOrWhiteSpace(s) && s != "NULL" ? s : null;
    }
}
