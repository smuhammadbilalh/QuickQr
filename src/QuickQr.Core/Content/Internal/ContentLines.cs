using System.Text;

namespace QuickQr.Core.Content.Internal;

/// <summary>A parsed vCard / iCalendar content line: <c>GROUP.NAME;PARAM=VALUE:value</c>.</summary>
internal sealed record ContentLine(string Name, IReadOnlyDictionary<string, string> Parameters, IReadOnlyList<string> Types, string Value)
{
    public string? Parameter(string name) => Parameters.TryGetValue(name, out var value) ? value : null;

    public bool HasType(string type) => Types.Contains(type, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Reads and writes RFC 5545 / RFC 6350 content lines (unfolding, parameters, quoted-printable).</summary>
internal static class ContentLines
{
    /// <summary>Splits into logical lines: accepts CRLF/LF/CR, unfolds continuation lines and vCard 2.1 quoted-printable soft breaks.</summary>
    public static List<string> Unfold(string text)
    {
        var physical = PayloadText.SplitLines(text);
        var lines = new List<string>(physical.Length);
        foreach (var line in physical)
        {
            if (lines.Count > 0 && line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                lines[^1] += line[1..];
                continue;
            }
            if (lines.Count > 0 && IsQuotedPrintableContinuation(lines[^1]))
            {
                lines[^1] = lines[^1][..^1] + line;
                continue;
            }
            lines.Add(line);
        }
        return lines;
    }

    private static bool IsQuotedPrintableContinuation(string previous)
    {
        if (!previous.EndsWith('=')) return false;
        var colon = previous.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && previous[..colon].Contains("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase);
    }

    public static ContentLine? Parse(string line)
    {
        var colon = IndexOfUnquotedColon(line);
        if (colon <= 0) return null;

        var head = line[..colon];
        var value = line[(colon + 1)..];
        var segments = SplitUnquoted(head, ';');

        var name = segments[0].Trim();
        var dot = name.LastIndexOf('.');
        if (dot >= 0) name = name[(dot + 1)..]; // "item1.TEL" → "TEL"
        name = name.ToUpperInvariant();

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var types = new List<string>();
        foreach (var segment in segments.Skip(1))
        {
            var eq = segment.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                // vCard 2.1 bare parameter, e.g. "TEL;CELL:" or "NOTE;QUOTED-PRINTABLE:".
                var bare = segment.Trim();
                if (bare.Equals("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase)) parameters["ENCODING"] = bare;
                else if (bare.Length > 0) types.Add(bare);
                continue;
            }

            var key = segment[..eq].Trim();
            var raw = segment[(eq + 1)..].Trim().Trim('"');
            parameters[key] = raw;
            if (key.Equals("TYPE", StringComparison.OrdinalIgnoreCase))
                types.AddRange(raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(t => t.Trim('"')));
        }

        if (parameters.TryGetValue("ENCODING", out var encoding) &&
            encoding.Equals("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase))
        {
            value = DecodeQuotedPrintable(value, parameters.GetValueOrDefault("CHARSET"));
        }

        return new ContentLine(name, parameters, types, value);
    }

    private static int IndexOfUnquotedColon(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (line[i] == ':' && !quoted) return i;
        }
        return -1;
    }

    private static List<string> SplitUnquoted(string value, char separator)
    {
        var parts = new List<string>();
        var quoted = false;
        var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '"') quoted = !quoted;
            else if (value[i] == separator && !quoted)
            {
                parts.Add(value[start..i]);
                start = i + 1;
            }
        }
        parts.Add(value[start..]);
        return parts;
    }

    private static string DecodeQuotedPrintable(string value, string? charset)
    {
        var bytes = new List<byte>(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '=' && i + 2 < value.Length && Uri.IsHexDigit(value[i + 1]) && Uri.IsHexDigit(value[i + 2]))
            {
                bytes.Add((byte)((Uri.FromHex(value[i + 1]) << 4) | Uri.FromHex(value[i + 2])));
                i += 2;
            }
            else if (c == '=' && i == value.Length - 1)
            {
                // Trailing soft line break.
            }
            else
            {
                var encoded = Encoding.UTF8.GetBytes(c.ToString());
                bytes.AddRange(encoded);
            }
        }

        var encodingToUse = charset is not null &&
            (charset.Equals("ISO-8859-1", StringComparison.OrdinalIgnoreCase) || charset.Equals("LATIN1", StringComparison.OrdinalIgnoreCase))
            ? Encoding.Latin1
            : Encoding.UTF8;
        return encodingToUse.GetString([.. bytes]);
    }

    /// <summary>Appends a line folded at 75 octets (RFC 5545 §3.1) followed by CRLF.</summary>
    public static void AppendFolded(StringBuilder sb, string line)
    {
        var octets = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var length = rune.Utf8SequenceLength;
            if (octets + length > 75)
            {
                sb.Append("\r\n ");
                octets = 1;
            }
            sb.Append(rune.ToString());
            octets += length;
        }
        sb.Append("\r\n");
    }
}
