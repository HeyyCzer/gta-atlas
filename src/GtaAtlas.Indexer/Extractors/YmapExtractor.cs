using CodeWalker.GameFiles;
using GtaAtlas.Data;

namespace GtaAtlas.Indexer.Extractors;

/// <summary>
/// Map placement files. Script-managed ymaps (flag bit 0) are what RequestIpl loads: tagged "ipl",
/// except LOD maps (contentFlags bit 1), which stream in with their parent.
/// </summary>
public sealed class YmapExtractor : Extractor
{
    public const string IplTag = "ipl";

    public override bool Accepts(RpfFileEntry entry) => entry.NameLower.EndsWith(".ymap");

    public override IEnumerable<RawAsset> Extract(RpfFileEntry entry, GameText text)
    {
        var ymap = GameArchive.Load<YmapFile>(entry);
        if (ymap is null)
            yield break;

        var name = FileStem(entry);
        var map = ymap.CMapData;
        var tags = new List<string>();
        if (IsIpl(map.flags, map.contentFlags))
            tags.Add(IplTag);
        if ((map.contentFlags & 2) != 0)
            tags.Add("lod");
        if ((ymap.CMloInstanceDefs?.Length ?? 0) > 0)
            tags.Add("interior");
        if ((ymap.CCarGens?.Length ?? 0) > 0)
            tags.Add("cargens");

        yield return Raw(entry, AssetKind.Ymap, name, Names.Joaat(name)) with
        {
            Data = new
            {
                parent = Names.OrNull(map.parent),
                flags = map.flags,
                contentFlags = map.contentFlags,
                entitiesMin = Vec(map.entitiesExtentsMin),
                entitiesMax = Vec(map.entitiesExtentsMax),
                streamingMin = Vec(map.streamingExtentsMin),
                streamingMax = Vec(map.streamingExtentsMax),
                entities = ymap.CEntityDefs?.Length ?? 0,
                mloInstances = ymap.CMloInstanceDefs?.Length ?? 0,
                carGens = ymap.CCarGens?.Length ?? 0,
            },
            Tags = tags,
        };
    }

    internal static bool IsIpl(uint flags, uint contentFlags) => (flags & 1) != 0 && (contentFlags & 2) == 0;
}
