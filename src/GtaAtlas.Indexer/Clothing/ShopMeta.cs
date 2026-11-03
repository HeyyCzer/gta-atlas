using System.Globalization;
using System.Xml.Linq;

namespace GtaAtlas.Indexer.Clothing;

/// <summary>One sellable clothing variant from a ShopPedApparel meta (component or prop, one texture).</summary>
public sealed record ShopItem(bool Prop, int Slot, int LocalDrawable, int Texture, string? TextLabel, string? DevName, string? UniqueName);

public sealed record ShopMeta(string PedName, string FullDlcName, IReadOnlyList<ShopItem> Items)
{
    /// <summary>Parses a ShopPedApparel document. Returns null for other shop metas (weapons, vehicles, tattoos).</summary>
    public static ShopMeta? Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root;
        if (root is null || root.Name.LocalName != "ShopPedApparel")
            return null;

        var ped = root.Element("pedName")?.Value.Trim();
        var full = root.Element("fullDlcName")?.Value.Trim();
        if (string.IsNullOrEmpty(ped) || string.IsNullOrEmpty(full))
            return null;

        var items = new List<ShopItem>();
        items.AddRange(ReadItems(root.Element("pedComponents"), prop: false));
        items.AddRange(ReadItems(root.Element("pedProps"), prop: true));
        return new ShopMeta(ped, full, items);
    }

    private static IEnumerable<ShopItem> ReadItems(XElement? list, bool prop)
    {
        if (list is null)
            yield break;

        // Rockstar puts a "<!-- FEET: Black Cloth Clogs -->" comment before each item.
        string? comment = null;
        foreach (var node in list.Nodes())
        {
            if (node is XComment c)
            {
                comment = c.Value;
                continue;
            }
            if (node is not XElement { Name.LocalName: "Item" } item)
                continue;

            var slot = ClothingSlots.ParseSlot(item.Element(prop ? "eAnchorPoint" : "eCompType")?.Value, prop);
            var local = Int(item, prop ? "localPropIndex" : "localDrawableIndex");
            if (slot >= 0 && local >= 0)
            {
                yield return new ShopItem(prop, slot, local, Math.Max(0, Int(item, "textureIndex")),
                    NullIfEmpty(item.Element("textLabel")?.Value),
                    DevName(comment),
                    NullIfEmpty(item.Element("uniqueNameHash")?.Value));
            }
            comment = null;
        }
    }

    /// <summary>" FEET: Black Cloth Clogs " → "Black Cloth Clogs".</summary>
    internal static string? DevName(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return null;
        var colon = comment.IndexOf(':');
        return NullIfEmpty(colon >= 0 ? comment[(colon + 1)..] : comment);
    }

    private static int Int(XElement item, string name) =>
        int.TryParse(item.Element(name)?.Attribute("value")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : -1;

    private static string? NullIfEmpty(string? s)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) || t == "0" ? null : t;
    }
}
