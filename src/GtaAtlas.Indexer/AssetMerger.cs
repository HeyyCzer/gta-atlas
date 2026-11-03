using System.Text.Json;
using System.Text.Json.Serialization;
using GtaAtlas.Data;

namespace GtaAtlas.Indexer;

public static class AssetMerger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    /// <summary>
    /// Groups definitions by (kind, key). The asset is attributed to the first DLC in load order
    /// that defines it outside an overlay (the one that introduced it); its data comes from the
    /// last one (what the game actually uses).
    /// </summary>
    public static List<AssetInput> Merge(IEnumerable<RawAsset> raws, IReadOnlyList<DlcInput> dlcs)
    {
        var order = dlcs.ToDictionary(d => d.Name, d => d.OrderIndex, StringComparer.OrdinalIgnoreCase);
        int OrderOf(RawAsset r) => order.TryGetValue(r.Dlc, out var o) ? o : int.MaxValue;

        return raws
            .GroupBy(r => (r.Kind, r.Key))
            .Select(g =>
            {
                var sorted = g.OrderBy(OrderOf).ThenBy(r => r.Path, StringComparer.Ordinal).ToList();
                var first = sorted.FirstOrDefault(r => !r.Overlay) ?? sorted[0];
                var last = sorted[^1];
                return new AssetInput
                {
                    Kind = last.Kind,
                    Key = last.Key,
                    Name = last.Name,
                    Hash = last.Hash,
                    DisplayName = sorted.LastOrDefault(r => r.DisplayName is not null)?.DisplayName,
                    Dlc = first.Dlc,
                    Parent = last.Parent,
                    Data = last.Data is null ? null : JsonSerializer.Serialize(last.Data, JsonOptions),
                    Tags = last.Tags,
                    Sources = sorted.Select(r => new AssetSource(r.Dlc, r.Path)).DistinctBy(s => s.Path).ToList(),
                };
            })
            .ToList();
    }
}
