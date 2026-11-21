using Microsoft.Extensions.Logging;
using Plugin.InAppBilling;
using QuickQr.Core.Licensing;

namespace QuickQr.Services.Billing;

/// <summary>
/// Google Play Billing / StoreKit via Plugin.InAppBilling. Every offered product is a one-time, non-consumable
/// in-app purchase (see <see cref="ProductIds.Offered"/>). Purchases are acknowledged (Android) / finalized (Apple)
/// as soon as the store reports them, including ones found while restoring, so the store never refunds them.
/// Windows ships unpackaged (no Microsoft Store licence context), so the provider reports itself unsupported there.
/// </summary>
public sealed class StorePurchaseProvider(ILogger<StorePurchaseProvider> logger) : IPurchaseProvider
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string Name => DeviceInfo.Current.Platform == DevicePlatform.Android ? "Google Play" : "App Store";

    public bool IsSupported
    {
        get
        {
            var platform = DeviceInfo.Current.Platform;
            if (platform != DevicePlatform.Android && platform != DevicePlatform.iOS && platform != DevicePlatform.MacCatalyst)
                return false;

            try
            {
                return CrossInAppBilling.IsSupported;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "In-app billing is unavailable");
                return false;
            }
        }
    }

    public Task<IReadOnlyList<StoreProduct>> GetProductsAsync(IReadOnlyList<string> productIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        return WithConnectionAsync<IReadOnlyList<StoreProduct>>(async billing =>
        {
            if (productIds.Count == 0) return [];

            var products = await billing.GetProductInfoAsync(ItemType.InAppPurchase, productIds.ToArray(), cancellationToken);
            return (products ?? [])
                .Where(p => !string.IsNullOrEmpty(p.ProductId))
                .Select(p => new StoreProduct(
                    p.ProductId,
                    CleanTitle(p.Name),
                    p.Description ?? string.Empty,
                    p.LocalizedPrice ?? string.Empty,
                    ProductIds.TierFor(p.ProductId),
                    ProductIds.PeriodFor(p.ProductId),
                    IsBestValue: p.ProductId == ProductIds.ProLifetime))
                .ToList();
        }, cancellationToken);
    }

    public Task<(PurchaseStatus Status, OwnedPurchase? Purchase, string? Message)> PurchaseAsync(string productId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        return WithConnectionAsync<(PurchaseStatus, OwnedPurchase?, string?)>(async billing =>
        {
            InAppBillingPurchase? purchase;
            try
            {
                purchase = await billing.PurchaseAsync(productId, ItemType.InAppPurchase, null, null, null, cancellationToken);
            }
            catch (InAppBillingPurchaseException ex) when (ex.PurchaseError == PurchaseError.AlreadyOwned)
            {
                var owned = await ReadOwnedAsync(billing, cancellationToken);
                return (PurchaseStatus.AlreadyOwned, owned.FirstOrDefault(p => p.ProductId == productId), "You already own this.");
            }
            catch (InAppBillingPurchaseException ex)
            {
                logger.LogInformation(ex, "Purchase of {ProductId} ended with {Error}", productId, ex.PurchaseError);
                return (StatusFor(ex.PurchaseError), null, MessageFor(ex.PurchaseError));
            }

            if (purchase is null)
                return (PurchaseStatus.Failed, null, "The store did not complete the purchase.");

            switch (purchase.State)
            {
                case PurchaseState.Purchased:
                case PurchaseState.Restored:
                    await FinalizeAsync(billing, new[] { purchase }, cancellationToken);
                    return (purchase.State == PurchaseState.Restored ? PurchaseStatus.Restored : PurchaseStatus.Purchased, ToOwned(purchase, productId), null);
                case PurchaseState.Purchasing:
                case PurchaseState.Deferred:
                case PurchaseState.PaymentPending:
                    return (PurchaseStatus.Pending, null, "Your payment is pending. QuickQr unlocks as soon as the store confirms it.");
                case PurchaseState.Canceled:
                    return (PurchaseStatus.Cancelled, null, null);
                default:
                    return (PurchaseStatus.Failed, null, "The purchase could not be completed.");
            }
        }, cancellationToken);
    }

    public Task<IReadOnlyList<OwnedPurchase>> GetOwnedPurchasesAsync(CancellationToken cancellationToken = default) =>
        WithConnectionAsync(billing => ReadOwnedAsync(billing, cancellationToken), cancellationToken);

    private async Task<IReadOnlyList<OwnedPurchase>> ReadOwnedAsync(IInAppBilling billing, CancellationToken cancellationToken)
    {
        var purchases = (await billing.GetPurchasesAsync(ItemType.InAppPurchase, cancellationToken) ?? [])
            .Where(p => p.State is PurchaseState.Purchased or PurchaseState.Restored)
            .Where(p => ProductIdOf(p) is { } id && ProductIds.All.Contains(id))
            .ToList();

        await FinalizeAsync(billing, purchases, cancellationToken);
        return purchases.Select(p => ToOwned(p, ProductIdOf(p)!)).ToList();
    }

    /// <summary>Android: acknowledge within 3 days or Google refunds. Apple: finish the transaction.</summary>
    private async Task FinalizeAsync(IInAppBilling billing, IReadOnlyList<InAppBillingPurchase> purchases, CancellationToken cancellationToken)
    {
        var android = DeviceInfo.Current.Platform == DevicePlatform.Android;
        var ids = purchases
            .Where(p => !android || p.IsAcknowledged != true)
            .Select(p => android ? p.PurchaseToken : p.TransactionIdentifier ?? p.Id)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToArray();
        if (ids.Length == 0) return;

        try
        {
            var results = await billing.FinalizePurchaseAsync(ids, cancellationToken);
            foreach (var (id, success) in results ?? [])
            {
                if (!success) logger.LogWarning("The store did not finalize transaction {Transaction}", Redact(id));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Retried on the next launch (owned purchases are re-read and finalized again).
            logger.LogWarning(ex, "Finalizing {Count} purchase(s) failed", ids.Length);
        }
    }

    private async Task<T> WithConnectionAsync<T>(Func<IInAppBilling, Task<T>> operation, CancellationToken cancellationToken)
    {
        if (!IsSupported) throw new NotSupportedException("In-app purchases are not available on this device.");

        await _gate.WaitAsync(cancellationToken);
        var billing = CrossInAppBilling.Current;
        try
        {
            if (!await billing.ConnectAsync(true, cancellationToken))
                throw new InvalidOperationException("Could not connect to the store. Check your connection and try again.");

            return await operation(billing);
        }
        finally
        {
            try
            {
                await billing.DisconnectAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Store disconnect failed");
            }

            _gate.Release();
        }
    }

    private static string? ProductIdOf(InAppBillingPurchase purchase) =>
        !string.IsNullOrEmpty(purchase.ProductId) ? purchase.ProductId : purchase.ProductIds?.FirstOrDefault();

    private static OwnedPurchase ToOwned(InAppBillingPurchase purchase, string productId) =>
        new(productId,
            purchase.PurchaseToken ?? purchase.TransactionIdentifier ?? purchase.Id ?? string.Empty,
            new DateTimeOffset(DateTime.SpecifyKind(purchase.TransactionDateUtc, DateTimeKind.Utc)),
            null);

    private static PurchaseStatus StatusFor(PurchaseError error) => error switch
    {
        PurchaseError.UserCancelled => PurchaseStatus.Cancelled,
        PurchaseError.AlreadyOwned => PurchaseStatus.AlreadyOwned,
        PurchaseError.BillingUnavailable or PurchaseError.FeatureNotSupported => PurchaseStatus.NotSupported,
        _ => PurchaseStatus.Failed,
    };

    private static string? MessageFor(PurchaseError error) => error switch
    {
        PurchaseError.UserCancelled => null,
        PurchaseError.PaymentNotAllowed => "Purchases are not allowed on this device (check parental controls).",
        PurchaseError.PaymentInvalid => "The payment was declined.",
        PurchaseError.ItemUnavailable or PurchaseError.InvalidProduct => "This product is not available in your country yet.",
        PurchaseError.NetworkError or PurchaseError.ServiceTimeout or PurchaseError.ServiceDisconnected or PurchaseError.ServiceUnavailable
            => "The store could not be reached. Check your connection and try again.",
        PurchaseError.AppleTermsConditionsChanged => "Please accept the updated App Store terms in Settings and try again.",
        PurchaseError.BillingUnavailable or PurchaseError.AppStoreUnavailable => "The store is not available on this device.",
        _ => "The purchase could not be completed.",
    };

    /// <summary>Play titles look like "QuickQr Pro (QuickQr - QR Codes)": drop the app-name suffix.</summary>
    private static string CleanTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        var index = title.LastIndexOf(" (", StringComparison.Ordinal);
        return index > 0 && title.EndsWith(')') ? title[..index] : title;
    }

    private static string Redact(string id) => id.Length <= 8 ? "****" : id[..4] + "…" + id[^4..];
}
