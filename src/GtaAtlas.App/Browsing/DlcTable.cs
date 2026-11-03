using GtaAtlas.Data;

namespace GtaAtlas.App.Browsing;

public sealed record DlcColumn(string Title, Func<DlcSummary, IComparable> Key, bool Numeric);

/// <summary>Columns and ordering of the DLC catalog table. Default: newest pack (highest load order) first.</summary>
public static class DlcTable
{
    private static int Count(DlcSummary d, string kind) => d.CountsByKind.GetValueOrDefault(kind);

    public static IReadOnlyList<DlcColumn> Columns { get; } =
    [
        new("#", d => d.OrderIndex, true),
        new("Pack", d => d.Name, false),
        new("Props", d => Count(d, AssetKind.Prop), true),
        new("Vehicles", d => Count(d, AssetKind.Vehicle), true),
        new("Peds", d => Count(d, AssetKind.Ped), true),
        new("Clothes", d => Count(d, AssetKind.Clothing), true),
        new("Anims", d => Count(d, AssetKind.Anim), true),
        new("MLOs", d => Count(d, AssetKind.Mlo), true),
        new("YMAPs", d => Count(d, AssetKind.Ymap), true),
        new("Total", d => d.CountsByKind.Values.Sum(), true),
    ];

    public const int DefaultColumn = 0;
    public const bool DefaultDescending = true;

    /// <summary>Sorts by a column; ties fall back to newest first so equal counts stay in a stable, useful order.</summary>
    public static IReadOnlyList<DlcSummary> Sort(IEnumerable<DlcSummary> dlcs, int column, bool descending)
    {
        var key = Columns[column].Key;
        var sorted = descending ? dlcs.OrderByDescending(key) : dlcs.OrderBy(key);
        return sorted.ThenByDescending(d => d.OrderIndex).ToList();
    }

    /// <summary>Clicking the active column flips direction; a new column starts descending for numbers (biggest first), ascending for text.</summary>
    public static (int Column, bool Descending) Toggle(int current, bool descending, int clicked) =>
        clicked == current ? (current, !descending) : (clicked, Columns[clicked].Numeric);
}
