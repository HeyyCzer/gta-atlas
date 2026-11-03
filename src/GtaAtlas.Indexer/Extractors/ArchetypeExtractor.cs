using System.Text.RegularExpressions;
using CodeWalker.GameFiles;
using GtaAtlas.Data;

namespace GtaAtlas.Indexer.Extractors;

/// <summary>Props and MLO interiors from .ytyp archetype definitions.</summary>
public sealed partial class ArchetypeExtractor : Extractor
{
    /// <summary>Prop whose ytyp is always loaded, so scripts can spawn it anywhere (tagged by the indexer, see <see cref="PermanentTypes"/>).</summary>
    public const string ScriptableTag = "scriptable";

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
                };
                continue;
            }

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
                Tags = PropTags(name, def.assetType, arch is TimeArchetype, def.clipDictionary.Hash != 0, arch.BBMin, arch.BBMax),
            };
        }
    }

    /// <summary>Tags meant for filtering: behaviour and size. Technical details (ytyp, asset type) stay in the data.</summary>
    internal static List<string> PropTags(string name, rage__fwArchetypeDef__eAssetType type, bool timed, bool animated,
        SharpDX.Vector3 bbMin, SharpDX.Vector3 bbMax)
    {
        var tags = new List<string>();
        if (type == rage__fwArchetypeDef__eAssetType.ASSET_TYPE_FRAGMENT)
            tags.Add("fragment");
        if (timed)
            tags.Add("timed");
        if (animated)
            tags.Add("animated");
        if (IsLodName(name))
            tags.Add("lod");
        if (SizeTag(bbMax - bbMin) is { } size)
            tags.Add(size);
        return tags;
    }

    /// <summary>Largest bounding-box side: under 1 m small, 5 m medium, 20 m large, else huge.</summary>
    internal static string? SizeTag(SharpDX.Vector3 extent)
    {
        var side = Math.Max(extent.X, Math.Max(extent.Y, extent.Z));
        if (!float.IsFinite(side) || side <= 0)
            return null;
        return side < 1 ? "size: small" : side < 5 ? "size: medium" : side < 20 ? "size: large" : "size: huge";
    }

    /// <summary>Map LOD stand-ins: "_lod", "_slod2", "lod_" segments.</summary>
    internal static bool IsLodName(string name) => LodName().IsMatch(name);

    [GeneratedRegex(@"(^|_)s?lod\d*($|_)", RegexOptions.IgnoreCase)]
    private static partial Regex LodName();

    private static string AssetTypeTag(rage__fwArchetypeDef__eAssetType type) => type switch
    {
        rage__fwArchetypeDef__eAssetType.ASSET_TYPE_FRAGMENT => "fragment",
        rage__fwArchetypeDef__eAssetType.ASSET_TYPE_DRAWABLE => "drawable",
        rage__fwArchetypeDef__eAssetType.ASSET_TYPE_DRAWABLEDICTIONARY => "drawable-dict",
        rage__fwArchetypeDef__eAssetType.ASSET_TYPE_ASSETLESS => "assetless",
        _ => "unknown",
    };
}
