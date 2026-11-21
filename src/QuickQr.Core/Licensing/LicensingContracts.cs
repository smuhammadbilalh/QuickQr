namespace QuickQr.Core.Licensing;

/// <summary>Subscription plan. Ordered: a higher tier includes everything below it.</summary>
public enum SubscriptionTier { Free = 0, Pro = 1, Business = 2 }

/// <summary>Features that require a paid tier. Values are persisted — append only.</summary>
public enum PremiumFeature
{
    // Design studio (Pro)
    CustomColors = 0,
    Gradients = 1,
    ModuleShapes = 2,
    EyeStyles = 3,
    LogoEmbedding = 4,
    Frames = 5,
    BrandKits = 6,

    // Export (Pro)
    VectorExport = 10,        // SVG
    PdfExport = 11,           // print-ready PDF
    HighResolutionExport = 12, // > 1024 px
    AdvancedFormats = 13,     // Data Matrix, Aztec, PDF417 and 1D barcodes

    // Productivity (Pro)
    UnlimitedHistory = 20,
    HistoryExport = 21,       // CSV / JSON
    SecureQr = 22,            // passphrase-encrypted payloads
    AppLock = 23,             // biometric / device-credential lock
    ContinuousScan = 24,      // multi-scan session with CSV export
    UrlReputation = 25,       // online Safe Browsing lookups

    // Business
    BatchGeneration = 30,     // CSV → ZIP of codes
    PrintSheets = 31,         // label/sticker sheets as PDF
    DynamicCodes = 32,        // editable short-link codes (needs backend)
    ScanAnalytics = 33,       // scan statistics (needs backend)
    CloudSync = 34,           // cross-device history sync (needs backend)
    TeamWorkspace = 35        // shared brand kits and codes (needs backend)
}

public enum FeatureAvailability { Available = 0, ComingSoon = 1 }

/// <param name="IconGlyph">Material Icons glyph.</param>
public sealed record FeatureInfo(
    PremiumFeature Feature,
    string Name,
    string Description,
    SubscriptionTier RequiredTier,
    string IconGlyph,
    FeatureAvailability Availability = FeatureAvailability.Available);

public enum BillingPeriod { Monthly = 0, Yearly = 1, Lifetime = 2 }

public static class ProductIds
{
    public const string ProMonthly = "quickqr.pro.monthly";
    public const string ProYearly = "quickqr.pro.yearly";
    public const string ProLifetime = "quickqr.pro.lifetime";
    public const string BusinessMonthly = "quickqr.business.monthly";
    public const string BusinessYearly = "quickqr.business.yearly";
    public const string BusinessLifetime = "quickqr.business.lifetime";
    /// <summary>Discounted Pro → Business upgrade for existing Pro owners.</summary>
    public const string BusinessUpgrade = "quickqr.business.upgrade";

    public static IReadOnlyList<string> All { get; } =
        [ProMonthly, ProYearly, ProLifetime, BusinessMonthly, BusinessYearly, BusinessLifetime, BusinessUpgrade];

    /// <summary>
    /// Products sold today. One-time purchases only: everything QuickQr does runs on-device, so there is no
    /// recurring cost to justify a subscription. Subscription ids are reserved for backend features (sync, dynamic codes).
    /// </summary>
    public static IReadOnlyList<string> Offered { get; } = [ProLifetime, BusinessLifetime, BusinessUpgrade];

    public static SubscriptionTier TierFor(string productId) => productId switch
    {
        ProMonthly or ProYearly or ProLifetime => SubscriptionTier.Pro,
        BusinessMonthly or BusinessYearly or BusinessLifetime or BusinessUpgrade => SubscriptionTier.Business,
        _ => SubscriptionTier.Free
    };

    public static BillingPeriod PeriodFor(string productId) => productId switch
    {
        ProMonthly or BusinessMonthly => BillingPeriod.Monthly,
        ProLifetime or BusinessLifetime or BusinessUpgrade => BillingPeriod.Lifetime,
        _ => BillingPeriod.Yearly
    };
}

public sealed record StoreProduct(
    string ProductId,
    string Title,
    string Description,
    string LocalizedPrice,
    SubscriptionTier Tier,
    BillingPeriod Period,
    bool IsBestValue = false);

public sealed record OwnedPurchase(string ProductId, string PurchaseToken, DateTimeOffset PurchasedAt, DateTimeOffset? ExpiresAt);

public enum PurchaseStatus { Purchased = 0, Restored = 1, Cancelled = 2, AlreadyOwned = 3, Pending = 4, Failed = 5, NotSupported = 6 }

public sealed record PurchaseOutcome(PurchaseStatus Status, SubscriptionTier Tier, string? Message = null)
{
    public bool IsSuccess => Status is PurchaseStatus.Purchased or PurchaseStatus.Restored or PurchaseStatus.AlreadyOwned;
}

/// <summary>Store billing abstraction (Google Play Billing / StoreKit / Microsoft Store), implemented in the app layer.</summary>
public interface IPurchaseProvider
{
    string Name { get; }
    bool IsSupported { get; }
    Task<IReadOnlyList<StoreProduct>> GetProductsAsync(IReadOnlyList<string> productIds, CancellationToken cancellationToken = default);
    Task<(PurchaseStatus Status, OwnedPurchase? Purchase, string? Message)> PurchaseAsync(string productId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OwnedPurchase>> GetOwnedPurchasesAsync(CancellationToken cancellationToken = default);
}

/// <summary>The single authority on what the current user may use.</summary>
public interface IEntitlementService
{
    SubscriptionTier Tier { get; }

    /// <summary>True when the current tier unlocks <paramref name="feature"/> and the feature is available (not "coming soon").</summary>
    bool HasFeature(PremiumFeature feature);

    event EventHandler? EntitlementsChanged;

    /// <summary>Loads the cached entitlement and re-validates owned purchases with the store in the background.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoreProduct>> GetProductsAsync(CancellationToken cancellationToken = default);
    Task<PurchaseOutcome> PurchaseAsync(string productId, CancellationToken cancellationToken = default);
    Task<PurchaseOutcome> RestoreAsync(CancellationToken cancellationToken = default);
}
