using System.Globalization;
using System.Text.Json;
using GtaAtlas.Data;

namespace GtaAtlas.App.Browsing;

public sealed record DetailField(string Label, string Value);

/// <summary>How assets read on cards and in the detail panel.</summary>
public static class AssetText
{
    private static readonly Dictionary<string, string> KindTitles = new()
    {
        [AssetKind.Prop] = "Prop",
        [AssetKind.Vehicle] = "Vehicle",
        [AssetKind.Ped] = "Ped",
        [AssetKind.Clothing] = "Clothing",
        [AssetKind.Anim] = "Animation",
        [AssetKind.AnimDict] = "Anim dict",
        [AssetKind.Mlo] = "MLO",
        [AssetKind.Ymap] = "YMAP",
    };

    public static string KindTitle(string kind) => KindTitles.GetValueOrDefault(kind, kind);

    /// <summary>Second line of a card: the readable name, or for a clip the dictionary it lives in.</summary>
    public static string? Subtitle(AssetSummary a)
    {
        if (!string.IsNullOrEmpty(a.DisplayName))
            return a.DisplayName;
        if (a.Kind == AssetKind.Anim)
        {
            var slash = a.Key.LastIndexOf('/');
            return slash > 0 ? a.Key[..slash] : null;
        }
        return null;
    }

    /// <summary>What Ctrl+C copies: the name scripts use (a clip needs its dictionary too).</summary>
    public static string CopyText(AssetSummary a) =>
        a.Kind == AssetKind.Anim && Subtitle(a) is { } dict ? $"{dict} {a.Name}" : a.Name;

    /// <summary>Flattens the kind-specific JSON into label/value rows.</summary>
    public static IReadOnlyList<DetailField> DataFields(string? json)
    {
        var fields = new List<DetailField>();
        if (string.IsNullOrWhiteSpace(json))
            return fields;

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return [new DetailField("data", Format(doc.RootElement))];

        foreach (var prop in doc.RootElement.EnumerateObject())
            Flatten(prop.Name, prop.Value, fields);
        return fields;
    }

    private static void Flatten(string label, JsonElement value, List<DetailField> fields)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Null or JsonValueKind.Undefined:
                return;
            case JsonValueKind.Object:
                foreach (var prop in value.EnumerateObject())
                    Flatten($"{label}.{prop.Name}", prop.Value, fields);
                return;
            case JsonValueKind.Array when value.EnumerateArray().Any(e => e.ValueKind is JsonValueKind.Object or JsonValueKind.Array):
                var i = 0;
                foreach (var item in value.EnumerateArray())
                    fields.Add(new DetailField($"{label}[{i++}]", Format(item)));
                return;
            default:
                fields.Add(new DetailField(label, Format(value)));
                return;
        }
    }

    private static string Format(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number => value.TryGetInt64(out var l)
            ? l.ToString(CultureInfo.InvariantCulture)
            : value.GetDouble().ToString("0.###", CultureInfo.InvariantCulture),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(Format)),
        JsonValueKind.Object => string.Join(", ", value.EnumerateObject()
            .Where(p => p.Value.ValueKind != JsonValueKind.Null)
            .Select(p => $"{p.Name}: {Format(p.Value)}")),
        _ => "",
    };
}
