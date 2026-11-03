namespace GtaAtlas.Data;

public static class AssetKind
{
    public const string Prop = "prop";
    public const string Mlo = "mlo";
    public const string Vehicle = "vehicle";
    public const string Ped = "ped";
    public const string AnimDict = "anim_dict";
    public const string Anim = "anim";
    public const string Ymap = "ymap";
    public const string Clothing = "clothing";
}

public static class DlcNames
{
    /// <summary>Content from the base game archives (x64*.rpf, common.rpf).</summary>
    public const string Base = "base";

    /// <summary>Content patched directly in update.rpf outside any DLC folder.</summary>
    public const string Update = "update";

    /// <summary>Tag for assets from Enhanced-only packs, which Legacy ships but does not use.</summary>
    public const string EnhancedTag = "enhanced";

    /// <summary>"Expanded &amp; Enhanced" packs: mpg9ec, mpsum2_g9ec, patchday27g9ecng...</summary>
    public static bool IsEnhancedOnly(string dlc) => dlc.Contains("g9ec", StringComparison.OrdinalIgnoreCase);
}

public sealed record DlcInput(string Name, int OrderIndex, string? DeviceName = null, int? SetupOrder = null, string? Timestamp = null);

public sealed record AssetSource(string Dlc, string Path);

/// <summary>One indexed asset, already merged across every file that defines it.</summary>
public sealed record AssetInput
{
    public required string Kind { get; init; }
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required uint Hash { get; init; }
    public string? DisplayName { get; init; }

    /// <summary>DLC that introduced the asset.</summary>
    public required string Dlc { get; init; }

    /// <summary>(Kind, Key) of the parent asset, e.g. the clip dictionary of an anim.</summary>
    public (string Kind, string Key)? Parent { get; init; }

    public string? Data { get; init; }
    public IReadOnlyCollection<string> Tags { get; init; } = [];
    public IReadOnlyCollection<AssetSource> Sources { get; init; } = [];
}

public sealed record AssetSummary(long Id, string Kind, string Key, string Name, uint Hash, string? DisplayName, string Dlc)
{
    /// <summary>Filled by search results (for cards); empty elsewhere.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}

public sealed record AssetDetail(AssetSummary Asset, string? Data, IReadOnlyList<string> Tags, IReadOnlyList<AssetSource> Sources, AssetSummary? Parent);

public sealed record DlcSummary(long Id, string Name, int OrderIndex, string? Title, string? Timestamp, IReadOnlyDictionary<string, int> CountsByKind);

public sealed record SearchQuery
{
    public string? Text { get; init; }
    public string? Kind { get; init; }
    public string? Dlc { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public long? ParentId { get; init; }
    public bool FavoritesOnly { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; } = 100;
}

public sealed record SearchResult(IReadOnlyList<AssetSummary> Items, int Total);
