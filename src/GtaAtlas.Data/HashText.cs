using System.Globalization;

namespace GtaAtlas.Data;

/// <summary>The three ways a joaat hash is written: unsigned, signed int32, hex.</summary>
public static class HashText
{
    public static string ToSigned(uint hash) => unchecked((int)hash).ToString(CultureInfo.InvariantCulture);

    public static string ToHex(uint hash) => "0x" + hash.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>Parses "3316510914", "-978456382" or "0xC5ADF0C2" (also without 0x when it has hex letters).</summary>
    public static bool TryParse(string? text, out uint hash)
    {
        hash = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var s = text.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hash);

        if (s.StartsWith('-'))
        {
            if (!int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var signed))
                return false;
            hash = unchecked((uint)signed);
            return true;
        }

        if (uint.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out hash))
            return true;

        // Bare hex like "C5ADF0C2": require exactly 8 digits so normal words are not read as hashes.
        return s.Length == 8 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hash);
    }
}
