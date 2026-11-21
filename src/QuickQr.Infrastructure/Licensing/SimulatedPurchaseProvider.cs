using System.Text.Json;
using QuickQr.Core.Licensing;
using QuickQr.Core.Platform;

namespace QuickQr.Infrastructure.Licensing;

/// <summary>
/// Development/CI store: sells <see cref="ProductIds.Offered"/> without real billing. Purchases are kept in
/// <see cref="IKeyValueStore"/> (<c>simstore.purchases</c>) and every call has a short simulated latency.
/// Never register this in a release build that ships to a real store.
/// </summary>
public sealed class SimulatedPurchaseProvider(IKeyValueStore store, TimeProvider clock) : IPurchaseProvider
{
    internal const string PurchasesKey = "simstore.purchases";

    private readonly Lock _gate = new();

    public string Name => "Simulated";

    public bool IsSupported => true;

    /// <summary>Simulated store round-trip time.</summary>
    internal TimeSpan Latency { get; init; } = TimeSpan.FromMilliseconds(600);

    public async Task<IReadOnlyList<StoreProduct>> GetProductsAsync(IReadOnlyList<string> productIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        await SimulateLatencyAsync(cancellationToken).ConfigureAwait(false);
        return ProductCatalog.All.Where(p => productIds.Contains(p.ProductId, StringComparer.Ordinal)).ToList();
    }

    public async Task<(PurchaseStatus Status, OwnedPurchase? Purchase, string? Message)> PurchaseAsync(string productId, CancellationToken cancellationToken = default)
    {
        await SimulateLatencyAsync(cancellationToken).ConfigureAwait(false);

        if (ProductCatalog.Find(productId) is null)
            return (PurchaseStatus.Failed, null, $"Unknown product '{productId}'.");

        lock (_gate)
        {
            var purchases = Load();
            var now = clock.GetUtcNow();
            var existing = purchases.FirstOrDefault(p => p.ProductId == productId && IsActive(p, now));
            if (existing is not null)
                return (PurchaseStatus.AlreadyOwned, ToOwned(existing), "You already own this product.");

            // Offered products are one-time purchases; ExpiresAt stays null.
            var record = new SimulatedPurchaseRecord(productId, "sim-" + Guid.NewGuid().ToString("N"), now, null);
            purchases.Add(record);
            Save(purchases);
            return (PurchaseStatus.Purchased, ToOwned(record), null);
        }
    }

    public async Task<IReadOnlyList<OwnedPurchase>> GetOwnedPurchasesAsync(CancellationToken cancellationToken = default)
    {
        await SimulateLatencyAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            return Load().Where(p => IsActive(p, now)).Select(ToOwned).ToList();
        }
    }

    /// <summary>Forgets every simulated purchase (developer tools).</summary>
    public void Reset()
    {
        lock (_gate) store.Remove(PurchasesKey);
    }

    private static bool IsActive(SimulatedPurchaseRecord purchase, DateTimeOffset now) =>
        purchase.ExpiresAt is null || purchase.ExpiresAt > now;

    private static OwnedPurchase ToOwned(SimulatedPurchaseRecord r) => new(r.ProductId, r.PurchaseToken, r.PurchasedAt, r.ExpiresAt);

    private List<SimulatedPurchaseRecord> Load()
    {
        var json = store.Get(PurchasesKey);
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize(json, LicensingJsonContext.Default.ListSimulatedPurchaseRecord) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(List<SimulatedPurchaseRecord> purchases) =>
        store.Set(PurchasesKey, JsonSerializer.Serialize(purchases, LicensingJsonContext.Default.ListSimulatedPurchaseRecord));

    private Task SimulateLatencyAsync(CancellationToken cancellationToken) =>
        Latency > TimeSpan.Zero ? Task.Delay(Latency, clock, cancellationToken) : Task.CompletedTask;
}
