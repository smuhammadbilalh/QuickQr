using System.Globalization;
using System.Text;

namespace QuickQr.Core.Content.Internal;

/// <summary>String helpers shared by the payload formats: escaping, percent-encoding, field splitting.</summary>
internal static class PayloadText
{
    public static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Trimmed value, or null when the value is null or whitespace.</summary>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static bool StartsWithIgnoreCase(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    public static string NormalizeNewlines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    /// <summary>Replaces line breaks with spaces (for single-line fields).</summary>
    public static string SingleLine(string value) => NormalizeNewlines(value).Replace('\n', ' ');

    /// <summary>Splits text into lines, accepting CRLF, LF and CR.</summary>
    public static string[] SplitLines(string value) => NormalizeNewlines(value).Split('\n');

    /// <summary>Truncates to at most <paramref name="max"/> characters, ending with an ellipsis when shortened.</summary>
    public static string Truncate(string value, int max)
    {
        if (value.Length <= max) return value;
        var cut = max - 1;
        if (cut > 0 && char.IsHighSurrogate(value[cut - 1])) cut--;
        return string.Concat(value.AsSpan(0, cut).TrimEnd(), "…");
    }

    // ---- Backslash escaping (Wi-Fi, MeCard, MATMSG) ----

    public static string BackslashEscape(string value, string specials)
    {
        if (value.AsSpan().IndexOfAny(specials) < 0) return value;
        var sb = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            if (specials.Contains(c, StringComparison.Ordinal)) sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static string BackslashUnescape(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal)) return value;
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                sb.Append(value[++i]);
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Splits on <paramref name="separator"/> where it is not escaped with a backslash. Parts keep their escapes.</summary>
    public static List<string> SplitUnescaped(string value, char separator)
    {
        var parts = new List<string>();
        var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\')
            {
                i++;
                continue;
            }
            if (value[i] == separator)
            {
                parts.Add(value[start..i]);
                start = i + 1;
            }
        }
        parts.Add(value[start..]);
        return parts;
    }

    /// <summary>Parses <c>KEY:value;KEY:value;;</c> bodies (Wi-Fi, MeCard, MATMSG). Keys are upper-cased; values keep escapes.</summary>
    public static List<KeyValuePair<string, string>> ParseFields(string body)
    {
        var fields = new List<KeyValuePair<string, string>>();
        foreach (var token in SplitUnescaped(body, ';'))
        {
            var colon = token.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0) continue;
            var key = token[..colon].Trim().ToUpperInvariant();
            if (key.Length == 0) continue;
            fields.Add(new(key, token[(colon + 1)..]));
        }
        return fields;
    }

    // ---- RFC 6350 (vCard) / RFC 5545 (iCalendar) text values ----

    public static string EscapeText(string value)
    {
        var normalized = NormalizeNewlines(value);
        var sb = new StringBuilder(normalized.Length + 8);
        foreach (var c in normalized)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case ';': sb.Append(@"\;"); break;
                case ',': sb.Append(@"\,"); break;
                case '\n': sb.Append(@"\n"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    public static string UnescapeText(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal)) return value;
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                sb.Append(next is 'n' or 'N' ? '\n' : next);
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    // ---- Percent encoding (RFC 3986) ----

    public static bool IsUnreserved(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~';

    /// <summary>UTF-8 percent-encodes everything except unreserved characters and <paramref name="safe"/>.</summary>
    public static string PercentEncode(string value, string safe = "")
    {
        var sb = new StringBuilder(value.Length + 16);
        Span<byte> buffer = stackalloc byte[4];
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.IsAscii)
            {
                var c = (char)rune.Value;
                if (IsUnreserved(c) || safe.Contains(c, StringComparison.Ordinal))
                {
                    sb.Append(c);
                    continue;
                }
            }
            var written = rune.EncodeToUtf8(buffer);
            for (var i = 0; i < written; i++)
                sb.Append('%').Append(buffer[i].ToString("X2", Invariant));
        }
        return sb.ToString();
    }

    public static string PercentDecode(string value, bool plusAsSpace = false)
    {
        if (plusAsSpace) value = value.Replace('+', ' ');
        return value.Contains('%', StringComparison.Ordinal) ? Uri.UnescapeDataString(value) : value;
    }

    public static List<KeyValuePair<string, string>> ParseQuery(string query, bool plusAsSpace)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (query.StartsWith('?')) query = query[1..];
        var hash = query.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0) query = query[..hash];
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            var key = eq < 0 ? part : part[..eq];
            var value = eq < 0 ? string.Empty : part[(eq + 1)..];
            result.Add(new(PercentDecode(key, plusAsSpace).Trim(), PercentDecode(value, plusAsSpace)));
        }
        return result;
    }

    public static string? GetValue(List<KeyValuePair<string, string>> pairs, string key)
    {
        foreach (var pair in pairs)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        }
        return null;
    }

    /// <summary>Formats a decimal without exponent or trailing zeros.</summary>
    public static string FormatDecimal(decimal value) => value.ToString("0.############################", Invariant);

    public static string FormatMoney(decimal value) => value.ToString("0.00", Invariant);

    public static bool TryParseDecimal(string? value, out decimal result) =>
        decimal.TryParse(value?.Trim(), NumberStyles.Float, Invariant, out result);

    public static int DecimalPlaces(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0xFF;

    public static int DecimalPlacesNormalized(decimal value)
    {
        // 1.50m has scale 2 but only one significant decimal place.
        var normalized = value / 1.0000000000000000000000000000m;
        return DecimalPlaces(normalized);
    }
}
