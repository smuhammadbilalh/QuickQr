using Microsoft.Extensions.Logging;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Content;
using QuickQr.Core.History;
using QuickQr.Core.Licensing;
using QuickQr.Core.Security;
using QuickQr.Core.Settings;

namespace QuickQr.Core.Scanning;

public sealed record ScanOutcome(
    string Payload,
    BarcodeFormat Format,
    QrContent Content,
    ContentSummary Summary,
    IReadOnlyList<ContentAction> Actions,
    UrlSafetyReport? Safety,
    HistoryEntry? HistoryEntry)
{
    /// <summary>The URL the primary action would open, if any (used for safety checks and previews).</summary>
    public string? LinkTarget => Actions.FirstOrDefault(a => a.Kind is ContentActionKind.OpenUrl or ContentActionKind.OpenStore)?.Target;
}

public interface IScanProcessingService
{
    /// <summary>Classifies the payload, analyses links, suggests actions and records history.</summary>
    Task<ScanOutcome> ProcessAsync(string payload, BarcodeFormat format, ScanSource source, CancellationToken cancellationToken = default);

    /// <summary>Online reputation check. Returns null when the feature is locked or disabled in settings.</summary>
    Task<ReputationResult?> CheckReputationAsync(string url, CancellationToken cancellationToken = default);
}

public sealed class ScanProcessingService(
    IContentParser parser,
    IContentSummarizer summarizer,
    IContentActionProvider actionProvider,
    IUrlSafetyAnalyzer safetyAnalyzer,
    IUrlReputationService reputation,
    IHistoryService history,
    IEntitlementService entitlements,
    IAppSettingsService settings,
    ILogger<ScanProcessingService> logger) : IScanProcessingService
{
    public async Task<ScanOutcome> ProcessAsync(string payload, BarcodeFormat format, ScanSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var content = parser.Parse(payload);
        var summary = summarizer.Summarize(content);
        var actions = actionProvider.GetActions(content);

        var link = actions.FirstOrDefault(a => a.Kind is ContentActionKind.OpenUrl or ContentActionKind.OpenStore)?.Target;
        var safety = link is null ? null : safetyAnalyzer.Analyze(link);

        HistoryEntry? entry = null;
        if (settings.Current.SaveScansToHistory)
        {
            try
            {
                entry = await history.RecordScannedAsync(payload, content, format, source, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to record scan in history");
            }
        }

        return new ScanOutcome(payload, format, content, summary, actions, safety, entry);
    }

    public async Task<ReputationResult?> CheckReputationAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!entitlements.HasFeature(PremiumFeature.UrlReputation) || !settings.Current.OnlineLinkProtection)
            return null;

        try
        {
            return await reputation.CheckAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Reputation check failed");
            return new ReputationResult(ReputationVerdict.Unknown, [], "error");
        }
    }
}
