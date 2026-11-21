using QuickQr.Core.Licensing;
using QuickQr.Core.Rendering;

namespace QuickQr.Core.Tests.Licensing;

public class FeatureCatalogTests
{
    private static readonly IReadOnlyDictionary<string, (QrStyle Style, PremiumFeature Feature)> StyleCases =
        new Dictionary<string, (QrStyle, PremiumFeature)>
        {
            ["foreground"] = (QrStyle.Default with { ForegroundColor = "#123456" }, PremiumFeature.CustomColors),
            ["background"] = (QrStyle.Default with { BackgroundColor = "#FFEEDD" }, PremiumFeature.CustomColors),
            ["transparent"] = (QrStyle.Default with { TransparentBackground = true }, PremiumFeature.CustomColors),
            ["eye-colour"] = (QrStyle.Default with { EyeColor = "#FF0000" }, PremiumFeature.CustomColors),
            ["gradient"] = (QrStyle.Default with { Gradient = new GradientStyle(GradientType.Linear, "#000000", "#0000FF") }, PremiumFeature.Gradients),
            ["module-shape"] = (QrStyle.Default with { ModuleShape = ModuleShape.Circle }, PremiumFeature.ModuleShapes),
            ["eye-frame"] = (QrStyle.Default with { EyeFrameShape = EyeFrameShape.Rounded }, PremiumFeature.EyeStyles),
            ["eye-ball"] = (QrStyle.Default with { EyeBallShape = EyeBallShape.Diamond }, PremiumFeature.EyeStyles),
            ["logo"] = (QrStyle.Default with { Logo = new LogoOptions([1, 2, 3]) }, PremiumFeature.LogoEmbedding),
            ["frame"] = (QrStyle.Default with { Frame = new FrameOptions() }, PremiumFeature.Frames),
        };

    public static TheoryData<string> StyleCaseNames => [.. StyleCases.Keys];

    [Fact]
    public void Default_style_uses_no_premium_features() =>
        FeatureCatalog.FeaturesUsedBy(QrStyle.Default).Should().BeEmpty();

    [Fact]
    public void Default_style_with_default_options_uses_no_premium_features() =>
        FeatureCatalog.FeaturesUsedBy(QrStyle.Default, new RenderOptions()).Should().BeEmpty();

    [Theory]
    [MemberData(nameof(StyleCaseNames))]
    public void Each_style_property_maps_to_its_feature(string name)
    {
        var (style, feature) = StyleCases[name];
        FeatureCatalog.FeaturesUsedBy(style).Should().Equal(feature);
    }

    [Fact]
    public void Colour_comparison_is_case_insensitive() =>
        FeatureCatalog.FeaturesUsedBy(QrStyle.Default with { ForegroundColor = "#000000", BackgroundColor = "#ffffff" }).Should().BeEmpty();

    [Fact]
    public void Frame_style_none_is_not_a_frame() =>
        FeatureCatalog.FeaturesUsedBy(QrStyle.Default with { Frame = new FrameOptions(FrameStyle.None) }).Should().BeEmpty();

    [Fact]
    public void Quiet_zone_and_text_options_are_free() =>
        FeatureCatalog.FeaturesUsedBy(QrStyle.Default with { QuietZoneModules = 8, ShowHumanReadableText = false }).Should().BeEmpty();

    [Theory]
    [InlineData(ExportFormat.Png, 1024, null)]
    [InlineData(ExportFormat.Jpeg, 512, null)]
    [InlineData(ExportFormat.Webp, 1024, null)]
    [InlineData(ExportFormat.Svg, 1024, PremiumFeature.VectorExport)]
    [InlineData(ExportFormat.Pdf, 1024, PremiumFeature.PdfExport)]
    [InlineData(ExportFormat.Png, 1025, PremiumFeature.HighResolutionExport)]
    [InlineData(ExportFormat.Png, 4096, PremiumFeature.HighResolutionExport)]
    public void Render_options_map_to_export_features(ExportFormat format, int size, PremiumFeature? expected)
    {
        var used = FeatureCatalog.FeaturesUsedBy(QrStyle.Default, new RenderOptions(size, format));
        if (expected is null) used.Should().BeEmpty();
        else used.Should().Equal(expected.Value);
    }

