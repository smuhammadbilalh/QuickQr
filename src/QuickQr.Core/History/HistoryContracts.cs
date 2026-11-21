using QuickQr.Core.Barcodes;
using QuickQr.Core.Content;

namespace QuickQr.Core.History;

public enum HistoryKind { Generated = 0, Scanned = 1 }

public enum ScanSource { None = 0, Camera = 1, Image = 2, Clipboard = 3, Batch = 4 }

public sealed record HistoryEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required HistoryKind Kind { get; init; }
    public required string Payload { get; init; }
    public required ContentType ContentType { get; init; }
    public BarcodeFormat Format { get; init; } = BarcodeFormat.QrCode;
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public bool IsFavorite { get; init; }
    public string? Notes { get; init; }
    /// <summary>Comma-separated tags.</summary>
    public string? Tags { get; init; }
    public ScanSource Source { get; init; }
    /// <summary>JSON-serialised <see cref="Rendering.QrStyle"/> (without logo bytes) for generated codes.</summary>
    public string? StyleJson { get; init; }
}

public enum HistorySort { NewestFirst = 0, OldestFirst = 1, TitleAscending = 2 }

public sealed record HistoryQuery(
    HistoryKind? Kind = null,
    string? Search = null,
    bool FavoritesOnly = false,
    ContentType? ContentType = null,
    HistorySort Sort = HistorySort.NewestFirst,
    int Skip = 0,
    int Take = 50);

/// <summary>Persistence port for history. Implemented with SQLite in infrastructure.</summary>
public interface IHistoryRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task AddAsync(HistoryEntry entry, CancellationToken cancellationToken = default);
    Task UpdateAsync(HistoryEntry entry, CancellationToken cancellationToken = default);
    Task<HistoryEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HistoryEntry>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken = default);
    Task<int> CountAsync(HistoryKind? kind = null, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteAllAsync(HistoryKind? kind = null, CancellationToken cancellationToken = default);
    /// <summary>Deletes the oldest non-favourite entries so that at most <paramref name="keep"/> remain. Returns the number deleted.</summary>
    Task<int> PruneAsync(int keep, CancellationToken cancellationToken = default);
}

public enum HistoryExportFormat { Csv = 0, Json = 1 }

public interface IHistoryExporter
{
    Task ExportAsync(IEnumerable<HistoryEntry> entries, HistoryExportFormat format, Stream output, CancellationToken cancellationToken = default);
}

/// <summary>Application service over history: applies retention rules and raises change notifications.</summary>
public interface IHistoryService
{
    event EventHandler? HistoryChanged;
    Task<HistoryEntry> RecordGeneratedAsync(string payload, QrContent content, BarcodeFormat format, string? styleJson, CancellationToken cancellationToken = default);
    Task<HistoryEntry> RecordScannedAsync(string payload, QrContent content, BarcodeFormat format, ScanSource source, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HistoryEntry>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken = default);
    Task<HistoryEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken cancellationToken = default);
    Task UpdateNotesAsync(Guid id, string? notes, string? tags, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task ClearAsync(HistoryKind? kind = null, CancellationToken cancellationToken = default);
    Task<int> CountAsync(HistoryKind? kind = null, CancellationToken cancellationToken = default);
    /// <summary>Requires <see cref="Licensing.PremiumFeature.HistoryExport"/>.</summary>
    Task<Result> ExportAsync(HistoryQuery query, HistoryExportFormat format, Stream output, CancellationToken cancellationToken = default);
}
