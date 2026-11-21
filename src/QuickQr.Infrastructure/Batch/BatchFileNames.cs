using System.Globalization;
using System.Text;

namespace QuickQr.Infrastructure.Batch;

/// <summary>Cross-platform file-name sanitising and de-duplication for batch outputs.</summary>
internal static class BatchFileNames
{
    public const int MaxLength = 80;

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>Generated name for the 1-based item <paramref name="ordinal"/>: <c>code-0001</c>.</summary>
    public static string Generated(int ordinal) => "code-" + ordinal.ToString("D4", CultureInfo.InvariantCulture);

    /// <summary>
    /// Replaces characters that are invalid on any supported file system, trims dots/spaces, avoids Windows reserved
    /// device names and limits the length. Returns null when nothing usable remains.
    /// </summary>
    public static string? Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Trim())
        {
            builder.Append(c switch
            {
                < ' ' or '\u007F' => '_',
                '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' => '_',
                _ => c
            });
        }

        var result = TrimEdges(builder.ToString());
        if (result.Length == 0 || result.All(static c => c == '_')) return null;

        result = Truncate(result, MaxLength);

        var stem = result.Split('.')[0].TrimEnd();
        if (WindowsReservedNames.Contains(stem))
            result = Truncate("_" + result, MaxLength);

        return result.Length == 0 ? null : result;
    }

    /// <summary>Returns a name not yet in <paramref name="used"/> (case-insensitive) by appending <c>-2</c>, <c>-3</c>, ... and records it.</summary>
    public static string MakeUnique(string name, HashSet<string> used)
    {
        if (used.Add(name)) return name;

        for (var n = 2; ; n++)
        {
            var suffix = "-" + n.ToString(CultureInfo.InvariantCulture);
            var candidate = Truncate(name, MaxLength - suffix.Length) + suffix;
            if (used.Add(candidate)) return candidate;
        }
    }

    public static HashSet<string> NewNameSet() => new(StringComparer.OrdinalIgnoreCase);

    private static string TrimEdges(string value) => value.Trim().Trim('.', ' ').Trim();

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength) return value;
        var length = maxLength;
        if (char.IsHighSurrogate(value[length - 1])) length--; // never split a surrogate pair
        return TrimEdges(value[..length]);
    }
}
