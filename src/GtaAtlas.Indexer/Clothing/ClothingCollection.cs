namespace GtaAtlas.Indexer.Clothing;

/// <summary>
/// Drawables of one freemode ped variation file (.ymt). Base file and each DLC collection
/// (e.g. mp_m_freemode_01_mp_m_2023_02) is one collection.
/// </summary>
public sealed record ClothingCollection
{
    /// <summary>File stem, e.g. "mp_m_freemode_01_mp_m_2023_02"; equals ShopPedApparel.fullDlcName.</summary>
    public required string Name { get; init; }
    public required string Ped { get; init; }
    public required string Dlc { get; init; }
    public required string Path { get; init; }

    /// <summary>File lives in an overlay RPF: it can update an existing collection but not create one.</summary>
    public bool Overlay { get; init; }

    /// <summary>Texture count of each drawable, per component slot (0–11).</summary>
    public required IReadOnlyDictionary<int, IReadOnlyList<int>> Components { get; init; }

    /// <summary>Texture count of each prop, per anchor slot, in prop id order.</summary>
    public required IReadOnlyDictionary<int, IReadOnlyList<int>> Props { get; init; }
}

public sealed record NumberedDrawable(
    ClothingCollection Collection,
    bool Prop,
    int Slot,
    int LocalIndex,
    int GlobalIndex,
    int Textures);

public static class ClothingNumbering
{
    /// <summary>
    /// Assigns global drawable/prop indices per ped and slot: base collection first, then DLC
    /// collections by load order (<paramref name="dlcOrder"/>), each appended after the previous ones.
    /// A collection re-shipped by a later pack (patchday) keeps its original position but uses the
    /// newest file's contents. Collections that only exist in overlay RPFs are never registered by the game.
    /// </summary>
    public static List<NumberedDrawable> Assign(IEnumerable<ClothingCollection> files, Func<string, int> dlcOrder)
    {
        var result = new List<NumberedDrawable>();
        foreach (var ped in files.GroupBy(f => f.Ped, StringComparer.OrdinalIgnoreCase))
        {
            var collections = ped
                .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Any(f => !f.Overlay))
                .Select(g =>
                {
                    var sorted = g.OrderBy(f => dlcOrder(f.Dlc)).ThenBy(f => f.Path, StringComparer.Ordinal).ToList();
                    var isBase = g.Key.Equals(ped.Key, StringComparison.OrdinalIgnoreCase);
                    var origin = sorted.First(f => !f.Overlay);
                    return (Position: isBase ? int.MinValue : dlcOrder(origin.Dlc), Latest: sorted[^1] with { Dlc = origin.Dlc });
                })
                .OrderBy(c => c.Position)
                .ThenBy(c => c.Latest.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => c.Latest)
                .ToList();

            var next = new Dictionary<(bool, int), int>();
            foreach (var c in collections)
            {
                Number(c, prop: false, c.Components);
                Number(c, prop: true, c.Props);
            }

            void Number(ClothingCollection c, bool prop, IReadOnlyDictionary<int, IReadOnlyList<int>> slots)
            {
                foreach (var (slot, textures) in slots.OrderBy(s => s.Key))
                {
                    var start = next.GetValueOrDefault((prop, slot));
                    for (var i = 0; i < textures.Count; i++)
                        result.Add(new NumberedDrawable(c, prop, slot, i, start + i, textures[i]));
                    next[(prop, slot)] = start + textures.Count;
                }
            }
        }
        return result;
    }
}
