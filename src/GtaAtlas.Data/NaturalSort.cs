using System.Text;

namespace GtaAtlas.Data;

/// <summary>
/// Sort key that orders embedded numbers by value ("C/0/2/F" before "C/0/10/F") under plain binary comparison,
/// so SQLite can keep it in an ordinary index. Digit runs are left-padded with zeros to <see cref="Width"/>.
/// </summary>
public static class NaturalSort
{
    /// <summary>Digits of uint.MaxValue; longer runs are kept whole (and may sort out of numeric order).</summary>
    public const int Width = 10;

    public static string Key(string name)
    {
        StringBuilder? sb = null;
        for (var i = 0; i < name.Length;)
        {
            if (!char.IsAsciiDigit(name[i]))
            {
                sb?.Append(name[i]);
                i++;
                continue;
            }

            var start = i;
            while (i < name.Length && char.IsAsciiDigit(name[i]))
                i++;
            sb ??= new StringBuilder(name, 0, start, name.Length + Width * 2);
            sb.Append('0', Math.Max(0, Width - (i - start))).Append(name, start, i - start);
        }
        return sb?.ToString() ?? name;
    }
}
