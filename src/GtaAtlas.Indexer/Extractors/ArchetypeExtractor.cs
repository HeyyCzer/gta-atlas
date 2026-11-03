using CodeWalker.GameFiles;
using GtaAtlas.Data;

namespace GtaAtlas.Indexer.Extractors;

/// <summary>Props and MLO interiors from .ytyp archetype definitions.</summary>
public sealed class ArchetypeExtractor : Extractor
{
    public override bool Accepts(RpfFileEntry entry) => entry.NameLower.EndsWith(".ytyp");

    public override IEnumerable<RawAsset> Extract(RpfFileEntry entry, GameText text)
    {
        var ytyp = GameArchive.Load<YtypFile>(entry);
        if (ytyp?.AllArchetypes is null)
            yield break;

        var ytypName = FileStem(entry);
        foreach (var arch in ytyp.AllArchetypes)
        {
            var def = arch._BaseArchetypeDef;
            var name = Names.Of(def.name);
            if (name.Length == 0)
                continue;

            if (arch is MloArchetype mlo)
            {
                yield return Raw(entry, AssetKind.Mlo, name, def.name.Hash) with
                {
                    Data = new
                    {
                        ytyp = ytypName,
                        bbMin = Vec(arch.BBMin),
                        bbMax = Vec(arch.BBMax),
                        rooms = mlo.rooms?.Select(r => r.RoomName).ToArray() ?? [],
                        entitySets = mlo.entitySets?.Select(s => Names.Of(s._Data.name)).ToArray() ?? [],
                        entities = mlo.entities?.Length ?? 0,
                        portals = mlo.portals?.Length ?? 0,
                    },
                    Tags = [ytypName],
                };
                continue;
            }

            var tags = new List<string> { ytypName, AssetTypeTag(def.assetType) };
            if (arch is TimeArchetype)
                tags.Add("timed");

            yield return Raw(entry, AssetKind.Prop, name, def.name.Hash) with
            {
                Data = new
                {
                    ytyp = ytypName,
                    assetName = Names.OrNull(def.assetName),
                    assetType = AssetTypeTag(def.assetType),
                    drawableDict = Names.OrNull(def.drawableDictionary),
                    textureDict = Names.OrNull(def.textureDictionary),
                    clipDict = Names.OrNull(def.clipDictionary),
                    physicsDict = Names.OrNull(def.physicsDictionary),
                    bbMin = Vec(arch.BBMin),
                    bbMax = Vec(arch.BBMax),
                    bsRadius = Round(arch.BSRadius),
                    lodDist = Round(arch.LodDist),
                    flags = def.flags,
                    specialAttribute = def.specialAttribute,
                    timeFlags = (arch as TimeArchetype)?.TimeFlags,
                },
                Tags = tags,
            };
        }
    }

    private static string AssetTypeTag(rage__fwArchetypeDef__eAssetType type) => type switch
    {
        rage__fwArchetypeDef__eAssetType.ASSET_TYPE_FRAGMENT => "fragment",
        rage__fwArchetypeDef__eAssetType.ASSET_TYPE_DRAWABLE => "drawable",
        rage__fwArchetypeDef__eAssetType.ASSET_TYPE_DRAWABLEDICTIONARY => "drawable-dict",
        rage__fwArchetypeDef__eAssetType.ASSET_TYPE_ASSETLESS => "assetless",
        _ => "unknown",
    };
}
