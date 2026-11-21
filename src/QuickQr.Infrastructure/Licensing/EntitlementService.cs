using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuickQr.Core.Licensing;
using QuickQr.Core.Platform;

namespace QuickQr.Infrastructure.Licensing;

/// <summary>
/// Decides the current <see cref="SubscriptionTier"/> from the store's owned purchases (highest tier among
/// non-expired purchases) and caches it encrypted (<c>entitlement.v1</c>) so paid users start with their tier offline.
/// </summary>
/// <remarks>
/// Offline rule for the cached entitlement: one-time (lifetime) purchases are always honoured; a subscription is honoured
/// until it expires and afterwards for a <see cref="GracePeriod"/> counted from the last successful store validation.
/// A successful store response is always authoritative (refunds and expiries downgrade immediately).
/// </remarks>
public sealed class EntitlementService(
    IPurchaseProvider provider,
    ISecureKeyValueStore secureStore,
    TimeProvider clock,
    ILogger<EntitlementService>? logger = null) : IEntitlementService
{
    internal const string CacheKey = "entitlement.v1";
    internal static readonly TimeSpan GracePeriod = TimeSpan.FromDays(7);

    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private EntitlementCache? _state;
    private SubscriptionTier _tier = SubscriptionTier.Free;
    private bool _cacheLoaded;

    public event EventHandler? EntitlementsChanged;

    public SubscriptionTier Tier
    {
        get { lock (_gate) return _tier; }
    }

    public bool HasFeature(PremiumFeature feature) => FeatureCatalog.IsUnlocked(feature, Tier);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_cacheLoaded)
            {
                _cacheLoaded = true;
                if (await LoadCacheAsync().ConfigureAwait(false) is { } cached)
                    SetState(cached, EffectiveCachedTier(cached, clock.GetUtcNow()));
            }

            if (!provider.IsSupported)
            {
                ApplyCachedFallback();
                return;
            }

            IReadOnlyList<OwnedPurchase> owned;
            try
            {
                owned = await provider.GetOwnedPurchasesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
            {
                _logger.LogWarning(ex, "Could not validate purchases with {Store}; using the cached entitlement.", provider.Name);
                ApplyCachedFallback();
                return;
            }

            await ApplyAsync(Compute(owned, clock.GetUtcNow())).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<IReadOnlyList<StoreProduct>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        if (provider.IsSupported)
        {
            try
            {
                var products = await provider.GetProductsAsync(ProductIds.Offered, cancellationToken).ConfigureAwait(false);
                if (products.Count > 0)
                {
                    return products
                        .Where(p => ProductIds.Offered.Contains(p.ProductId))
                        .OrderBy(p => IndexOf(ProductIds.Offered, p.ProductId))
                        .ToList();
                }
            }
            catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
            {
                _logger.LogWarning(ex, "Could not load products from {Store}; showing reference prices.", provider.Name);
            }
        }

        return ProductCatalog.All;
    }

    public async Task<PurchaseOutcome> PurchaseAsync(string productId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productId) || !ProductIds.All.Contains(productId))
            return new PurchaseOutcome(PurchaseStatus.Failed, Tier, "Unknown product.");

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var target = ProductIds.TierFor(productId);
            EntitlementCache? state;
            SubscriptionTier current;
            lock (_gate)
            {
                state = _state;
                current = _tier;
            }

            // Never charge twice: a one-time purchase at this tier (or above) is already in place.
            if (current != SubscriptionTier.Free && current >= target && state?.ExpiresAt is null)
                return new PurchaseOutcome(PurchaseStatus.AlreadyOwned, current, $"You already have QuickQr {current}.");

            if (productId == ProductIds.BusinessUpgrade && current < SubscriptionTier.Pro)
                return new PurchaseOutcome(PurchaseStatus.Failed, current, "The upgrade is for QuickQr Pro owners. Choose QuickQr Business instead.");

            if (!provider.IsSupported)
                return new PurchaseOutcome(PurchaseStatus.NotSupported, current, "In-app purchases are not available on this device.");

            try
            {
                var (status, purchase, message) = await provider.PurchaseAsync(productId, cancellationToken).ConfigureAwait(false);
                if (status is PurchaseStatus.Purchased or PurchaseStatus.AlreadyOwned or PurchaseStatus.Restored)
                    await ApplyPurchaseAsync(productId, purchase, cancellationToken).ConfigureAwait(false);
                return new PurchaseOutcome(status, Tier, message);
            }
            catch (Exception ex) when (IsCallerCancellation(ex, cancellationToken))
            {
                return new PurchaseOutcome(PurchaseStatus.Cancelled, Tier);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Purchase of {ProductId} failed.", productId);
                return new PurchaseOutcome(PurchaseStatus.Failed, Tier, ex.Message);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<PurchaseOutcome> RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (!provider.IsSupported)
            return new PurchaseOutcome(PurchaseStatus.NotSupported, Tier, "In-app purchases are not available on this device.");

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var owned = await provider.GetOwnedPurchasesAsync(cancellationToken).ConfigureAwait(false);
            var state = Compute(owned, clock.GetUtcNow());
            await ApplyAsync(state).ConfigureAwait(false);
            return state.Tier > SubscriptionTier.Free
                ? new PurchaseOutcome(PurchaseStatus.Restored, state.Tier)
                : new PurchaseOutcome(PurchaseStatus.Failed, state.Tier, "No purchases were found for this account.");
        }
        catch (Exception ex) when (IsCallerCancellation(ex, cancellationToken))
        {
            return new PurchaseOutcome(PurchaseStatus.Cancelled, Tier);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Restoring purchases failed.");
            return new PurchaseOutcome(PurchaseStatus.Failed, Tier, ex.Message);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>Highest tier among active purchases; the expiry is null when that tier is owned outright.</summary>
    internal static EntitlementCache Compute(IEnumerable<OwnedPurchase> owned, DateTimeOffset now)
    {
        var active = owned
            .Where(p => p.ExpiresAt is null || p.ExpiresAt > now)
            .Where(p => ProductIds.TierFor(p.ProductId) > SubscriptionTier.Free)
            .ToList();
        if (active.Count == 0) return new EntitlementCache(SubscriptionTier.Free, null, null, now);

        var tier = active.Max(p => ProductIds.TierFor(p.ProductId));
        var atTier = active.Where(p => ProductIds.TierFor(p.ProductId) == tier).ToList();
        var best = atTier.FirstOrDefault(p => p.ExpiresAt is null) ?? atTier.MaxBy(p => p.ExpiresAt)!;
        return new EntitlementCache(tier, best.ProductId, best.ExpiresAt, now);
    }

    internal static SubscriptionTier EffectiveCachedTier(EntitlementCache cached, DateTimeOffset now)
    {
        if (cached.Tier == SubscriptionTier.Free) return SubscriptionTier.Free;
        if (cached.ExpiresAt is null || cached.ExpiresAt > now) return cached.Tier;
        return now - cached.LastValidatedUtc <= GracePeriod ? cached.Tier : SubscriptionTier.Free;
    }

    private async Task ApplyPurchaseAsync(string productId, OwnedPurchase? purchase, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var owned = new List<OwnedPurchase>();
        var fetched = false;
        try
        {
            owned.AddRange(await provider.GetOwnedPurchasesAsync(cancellationToken).ConfigureAwait(false));
            fetched = true;
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Could not re-read owned purchases after buying {ProductId}.", productId);
        }

        // The store's owned list can lag behind a fresh purchase: always include it.
        var fresh = purchase ?? new OwnedPurchase(productId, string.Empty, now, null);
        if (!owned.Exists(p => p.ProductId == fresh.ProductId && (p.PurchaseToken == fresh.PurchaseToken || fresh.PurchaseToken.Length == 0)))
            owned.Add(fresh);

        var state = Compute(owned, now);
        EntitlementCache? previous;
        lock (_gate) previous = _state;
        if (!fetched && previous is not null && EffectiveCachedTier(previous, now) > state.Tier)
            state = previous;

        await ApplyAsync(state).ConfigureAwait(false);
    }

    private async Task ApplyAsync(EntitlementCache state)
    {
        SetState(state, state.Tier);
        try
        {
            await secureStore.SetAsync(CacheKey, JsonSerializer.Serialize(state, LicensingJsonContext.Default.EntitlementCache)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not cache the entitlement.");
        }
    }

    private void ApplyCachedFallback()
    {
        EntitlementCache? state;
        lock (_gate) state = _state;
        if (state is not null) SetState(state, EffectiveCachedTier(state, clock.GetUtcNow()));
    }

    private void SetState(EntitlementCache state, SubscriptionTier tier)
    {
        bool changed;
        lock (_gate)
        {
            changed = _tier != tier;
            _state = state;
            _tier = tier;
        }

        if (changed) EntitlementsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<EntitlementCache?> LoadCacheAsync()
    {
        try
        {
            var json = await secureStore.GetAsync(CacheKey).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize(json, LicensingJsonContext.Default.EntitlementCache);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The cached entitlement could not be read; starting as Free.");
            return null;
        }
    }

    private static bool IsCallerCancellation(Exception ex, CancellationToken cancellationToken) =>
        ex is OperationCanceledException && cancellationToken.IsCancellationRequested;

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i] == value) return i;
        return int.MaxValue;
    }
}
