using System.Drawing;
using System.Globalization;

namespace TextRenderer.Service;

/// <summary>Parses <c>#RRGGBB</c> colours. Hex digits may be either case; the <c>#</c> and six digits are required.</summary>
public static class ColourParser
{
    public static bool TryParse(string? value, out Color colour)
    {
        colour = default;
        if (value is not { Length: 7 } || value[0] != '#')
            return false;

        if (!int.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
            return false;

        colour = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        return true;
    }
}
