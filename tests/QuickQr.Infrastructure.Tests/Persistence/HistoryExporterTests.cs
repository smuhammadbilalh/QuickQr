using System.Text;
using System.Text.Json;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Content;
using QuickQr.Core.History;
using QuickQr.Infrastructure.Persistence;

namespace QuickQr.Infrastructure.Tests.Persistence;

public sealed class HistoryExporterTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 21, 10, 30, 15, 123, TimeSpan.FromHours(2));
    private readonly HistoryExporter _exporter = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Csv_has_bom_header_crlf_and_iso_utc_timestamps()
    {
        var bytes = await ExportAsync(HistoryExportFormat.Csv, Sample());

        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var lines = text.Split("\r\n");

        lines[0].Should().Be("id,kind,type,format,title,subtitle,payload,created_at_utc,favorite,notes,tags,source");
        lines[1].Should().Be("00000000-0000-0000-0000-000000000001,Scanned,Url,QrCode,Example,example.com,https://example.com,2026-11-21T08:30:15.123Z,true,,news,Camera");
        lines[^1].Should().BeEmpty("every record ends with CRLF");
    }

    [Fact]
    public async Task Csv_quotes_delimiters_quotes_and_line_breaks()
    {
        var entry = Sample() with { Title = "Hello, \"world\"", Notes = "line1\nline2", Payload = "a\r\nb" };

        var text = await ExportCsvTextAsync(entry);

        text.Should().Contain("\"Hello, \"\"world\"\"\"");
        text.Should().Contain("\"line1\nline2\"");
        text.Should().Contain("\"a\r\nb\"");
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\")", "\"'=HYPERLINK(\"\"http://evil\"\")\"")]
    [InlineData("+1+2", "'+1+2")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tcmd", "'\tcmd")]
    [InlineData("\rcmd", "\"'\rcmd\"")]
    [InlineData("safe=text", "safe=text")]
    public void Csv_cells_are_protected_against_formula_injection(string value, string expected) =>
        CsvFormatter.Escape(value).Should().Be(expected);

    [Fact]
    public async Task Csv_guards_every_user_controlled_column()
    {
        var entry = Sample() with { Title = "=1+1", Subtitle = "+x", Payload = "-y", Notes = "@z", Tags = "=tag" };

        var row = (await ExportCsvTextAsync(entry)).Split("\r\n")[1];

        row.Should().Contain(",'=1+1,'+x,'-y,").And.Contain(",'@z,'=tag,");
    }

    [Fact]
    public async Task Json_is_indented_with_enum_names()
    {
        var bytes = await ExportAsync(HistoryExportFormat.Json, Sample(), Sample() with { Id = Guid.NewGuid(), Kind = HistoryKind.Generated, Tags = null });
        var json = Encoding.UTF8.GetString(bytes);

        json.Should().Contain("\n  {").And.Contain("\"kind\": \"Scanned\"").And.Contain("\"contentType\": \"Url\"").And.Contain("\"source\": \"Camera\"");

        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        root.GetArrayLength().Should().Be(2);
        var first = root[0];
        first.GetProperty("id").GetGuid().Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        first.GetProperty("format").GetString().Should().Be("QrCode");
        first.GetProperty("isFavorite").GetBoolean().Should().BeTrue();
        first.GetProperty("createdAtUtc").GetDateTimeOffset().Should().Be(Created);
        first.GetProperty("createdAtUtc").GetString().Should().EndWith("+00:00");
        first.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).Should().Equal("news");
        root[1].TryGetProperty("tags", out _).Should().BeFalse("null values are omitted");
        root[1].GetProperty("kind").GetString().Should().Be("Generated");
    }

    [Fact]
    public async Task Json_keeps_non_ascii_text_readable()
    {
        var json = Encoding.UTF8.GetString(await ExportAsync(HistoryExportFormat.Json, Sample() with { Title = "Café ☕" }));

        json.Should().Contain("Café ☕");
    }

    [Fact]
    public async Task Export_leaves_the_stream_open_and_handles_empty_input()
    {
        using var stream = new MemoryStream();
        await _exporter.ExportAsync([], HistoryExportFormat.Csv, stream, Ct);
        stream.CanWrite.Should().BeTrue();

        await _exporter.ExportAsync([], HistoryExportFormat.Json, stream, Ct);
        stream.CanWrite.Should().BeTrue();
    }

    private static HistoryEntry Sample() => new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
        Kind = HistoryKind.Scanned,
        Payload = "https://example.com",
        ContentType = ContentType.Url,
        Format = BarcodeFormat.QrCode,
        Title = "Example",
        Subtitle = "example.com",
        CreatedAt = Created,
        IsFavorite = true,
        Tags = "news",
        Source = ScanSource.Camera
    };

    private async Task<string> ExportCsvTextAsync(params HistoryEntry[] entries)
    {
        var bytes = await ExportAsync(HistoryExportFormat.Csv, entries);
        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
    }

    private async Task<byte[]> ExportAsync(HistoryExportFormat format, params HistoryEntry[] entries)
    {
        using var stream = new MemoryStream();
        await _exporter.ExportAsync(entries, format, stream, Ct);
        return stream.ToArray();
    }
}