    [Fact]
    public void Everything_at_once_is_reported_in_catalog_order()
    {
        var style = QrStyle.Default with
        {
            ForegroundColor = "#111111",
            Gradient = new GradientStyle(GradientType.Radial, "#000000", "#FFFFFF"),
            ModuleShape = ModuleShape.Rounded,
            EyeFrameShape = EyeFrameShape.Circle,
            EyeBallShape = EyeBallShape.Circle,
            Logo = new LogoOptions([0]),
            Frame = new FrameOptions(FrameStyle.BannerTop),
        };

        FeatureCatalog.FeaturesUsedBy(style, new RenderOptions(2048, ExportFormat.Svg)).Should().Equal(
            PremiumFeature.CustomColors, PremiumFeature.Gradients, PremiumFeature.ModuleShapes, PremiumFeature.EyeStyles,
            PremiumFeature.LogoEmbedding, PremiumFeature.Frames, PremiumFeature.VectorExport, PremiumFeature.HighResolutionExport);
    }

    [Theory]
    [InlineData(PremiumFeature.CustomColors, SubscriptionTier.Free, true)]
    [InlineData(PremiumFeature.UnlimitedHistory, SubscriptionTier.Free, true)]
    [InlineData(PremiumFeature.Gradients, SubscriptionTier.Free, false)]
    [InlineData(PremiumFeature.Gradients, SubscriptionTier.Pro, true)]
    [InlineData(PremiumFeature.AdvancedFormats, SubscriptionTier.Free, false)]
    [InlineData(PremiumFeature.AdvancedFormats, SubscriptionTier.Pro, true)]
    [InlineData(PremiumFeature.SecureQr, SubscriptionTier.Business, true)]
    [InlineData(PremiumFeature.BatchGeneration, SubscriptionTier.Pro, false)]
    [InlineData(PremiumFeature.BatchGeneration, SubscriptionTier.Business, true)]
    [InlineData(PremiumFeature.UrlReputation, SubscriptionTier.Business, false)]
    [InlineData(PremiumFeature.DynamicCodes, SubscriptionTier.Business, false)]
    public void IsUnlocked_respects_tier_and_availability(PremiumFeature feature, SubscriptionTier tier, bool expected) =>
        FeatureCatalog.IsUnlocked(feature, tier).Should().Be(expected);

    [Fact]
    public void Every_feature_has_a_name_description_and_icon() =>
        FeatureCatalog.All.Should().OnlyContain(f => f.Name.Length > 0 && f.Description.Length > 0 && f.IconGlyph.Length == 1)
            .And.OnlyHaveUniqueItems(f => f.Feature);

    [Fact]
    public void ForTier_partitions_the_catalog() =>
        Enum.GetValues<SubscriptionTier>().SelectMany(FeatureCatalog.ForTier).Should().BeEquivalentTo(FeatureCatalog.All);

    [Theory]
    [InlineData(ProductIds.ProLifetime, SubscriptionTier.Pro, BillingPeriod.Lifetime)]
    [InlineData(ProductIds.ProMonthly, SubscriptionTier.Pro, BillingPeriod.Monthly)]
    [InlineData(ProductIds.BusinessLifetime, SubscriptionTier.Business, BillingPeriod.Lifetime)]
    [InlineData(ProductIds.BusinessUpgrade, SubscriptionTier.Business, BillingPeriod.Lifetime)]
    [InlineData(ProductIds.BusinessYearly, SubscriptionTier.Business, BillingPeriod.Yearly)]
    [InlineData("unknown", SubscriptionTier.Free, BillingPeriod.Yearly)]
    public void Product_ids_map_to_tier_and_period(string productId, SubscriptionTier tier, BillingPeriod period)
    {
        ProductIds.TierFor(productId).Should().Be(tier);
        ProductIds.PeriodFor(productId).Should().Be(period);
    }

    [Fact]
    public void Offered_products_are_known_products() => ProductIds.All.Should().Contain(ProductIds.Offered);
}
