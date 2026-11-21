using System.Text.Json.Serialization;
using QuickQr.Core.Licensing;

namespace QuickQr.Infrastructure.Licensing;

/// <summary>
/// The products sold today (<see cref="ProductIds.Offered"/>: one-time purchases only) with reference USD display prices.
/// Used by the simulated store and as the fallback when the platform store is unavailable.
/// </summary>
internal static class ProductCatalog
{
    public static IReadOnlyList<StoreProduct> All { get; } =
    [
        new(ProductIds.ProLifetime, "QuickQr Pro", "Every design, export and productivity feature. Pay once, yours forever.", "$9.99", SubscriptionTier.Pro, BillingPeriod.Lifetime, IsBestValue: true),
        new(ProductIds.BusinessLifetime, "QuickQr Business", "Everything in Pro plus bulk generation and print sheets. Pay once.", "$29.99", SubscriptionTier.Business, BillingPeriod.Lifetime),
        new(ProductIds.BusinessUpgrade, "Upgrade Pro to Business", "For Pro owners: add bulk generation and print sheets.", "$19.99", SubscriptionTier.Business, BillingPeriod.Lifetime),
    ];

    public static StoreProduct? Find(string productId) =>
        All.FirstOrDefault(p => string.Equals(p.ProductId, productId, StringComparison.Ordinal));
}

/// <summary>Cached entitlement (stored encrypted in <see cref="Core.Platform.ISecureKeyValueStore"/>).</summary>
internal sealed record EntitlementCache(
    SubscriptionTier Tier,
    string? ProductId,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset LastValidatedUtc);

/// <summary>Purchase persisted by the simulated store.</summary>
internal sealed record SimulatedPurchaseRecord(
    string ProductId,
    string PurchaseToken,
    DateTimeOffset PurchasedAt,
    DateTimeOffset? ExpiresAt);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(EntitlementCache))]
[JsonSerializable(typeof(List<SimulatedPurchaseRecord>))]
internal sealed partial class LicensingJsonContext : JsonSerializerContext;
