using QuickQr.Core.Barcodes;
using QuickQr.Core.Content;
using QuickQr.Core.Licensing;

namespace QuickQr.Core.History;

public sealed class HistoryService(
    IHistoryRepository repository,
    IHistoryExporter exporter,
    IContentSummarizer summarizer,
    IEntitlementService entitlements,
    TimeProvider clock) : IHistoryService
{
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _initialized;

    public event EventHandler? HistoryChanged;

    public Task<HistoryEntry> RecordGeneratedAsync(string payload, QrContent content, BarcodeFormat format, string? styleJson, CancellationToken cancellationToken = default) =>
        RecordAsync(HistoryKind.Generated, payload, content, format, ScanSource.None, styleJson, cancellationToken);

    public Task<HistoryEntry> RecordScannedAsync(string payload, QrContent content, BarcodeFormat format, ScanSource source, CancellationToken cancellationToken = default) =>
        RecordAsync(HistoryKind.Scanned, payload, content, format, source, null, cancellationToken);

    public async Task<IReadOnlyList<HistoryEntry>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return await repository.QueryAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HistoryEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return await repository.GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken cancellationToken = default)
    {
        var entry = await GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (entry is null || entry.IsFavorite == isFavorite) return;
        await repository.UpdateAsync(entry with { IsFavorite = isFavorite }, cancellationToken).ConfigureAwait(false);
        RaiseChanged();
    }

    public async Task UpdateNotesAsync(Guid id, string? notes, string? tags, CancellationToken cancellationToken = default)
    {
        var entry = await GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (entry is null) return;
        await repository.UpdateAsync(entry with { Notes = notes, Tags = tags }, cancellationToken).ConfigureAwait(false);
        RaiseChanged();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await repository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        RaiseChanged();
    }

    public async Task ClearAsync(HistoryKind? kind = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await repository.DeleteAllAsync(kind, cancellationToken).ConfigureAwait(false);
        RaiseChanged();
    }

    public async Task<int> CountAsync(HistoryKind? kind = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return await repository.CountAsync(kind, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result> ExportAsync(HistoryQuery query, HistoryExportFormat format, Stream output, CancellationToken cancellationToken = default)
    {
        if (!entitlements.HasFeature(PremiumFeature.HistoryExport))
            return Result.Failure(Error.FeatureLocked($"Upgrade to use: {FeatureCatalog.Get(PremiumFeature.HistoryExport).Name}."));

        var entries = await QueryAsync(query with { Skip = 0, Take = int.MaxValue }, cancellationToken).ConfigureAwait(false);
        await exporter.ExportAsync(entries, format, output, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<HistoryEntry> RecordAsync(
        HistoryKind kind, string payload, QrContent content, BarcodeFormat format, ScanSource source, string? styleJson, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var summary = summarizer.Summarize(content);
        var entry = new HistoryEntry
        {
            Kind = kind,
            Payload = payload,
            ContentType = content.Type,
            Format = format,
            Title = summary.Title,
            Subtitle = summary.Subtitle,
            CreatedAt = clock.GetUtcNow(),
            Source = source,
            StyleJson = styleJson
        };

        await repository.AddAsync(entry, cancellationToken).ConfigureAwait(false);

        if (!entitlements.HasFeature(PremiumFeature.UnlimitedHistory))
            await repository.PruneAsync(FeatureCatalog.FreeHistoryLimit, cancellationToken).ConfigureAwait(false);

        RaiseChanged();
        return entry;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await _initGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            await repository.InitializeAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    private void RaiseChanged() => HistoryChanged?.Invoke(this, EventArgs.Empty);
}
