using QuickQr.Core.Rendering;

namespace QuickQr.Core.Licensing;

/// <summary>Definition of the paid plans: which tier unlocks which feature. Single source of truth for paywalls.</summary>
public static class FeatureCatalog
{
    /// <summary>History entries kept on the free tier (oldest are pruned first, favourites are kept).</summary>
    public const int FreeHistoryLimit = 100;

    /// <summary>Largest raster export on the free tier, in pixels.</summary>
    public const int FreeMaxExportSize = 1024;

    // Positioning (docs/research/feature-research.md): free = everything a person needs, no ads, no account,
    // unlimited local history and colours (competitors give these away). Pro = design studio, vector/print export,
    // power scanning and privacy. Business = volume work. Backend-dependent features are listed as "coming soon".
    public static IReadOnlyList<FeatureInfo> All { get; } =
    [
        new(PremiumFeature.CustomColors, "Custom colours", "Any foreground, background and eye colour, with a contrast check.", SubscriptionTier.Free, "\ue40a"),
        new(PremiumFeature.Gradients, "Gradients", "Linear and radial colour gradients across the code.", SubscriptionTier.Pro, "\ue3a3"),
        new(PremiumFeature.ModuleShapes, "Module shapes", "Rounded, dot, diamond and bar module styles.", SubscriptionTier.Pro, "\ue3c6"),
        new(PremiumFeature.EyeStyles, "Eye styles", "Custom finder-pattern frames and centres.", SubscriptionTier.Pro, "\ue8f4"),
        new(PremiumFeature.LogoEmbedding, "Logo in code", "Place your logo in the centre with automatic error correction.", SubscriptionTier.Pro, "\ue3f4"),
        new(PremiumFeature.Frames, "Frames & call-to-action", "Branded frames with text such as “SCAN ME”.", SubscriptionTier.Pro, "\ue3c2"),
        new(PremiumFeature.BrandKits, "Brand kits", "Save designs as reusable presets and apply them in one tap.", SubscriptionTier.Pro, "\ue40a"),
        new(PremiumFeature.VectorExport, "SVG export", "Infinitely scalable vector files for designers and print.", SubscriptionTier.Pro, "\ue3ae"),
        new(PremiumFeature.PdfExport, "PDF export", "Print-ready PDF at any physical size.", SubscriptionTier.Pro, "\ue415"),
        new(PremiumFeature.HighResolutionExport, "High-resolution export", "Raster exports up to 4096 px at 600 DPI.", SubscriptionTier.Pro, "\uf54b"),
        new(PremiumFeature.AdvancedFormats, "All barcode formats", "Data Matrix, Aztec, PDF417, Code 128/39/93, EAN, UPC, ITF and Codabar.", SubscriptionTier.Pro, "\ue70b"),
        new(PremiumFeature.UnlimitedHistory, "Unlimited history", "Keep every code you create or scan, searchable and offline.", SubscriptionTier.Free, "\ue8b3"),
        new(PremiumFeature.HistoryExport, "History export", "Export history to CSV or JSON.", SubscriptionTier.Pro, "\uf090"),
        new(PremiumFeature.SecureQr, "Secure QR", "Encrypt the content with a passphrase; only QuickQr users with the passphrase can read it.", SubscriptionTier.Pro, "\ue899"),
        new(PremiumFeature.AppLock, "App lock", "Protect QuickQr with fingerprint, face or device PIN.", SubscriptionTier.Pro, "\ue32a"),
        new(PremiumFeature.ContinuousScan, "Continuous scanning", "Scan many codes in one session and export them as CSV.", SubscriptionTier.Pro, "\ue43c"),
        // Google Safe Browsing forbids commercial use; Google Web Risk needs a server-side key proxy.
        new(PremiumFeature.UrlReputation, "Live link protection", "Check scanned links against a live threat database.", SubscriptionTier.Pro, "\ue32a", FeatureAvailability.ComingSoon),
        new(PremiumFeature.BatchGeneration, "Bulk generation", "Create hundreds of codes from a CSV file in one go.", SubscriptionTier.Business, "\ue8f1"),
        new(PremiumFeature.PrintSheets, "Print sheets", "Lay out codes on A4/Letter label sheets as PDF.", SubscriptionTier.Business, "\ue8ad"),
        new(PremiumFeature.DynamicCodes, "Dynamic codes", "Change where a printed code points without reprinting.", SubscriptionTier.Business, "\ue86a", FeatureAvailability.ComingSoon),
        new(PremiumFeature.ScanAnalytics, "Scan analytics", "See when, where and how often your codes are scanned.", SubscriptionTier.Business, "\ue6e1", FeatureAvailability.ComingSoon),
        new(PremiumFeature.CloudSync, "Cloud sync", "Your history and brand kits on every device.", SubscriptionTier.Business, "\uf15c", FeatureAvailability.ComingSoon),
        new(PremiumFeature.TeamWorkspace, "Team workspace", "Share brand kits and codes with your team.", SubscriptionTier.Business, "\uea21", FeatureAvailability.ComingSoon),
    ];

    private static readonly Dictionary<PremiumFeature, FeatureInfo> ByFeature = All.ToDictionary(f => f.Feature);

    public static FeatureInfo Get(PremiumFeature feature) => ByFeature[feature];

    public static SubscriptionTier RequiredTier(PremiumFeature feature) => ByFeature[feature].RequiredTier;

    public static bool IsUnlocked(PremiumFeature feature, SubscriptionTier tier)
    {
        var info = ByFeature[feature];
        return info.Availability == FeatureAvailability.Available && tier >= info.RequiredTier;
    }

    public static IEnumerable<FeatureInfo> ForTier(SubscriptionTier tier) => All.Where(f => f.RequiredTier == tier);

    /// <summary>Premium features a style uses. Empty for <see cref="QrStyle.Default"/>-like styles.</summary>
    public static IReadOnlyList<PremiumFeature> FeaturesUsedBy(QrStyle style, RenderOptions? options = null)
    {
        var used = new List<PremiumFeature>();
        var d = QrStyle.Default;

        if (!string.Equals(style.ForegroundColor, d.ForegroundColor, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(style.BackgroundColor, d.BackgroundColor, StringComparison.OrdinalIgnoreCase) ||
            style.TransparentBackground ||
            style.EyeColor is not null)
            used.Add(PremiumFeature.CustomColors);
        if (style.Gradient is not null) used.Add(PremiumFeature.Gradients);
        if (style.ModuleShape != d.ModuleShape) used.Add(PremiumFeature.ModuleShapes);
        if (style.EyeFrameShape != d.EyeFrameShape || style.EyeBallShape != d.EyeBallShape) used.Add(PremiumFeature.EyeStyles);
        if (style.Logo is not null) used.Add(PremiumFeature.LogoEmbedding);
        if (style.Frame is { Style: not FrameStyle.None }) used.Add(PremiumFeature.Frames);

        if (options is not null)
        {
            if (options.Format == ExportFormat.Svg) used.Add(PremiumFeature.VectorExport);
            if (options.Format == ExportFormat.Pdf) used.Add(PremiumFeature.PdfExport);
            if (options.SizePixels > FreeMaxExportSize) used.Add(PremiumFeature.HighResolutionExport);
        }

        return used;
    }
}
