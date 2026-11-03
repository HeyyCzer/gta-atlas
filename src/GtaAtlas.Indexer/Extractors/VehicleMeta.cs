using System.Globalization;
using System.Xml.Linq;

namespace GtaAtlas.Indexer.Extractors;

public sealed record VehicleMeta(
    string ModelName,
    string? TxdName,
    string? HandlingId,
    string? GameName,
    string? MakeName,
    string? Type,
    string? Class,
    string? Layout,
    string? WheelType,
    string? Swankness,
    IReadOnlyList<string> Flags,
    IReadOnlyList<string> Kits)
{
    /// <summary>Parses the InitDatas of a vehicles.meta document.</summary>
    public static IEnumerable<VehicleMeta> Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var items = doc.Root?.Element("InitDatas")?.Elements("Item") ?? [];
        foreach (var item in items)
        {
            var model = Text(item, "modelName");
            if (model is null)
                continue;

            yield return new VehicleMeta(
                model,
                Text(item, "txdName"),
                Text(item, "handlingId"),
                Text(item, "gameName"),
                Text(item, "vehicleMakeName"),
                Text(item, "type"),
                Text(item, "vehicleClass"),
                Text(item, "layout"),
                Text(item, "wheelType"),
                Text(item, "swankness"),
                Words(Text(item, "flags")),
                item.Element("kits")?.Elements("Item").Select(e => e.Value.Trim()).Where(v => v.Length > 0).ToList() ?? []);
        }
    }

    private static string? Text(XElement item, string name)
    {
        var v = item.Element(name)?.Value.Trim();
        return string.IsNullOrEmpty(v) || v.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : v;
    }

    private static List<string> Words(string? s) =>
        s?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList() ?? [];

    private static readonly Dictionary<string, string> ClassNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["VC_COMPACT"] = "Compacts",
        ["VC_SEDAN"] = "Sedans",
        ["VC_SUV"] = "SUVs",
        ["VC_COUPE"] = "Coupes",
        ["VC_MUSCLE"] = "Muscle",
        ["VC_SPORT_CLASSIC"] = "Sports Classics",
        ["VC_SPORT"] = "Sports",
        ["VC_SUPER"] = "Super",
        ["VC_MOTORCYCLE"] = "Motorcycles",
        ["VC_OFF_ROAD"] = "Off-Road",
        ["VC_INDUSTRIAL"] = "Industrial",
        ["VC_UTILITY"] = "Utility",
        ["VC_VAN"] = "Vans",
        ["VC_CYCLE"] = "Cycles",
        ["VC_BOAT"] = "Boats",
        ["VC_HELICOPTER"] = "Helicopters",
        ["VC_PLANE"] = "Planes",
        ["VC_SERVICE"] = "Service",
        ["VC_EMERGENCY"] = "Emergency",
        ["VC_MILITARY"] = "Military",
        ["VC_COMMERCIAL"] = "Commercial",
        ["VC_RAIL"] = "Trains",
        ["VC_OPEN_WHEEL"] = "Open Wheel",
    };

    /// <summary>"VC_SPORT_CLASSIC" → "Sports Classics"; unknown values are humanized.</summary>
    public static string? PrettyClass(string? vehicleClass) =>
        vehicleClass is null ? null : ClassNames.GetValueOrDefault(vehicleClass) ?? Humanize(vehicleClass, "VC_");

    /// <summary>"VEHICLE_TYPE_SUBMARINECAR" → "Submarinecar".</summary>
    public static string? PrettyType(string? type) => type is null ? null : Humanize(type, "VEHICLE_TYPE_");

    internal static string Humanize(string value, string prefix)
    {
        var s = value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? value[prefix.Length..] : value;
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.Replace('_', ' ').ToLowerInvariant());
    }
}
