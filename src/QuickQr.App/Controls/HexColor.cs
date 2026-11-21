using System.Globalization;

namespace QuickQr.Controls;

/// <summary>Hex colour helpers (#RGB, #RRGGBB, #AARRGGBB) and WCAG contrast maths. UI-agnostic and testable.</summary>
public static class HexColor
{
    /// <summary>Normalises user input to upper-case #RRGGBB or #AARRGGBB. Accepts a missing '#' and #RGB shorthand.</summary>
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var hex = input.Trim().TrimStart('#');
        if (hex.Length == 3)
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
        if (hex.Length is not (6 or 8)) return false;

        foreach (var c in hex)
            if (!char.IsAsciiHexDigit(c)) return false;

        normalized = "#" + hex.ToUpperInvariant();
        return true;
    }

    /// <summary>Parses #RRGGBB / #AARRGGBB into channels (0–255).</summary>
    public static bool TryParse(string? input, out byte a, out byte r, out byte g, out byte b)
    {
        a = r = g = b = 0;
        if (!TryNormalize(input, out var hex)) return false;

        var span = hex.AsSpan(1);
        var offset = 0;
        a = 255;
        if (span.Length == 8)
        {
            a = byte.Parse(span[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            offset = 2;
        }

        r = byte.Parse(span.Slice(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        g = byte.Parse(span.Slice(offset + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        b = byte.Parse(span.Slice(offset + 4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>WCAG 2.x relative luminance (0 = black, 1 = white). Null when the colour is invalid.</summary>
    public static double? RelativeLuminance(string? hex)
    {
        if (!TryParse(hex, out _, out var r, out var g, out var b)) return null;
        return (0.2126 * Channel(r)) + (0.7152 * Channel(g)) + (0.0722 * Channel(b));

        static double Channel(byte value)
        {
            var c = value / 255d;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
    }

    /// <summary>WCAG contrast ratio between two colours (1–21). Null when either colour is invalid.</summary>
    public static double? ContrastRatio(string? first, string? second)
    {
        if (RelativeLuminance(first) is not { } l1 || RelativeLuminance(second) is not { } l2) return null;
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }
}
