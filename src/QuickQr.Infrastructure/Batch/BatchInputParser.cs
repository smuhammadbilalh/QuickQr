using System.Globalization;
using System.Text;
using QuickQr.Core.Batch;

namespace QuickQr.Infrastructure.Batch;

/// <summary>
/// Parses CSV/TSV (RFC 4180: quoted fields, doubled quotes, embedded delimiters and line breaks) or plain
/// newline-separated text into batch items. The delimiter is auto-detected unless specified.
/// </summary>
public sealed class BatchInputParser : IBatchInputParser
{
    private const int DelimiterSampleLines = 20;
    private static readonly char[] CandidateDelimiters = [',', ';', '\t'];

    public BatchParseResult Parse(string text, BatchParseOptions? options = null)
    {
        options ??= new BatchParseOptions();
        ArgumentOutOfRangeException.ThrowIfNegative(options.PayloadColumn);
        if (options.FileNameColumn is { } nameColumn) ArgumentOutOfRangeException.ThrowIfNegative(nameColumn, nameof(options.FileNameColumn));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxItems);

        var items = new List<BatchItem>();
        var warnings = new List<string>();
        if (string.IsNullOrEmpty(text)) return new BatchParseResult(items, warnings);
        if (text[0] == '﻿') text = text[1..];

        var delimiter = options.Delimiter ?? DetectDelimiter(text);
        var usedNames = BatchFileNames.NewNameSet();
        var headerPending = options.HasHeaderRow;
        var ignored = 0;

        foreach (var (fields, line) in ReadRecords(text, delimiter))
        {
            if (fields.All(string.IsNullOrWhiteSpace)) continue; // blank line

            if (headerPending)
            {
                headerPending = false;
                continue;
            }

            if (items.Count >= options.MaxItems)
            {
                ignored++;
                continue;
            }

            var payload = options.PayloadColumn < fields.Count ? fields[options.PayloadColumn].Trim() : string.Empty;
            if (payload.Length == 0)
            {
                warnings.Add($"Line {line.ToString(CultureInfo.InvariantCulture)}: empty payload, row skipped.");
                continue;
            }

            string? requested = options.FileNameColumn is { } column && column < fields.Count ? fields[column] : null;
            var name = BatchFileNames.Sanitize(requested) ?? BatchFileNames.Generated(items.Count + 1);
            items.Add(new BatchItem(payload, BatchFileNames.MakeUnique(name, usedNames)));
        }

        if (ignored > 0)
        {
            warnings.Add(
                $"Only the first {options.MaxItems.ToString(CultureInfo.InvariantCulture)} items were imported; " +
                $"{ignored.ToString(CultureInfo.InvariantCulture)} more row(s) were ignored.");
        }

        return new BatchParseResult(items, warnings);
    }

    /// <summary>
    /// Picks the candidate (<c>, ; TAB</c>) that occurs outside quotes on the most sampled lines (then most often).
    /// Returns null (single column) when no candidate appears on at least half of the sampled lines.
    /// </summary>
    internal static char? DetectDelimiter(string text)
    {
        var lineCounts = new int[CandidateDelimiters.Length];
        var totals = new int[CandidateDelimiters.Length];
        var perLine = new int[CandidateDelimiters.Length];
        var sampled = 0;
        var inQuotes = false;
        var atFieldStart = true;
        var lineHasContent = false;

        void EndLine()
        {
            if (lineHasContent)
            {
                sampled++;
                for (var i = 0; i < perLine.Length; i++)
                {
                    if (perLine[i] > 0) lineCounts[i]++;
                    totals[i] += perLine[i];
                }
            }

            Array.Clear(perLine);
            lineHasContent = false;
            atFieldStart = true;
        }

        for (var p = 0; p < text.Length && sampled < DelimiterSampleLines; p++)
        {
            var c = text[p];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (p + 1 < text.Length && text[p + 1] == '"') p++;
                    else inQuotes = false;
                }

                continue;
            }

            switch (c)
            {
                case '"' when atFieldStart:
                    inQuotes = true;
                    atFieldStart = false;
                    lineHasContent = true;
                    break;
                case '\r' or '\n':
                    EndLine();
                    break;
                default:
                    var index = Array.IndexOf(CandidateDelimiters, c);
                    if (index >= 0)
                    {
                        perLine[index]++;
                        atFieldStart = true;
                    }
                    else if (!char.IsWhiteSpace(c))
                    {
                        atFieldStart = false;
                        lineHasContent = true;
                    }

                    break;
            }
        }

        EndLine();
        if (sampled == 0) return null;

        var best = -1;
        for (var i = 0; i < CandidateDelimiters.Length; i++)
        {
            if (lineCounts[i] == 0) continue;
            if (best < 0 || lineCounts[i] > lineCounts[best] || (lineCounts[i] == lineCounts[best] && totals[i] > totals[best]))
                best = i;
        }

        return best >= 0 && lineCounts[best] * 2 >= sampled ? CandidateDelimiters[best] : null;
    }

    /// <summary>
    /// RFC 4180 reader. Quotes are only special at the start of a field (lenient: stray quotes inside unquoted fields
    /// are kept literally). Yields each record with the 1-based line number it starts on.
    /// </summary>
    internal static IEnumerable<(List<string> Fields, int Line)> ReadRecords(string text, char? delimiter)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var atFieldStart = true;
        var line = 1;
        var recordLine = 1;
        var p = 0;

        while (p < text.Length)
        {
            var c = text[p];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (p + 1 < text.Length && text[p + 1] == '"')
                    {
                        field.Append('"');
                        p += 2;
                        continue;
                    }

                    inQuotes = false;
                    p++;
                    continue;
                }

                if (c == '\n' || (c == '\r' && (p + 1 >= text.Length || text[p + 1] != '\n'))) line++;
                field.Append(c);
                p++;
                continue;
            }

            if (c == '"' && atFieldStart)
            {
                field.Clear(); // whitespace before an opening quote is not part of the value
                inQuotes = true;
                atFieldStart = false;
                p++;
                continue;
            }

            if (delimiter is { } d && c == d)
            {
                fields.Add(field.ToString());
                field.Clear();
                atFieldStart = true;
                p++;
                continue;
            }

            if (c is '\r' or '\n')
            {
                fields.Add(field.ToString());
                field.Clear();
                yield return (fields, recordLine);

                fields = [];
                atFieldStart = true;
                p += c == '\r' && p + 1 < text.Length && text[p + 1] == '\n' ? 2 : 1;
                line++;
                recordLine = line;
                continue;
            }

            field.Append(c);
            if (!char.IsWhiteSpace(c)) atFieldStart = false;
            p++;
        }

        if (field.Length > 0 || fields.Count > 0 || inQuotes)
        {
            fields.Add(field.ToString());
            yield return (fields, recordLine);
        }
    }
}
