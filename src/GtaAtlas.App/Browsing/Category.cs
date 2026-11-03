using GtaAtlas.Data;
using GtaAtlas.Indexer.Extractors;

namespace GtaAtlas.App.Browsing;

/// <summary>A sidebar entry: a fixed slice of the index (kind and/or tags) the user browses and searches within.</summary>
public sealed record Category(string Id, string Title, string? Kind, IReadOnlyList<string> Tags)
{
    public const string DlcsId = "dlcs";

    /// <summary>The DLC catalog is a table of packs, not an asset list.</summary>
    public bool IsDlcList => Id == DlcsId;

    /// <summary>Clothing is split by gender; the filter is pointless elsewhere.</summary>
    public bool HasGender => Kind == AssetKind.Clothing;

    public static Category All { get; } = new("all", "All", null, []);
    public static Category Props { get; } = new("props", "Props", AssetKind.Prop, []);
    public static Category Vehicles { get; } = new("vehicles", "Vehicles", AssetKind.Vehicle, []);
    public static Category Peds { get; } = new("peds", "Peds", AssetKind.Ped, []);
    public static Category Clothes { get; } = new("clothes", "Clothes", AssetKind.Clothing, []);
    public static Category Animations { get; } = new("anims", "Animations", AssetKind.Anim, []);
    public static Category AnimDicts { get; } = new("animdicts", "Anim dicts", AssetKind.AnimDict, []);
    public static Category Mlos { get; } = new("mlos", "MLOs", AssetKind.Mlo, []);
    public static Category Ipls { get; } = new("ipls", "IPLs", AssetKind.Ymap, [YmapExtractor.IplTag]);
    public static Category Ymaps { get; } = new("ymaps", "YMAPs", AssetKind.Ymap, []);
    public static Category Dlcs { get; } = new(DlcsId, "DLCs", null, []);

    public static IReadOnlyList<Category> Sidebar { get; } =
        [Props, Vehicles, Peds, Clothes, Animations, AnimDicts, Mlos, Ipls, Ymaps, All, Dlcs];
}

public enum Gender
{
    Any,
    Male,
    Female,
}
