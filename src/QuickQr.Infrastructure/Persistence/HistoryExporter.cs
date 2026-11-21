using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuickQr.Core.History;

namespace QuickQr.Infrastructure.Persistence;

/// <summary>
/// Exports history as RFC 4180 CSV (UTF-8 with BOM, CRLF, formula-injection safe) or as indented JSON with enums as strings.
/// The output stream is left open.
/// </summary>
public sealed class HistoryExporter : IHistoryExporter
{
    internal static readonly string[] CsvHeader =
        ["id", "kind", "type", "format", "title", "subtitle", "payload", "created_at_utc", "favorite", "notes", "tags", "source"];

    private static readonly PersistenceJsonContext ExportJson = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Exports are files, never embedded in HTML: keep non-ASCII text readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    });

    public async Task ExportAsync(IEnumerable<HistoryEntry> entries, HistoryExportFormat format, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(output);

        switch (format)
        {
            case HistoryExportFormat.Csv:
                await WriteCsvAsync(entries, output, cancellationToken).ConfigureAwait(false);
                break;
            case HistoryExportFormat.Json:
                var records = entries.Select(HistoryExportRecord.From).ToList();
                await JsonSerializer.SerializeAsync(output, records, ExportJson.ListHistoryExportRecord, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported export format.");
        }
    }

    internal static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static async Task WriteCsvAsync(IEnumerable<HistoryEntry> entries, Stream output, CancellationToken cancellationToken)
    {
        var writer = new StreamWriter(output, CsvFormatter.Utf8WithBom, bufferSize: 16 * 1024, leaveOpen: true);
        await using (writer.ConfigureAwait(false))
        {
            await CsvFormatter.WriteRowAsync(writer, CsvHeader, cancellationToken).ConfigureAwait(false);
            foreach (var e in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await CsvFormatter.WriteRowAsync(writer,
                [
                    e.Id.ToString("D", CultureInfo.InvariantCulture),
                    e.Kind.ToString(),
                    e.ContentType.ToString(),
                    e.Format.ToString(),
                    e.Title,
                    e.Subtitle,
                    e.Payload,
                    FormatTimestamp(e.CreatedAt),
                    e.IsFavorite ? "true" : "false",
                    e.Notes,
                    e.Tags,
                    e.Source.ToString()
                ], cancellationToken).ConfigureAwait(false);
            }

            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>Stable JSON export shape (decoupled from the domain record; enums written as names).</summary>
internal sealed record HistoryExportRecord(
    Guid Id,
    string Kind,
    string ContentType,
    string Format,
    string Title,
    string? Subtitle,
    string Payload,
    DateTimeOffset CreatedAtUtc,
    bool IsFavorite,
    string? Notes,
    IReadOnlyList<string>? Tags,
    string Source,
    string? StyleJson)
{
    public static HistoryExportRecord From(HistoryEntry e) => new(
        e.Id,
        e.Kind.ToString(),
        e.ContentType.ToString(),
        e.Format.ToString(),
        e.Title,
        e.Subtitle,
        e.Payload,
        e.CreatedAt.ToUniversalTime(),
        e.IsFavorite,
        e.Notes,
        string.IsNullOrWhiteSpace(e.Tags)
            ? null
            : e.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        e.Source.ToString(),
        e.StyleJson);
}

[JsonSerializable(typeof(List<HistoryExportRecord>))]
internal sealed partial class PersistenceJsonContext : JsonSerializerContext;
