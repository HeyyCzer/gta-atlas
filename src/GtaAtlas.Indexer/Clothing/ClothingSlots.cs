namespace GtaAtlas.Indexer.Clothing;

public static class ClothingSlots
{
    /// <summary>Freemode peds whose clothing is indexed, with their gender letter.</summary>
    public static readonly IReadOnlyDictionary<string, char> Peds = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase)
    {
        ["mp_m_freemode_01"] = 'M',
        ["mp_f_freemode_01"] = 'F',
    };

    public static readonly string[] ComponentCodes =
        ["HEAD", "BERD", "HAIR", "UPPR", "LOWR", "HAND", "FEET", "TEEF", "ACCS", "TASK", "DECL", "JBIB"];

    public static readonly string[] ComponentNames =
        ["Head", "Masks", "Hair", "Torsos", "Legs", "Bags", "Shoes", "Accessories", "Undershirts", "Body Armor", "Decals", "Tops"];

    public static readonly string[] AnchorCodes =
        ["HEAD", "EYES", "EARS", "MOUTH", "LEFT_HAND", "RIGHT_HAND", "LEFT_WRIST", "RIGHT_WRIST", "HIP", "LEFT_FOOT", "RIGHT_FOOT", "PH_L_HAND", "PH_R_HAND"];

    public static readonly string[] AnchorNames =
        ["Hats", "Glasses", "Ears", "Mouth", "Left Hand", "Right Hand", "Watches", "Bracelets", "Hip", "Left Foot", "Right Foot", "PH Left Hand", "PH Right Hand"];

    public static string SlotName(bool prop, int slot)
    {
        var names = prop ? AnchorNames : ComponentNames;
        return slot >= 0 && slot < names.Length ? names[slot] : $"Slot {slot}";
    }

    /// <summary>"PV_COMP_FEET" → 6; "ANCHOR_RIGHT_WRIST" → 7; unknown → -1.</summary>
    public static int ParseSlot(string? value, bool prop)
    {
        if (value is null)
            return -1;
        var (prefix, codes) = prop ? ("ANCHOR_", AnchorCodes) : ("PV_COMP_", ComponentCodes);
        var code = value.Trim().StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? value.Trim()[prefix.Length..] : value.Trim();
        return Array.FindIndex(codes, c => c.Equals(code, StringComparison.OrdinalIgnoreCase));
    }
}
