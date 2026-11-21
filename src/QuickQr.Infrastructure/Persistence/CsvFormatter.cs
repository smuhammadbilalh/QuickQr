using System.Text;

namespace QuickQr.Infrastructure.Persistence;

/// <summary>RFC 4180 CSV cell formatting with spreadsheet formula-injection protection.</summary>
internal static class CsvFormatter
{
    /// <summary>UTF-8 with a byte-order mark so Excel detects the encoding.</summary>
    public static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    /// <summary>RFC 4180 record separator.</summary>
    public const string NewLine = "\r\n";

    /// <summary>
    /// Formats one cell. Values starting with <c>= + - @ TAB CR</c> are prefixed with an apostrophe so spreadsheet
    /// applications treat them as text, not formulas (CSV injection). Cells containing a comma, quote or line break are quoted.
    /// </summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;

        return value.AsSpan().IndexOfAny(",\"\r\n") >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }

    public static async Task WriteRowAsync(TextWriter writer, IReadOnlyList<string?> cells, CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0) line.Append(',');
            line.Append(Escape(cells[i]));
        }

        line.Append(NewLine);
        await writer.WriteAsync(line, cancellationToken).ConfigureAwait(false);
    }
}
