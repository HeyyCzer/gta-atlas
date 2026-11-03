using System.Collections.Concurrent;
using CodeWalker.GameFiles;
using GtaAtlas.Data;

namespace GtaAtlas.Indexer.Clothing;

/// <summary>
/// Freemode clothing. Needs every collection at once (global indices depend on load order),
/// so it runs as one pass after the per-file extractors.
/// </summary>
public static class ClothingIndexer
{
    public static List<RawAsset> Extract(GameArchive archive, IReadOnlyList<DlcInput> dlcs, DlcOverlays overlays, bool gen9, GameText text, Action<string> error)
    {
        var order = dlcs.ToDictionary(d => d.Name, d => d.OrderIndex, StringComparer.OrdinalIgnoreCase);
        int DlcOrder(string dlc) => order.TryGetValue(dlc, out var o) ? o : int.MaxValue;

        var collections = ReadCollections(archive, error)
            .Select(c => c with { Overlay = overlays.Contains(c.Path) })
            // Legacy never registers Enhanced-only (g9ec) collections; counting them shifts every later index.
            .Where(c => gen9 || !DlcNames.IsEnhancedOnly(SourcePaths.DlcOf(c.Path)))
            .ToList();
        var shop = ReadShopNames(archive, DlcOrder, error);

        return ClothingNumbering.Assign(collections, DlcOrder)
            .Select(d => ToRaw(d, shop, text))
            .ToList();
    }

    private static RawAsset ToRaw(NumberedDrawable d, ShopNames shop, GameText text)
    {
        var c = d.Collection;
        var gender = ClothingSlots.Peds[c.Ped];
        var type = d.Prop ? 'P' : 'C';
        var name = $"{type}/{d.Slot}/{d.GlobalIndex}/{gender}";
        var slotName = ClothingSlots.SlotName(d.Prop, d.Slot);

        var textures = Enumerable.Range(0, d.Textures).Select(t =>
        {
            var item = shop.Find(c.Name, d.Prop, d.Slot, d.LocalIndex, t);
            return new
            {
                texture = t,
                name = text.Get(item?.TextLabel),
                label = item?.TextLabel,
                devName = item?.DevName,
            };
        }).ToList();

        return new RawAsset
        {
            Kind = AssetKind.Clothing,
            Key = name.ToLowerInvariant(),
            Name = name,
            Hash = Names.Joaat(name),
            Path = c.Path,
            Dlc = c.Dlc,
            DisplayName = textures.Select(t => t.name).FirstOrDefault(n => n is not null),
            Data = new
            {
                ped = c.Ped,
                gender = gender.ToString(),
                prop = d.Prop,
                slot = d.Slot,
                slotName,
                drawable = d.GlobalIndex,
                localDrawable = d.LocalIndex,
                collection = c.Name,
                textures,
            },
            Tags = [gender == 'M' ? "Male" : "Female", d.Prop ? "Prop" : "Component", slotName],
        };
    }

    private static List<ClothingCollection> ReadCollections(GameArchive archive, Action<string> error)
    {
        var files = archive.Files.Where(e =>
            e.NameLower.EndsWith(".ymt") && ClothingSlots.Peds.Keys.Any(p => e.NameLower.StartsWith(p, StringComparison.Ordinal)));

        var result = new ConcurrentBag<ClothingCollection>();
        Parallel.ForEach(files, entry =>
        {
            try
            {
                if (ReadCollection(entry) is { } c)
                    result.Add(c);
            }
            catch (Exception e)
            {
                error($"{entry.Path}: {e.Message}");
            }
        });
        return result.ToList();
    }

    private static ClothingCollection? ReadCollection(RpfFileEntry entry)
    {
        var info = GameArchive.Load<PedFile>(entry)?.VariationInfo;
        if (info is null)
            return null;

        var stem = entry.NameLower[..^4];
        var ped = ClothingSlots.Peds.Keys.First(p => stem.StartsWith(p, StringComparison.Ordinal));

        var components = new Dictionary<int, IReadOnlyList<int>>();
        var indices = info.ComponentIndices ?? [];
        for (var slot = 0; slot < indices.Length && slot < ClothingSlots.ComponentCodes.Length; slot++)
        {
            var i = indices[slot];
            if (i == 255 || info.ComponentData3 is null || i >= info.ComponentData3.Length)
                continue;
            var drawables = info.ComponentData3[i].DrawblData3 ?? [];
            components[slot] = drawables.Select(d => d.TexData?.Length ?? 0).ToList();
        }

        var props = (info.PropInfo?.PropMetaData ?? [])
            .GroupBy(p => (int)p.Data.anchorId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<int>)g.OrderBy(p => p.Data.propId).Select(p => p.TexData?.Length ?? 0).ToList());

        return new ClothingCollection
        {
            Name = stem,
            Ped = ped,
            Dlc = SourcePaths.DlcOf(entry.Path),
            Path = entry.Path,
            Components = components,
            Props = props,
        };
    }

    private static ShopNames ReadShopNames(GameArchive archive, Func<string, int> dlcOrder, Action<string> error)
    {
        var metas = archive.Files
            .Where(e => e.NameLower.EndsWith(".meta") && ClothingSlots.Peds.Keys.Any(p => e.NameLower.StartsWith(p, StringComparison.Ordinal)))
            .Select(e => (Entry: e, Order: dlcOrder(SourcePaths.DlcOf(e.Path))))
            .ToList();

        var parsed = new ConcurrentBag<(int Order, string Path, ShopMeta Meta)>();
        Parallel.ForEach(metas, m =>
        {
            try
            {
                if (GameArchive.ReadText(m.Entry) is { } xml && ShopMeta.Parse(xml) is { } meta)
                    parsed.Add((m.Order, m.Entry.Path, meta));
            }
            catch (Exception e)
            {
                error($"{m.Entry.Path}: {e.Message}");
            }
        });

        // Later packs (dlc_patch) override earlier definitions of the same item.
        var names = new ShopNames();
        foreach (var (_, _, meta) in parsed.OrderBy(p => p.Order).ThenBy(p => p.Path, StringComparer.Ordinal))
        {
            foreach (var item in meta.Items)
                names.Add(meta.FullDlcName, item);
        }
        return names;
    }

    private sealed class ShopNames
    {
        private readonly Dictionary<(string, bool, int, int, int), ShopItem> _items = [];

        public void Add(string collection, ShopItem item) =>
            _items[(collection.ToLowerInvariant(), item.Prop, item.Slot, item.LocalDrawable, item.Texture)] = item;

        public ShopItem? Find(string collection, bool prop, int slot, int local, int texture) =>
            _items.GetValueOrDefault((collection.ToLowerInvariant(), prop, slot, local, texture));
    }
}
