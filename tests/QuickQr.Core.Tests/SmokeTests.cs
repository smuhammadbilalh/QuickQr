namespace QuickQr.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void FeatureCatalog_covers_every_premium_feature()
    {
        var all = Enum.GetValues<QuickQr.Core.Licensing.PremiumFeature>();
        QuickQr.Core.Licensing.FeatureCatalog.All.Select(f => f.Feature).Should().BeEquivalentTo(all);
    }
}
