using System.Globalization;
using Microsoft.UI;
using Windows.UI;

namespace Calendar.App.Helpers;

internal static class ColorHex
{
    /// <summary>Parses <c>#RRGGBB</c>; falls back to a neutral blue.</summary>
    public static Color Parse(string? hex)
    {
        if (hex is { Length: 7 } && hex[0] == '#' &&
            uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return Colors.SteelBlue;
    }

    public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
}
