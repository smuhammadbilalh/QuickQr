using Microsoft.Extensions.Logging;
using QuickQr.Core.Licensing;
using QuickQr.Services;

namespace QuickQr.Features.Premium;

/// <summary>
/// The single paywall entry point. <see cref="EnsureAsync"/> opens the paywall and waits until it closes
/// (signalled by <see cref="PaywallViewModel"/>), then reports whether the feature is now unlocked.
/// All members are called on the UI thread.
/// </summary>
public sealed class PremiumGate(
    IEntitlementService entitlements,
    INavigationService navigation,
    IDialogService dialogs,
    ILogger<PremiumGate> logger) : IPremiumGate
{
    private TaskCompletionSource? _paywallClosed;
    private bool _paywallOpen;

    public bool IsUnlocked(PremiumFeature feature) => entitlements.HasFeature(feature);

    public async Task<bool> EnsureAsync(PremiumFeature feature)
    {
        if (IsUnlocked(feature)) return true;

        var info = FeatureCatalog.Get(feature);
        if (info.Availability == FeatureAvailability.ComingSoon)
        {
            await dialogs.AlertAsync(info.Name, $"{info.Name} is coming soon. We'll let you know when it's ready.");
            return false;
        }

        var closed = _paywallClosed ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_paywallOpen)
            await OpenPaywallAsync(feature);

        await closed.Task;
        return IsUnlocked(feature);
    }

    public async Task ShowPaywallAsync(PremiumFeature? highlight = null)
    {
        if (_paywallOpen) return;
        await OpenPaywallAsync(highlight);
    }

    /// <summary>Called by the paywall when it appears (also when it was opened by plain navigation).</summary>
    internal void OnPaywallOpened() => _paywallOpen = true;

    /// <summary>Called by the paywall once it has been dismissed; releases every pending <see cref="EnsureAsync"/>.</summary>
    internal void OnPaywallClosed()
    {
        _paywallOpen = false;
        var pending = _paywallClosed;
        _paywallClosed = null;
        pending?.TrySetResult();
    }

    private async Task OpenPaywallAsync(PremiumFeature? highlight)
    {
        _paywallOpen = true;
        try
        {
            var parameters = highlight is { } feature
                ? new Dictionary<string, object> { [Routes.FeatureParameter] = feature.ToString() }
                : null;
            await navigation.GoToAsync(Routes.Paywall, parameters);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not open the paywall");
            OnPaywallClosed();
        }
    }
}
