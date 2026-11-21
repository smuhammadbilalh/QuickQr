using Microsoft.Extensions.Logging.Abstractions;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Content;
using QuickQr.Core.History;
using QuickQr.Core.Licensing;
using QuickQr.Core.Scanning;
using QuickQr.Core.Security;
using QuickQr.Core.Settings;

namespace QuickQr.Core.Tests.Scanning;

public class ScanProcessingServiceTests
{
    private readonly IUrlReputationService _reputation = Substitute.For<IUrlReputationService>();
    private readonly IHistoryService _history = Substitute.For<IHistoryService>();
    private readonly IEntitlementService _entitlements = Substitute.For<IEntitlementService>();
    private readonly IAppSettingsService _settings = Substitute.For<IAppSettingsService>();
    private readonly HistoryEntry _entry = new() { Kind = HistoryKind.Scanned, Payload = "p", ContentType = ContentType.Url, Title = "t" };
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ScanProcessingServiceTests()
    {
        _settings.Current.Returns(new AppSettings());
        _history.RecordScannedAsync(Arg.Any<string>(), Arg.Any<QrContent>(), Arg.Any<BarcodeFormat>(), Arg.Any<ScanSource>(), Arg.Any<CancellationToken>())
            .Returns(_entry);
    }

    private ScanProcessingService CreateService(
        IContentParser? parser = null,
        IContentSummarizer? summarizer = null,
        IContentActionProvider? actions = null,
        IUrlSafetyAnalyzer? analyzer = null) =>
        new(parser ?? new ContentParser(), summarizer ?? new ContentSummarizer(), actions ?? new ContentActionProvider(),
            analyzer ?? new UrlSafetyAnalyzer(), _reputation, _history, _entitlements, _settings, NullLogger<ScanProcessingService>.Instance);

    [Fact]
    public async Task Url_scan_is_classified_analysed_and_recorded()
    {
        var outcome = await CreateService().ProcessAsync("http://example.com/menu", BarcodeFormat.QrCode, ScanSource.Camera, Ct);

        outcome.Content.Should().Be(new UrlContent("http://example.com/menu"));
        outcome.Summary.Title.Should().Be("example.com");
        outcome.Actions[0].Kind.Should().Be(ContentActionKind.OpenUrl);
        outcome.LinkTarget.Should().Be("http://example.com/menu");
        outcome.Safety!.Level.Should().Be(RiskLevel.Medium);
        outcome.Safety.Signals.Should().ContainSingle(s => s.Code == UrlSafetyAnalyzer.InsecureScheme);
        outcome.HistoryEntry.Should().BeSameAs(_entry);
        await _history.Received(1).RecordScannedAsync("http://example.com/menu", outcome.Content, BarcodeFormat.QrCode, ScanSource.Camera, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Store_links_are_safety_checked()
    {
        var outcome = await CreateService().ProcessAsync("market://details?id=com.whatsapp", BarcodeFormat.QrCode, ScanSource.Image, Ct);

        outcome.Content.Should().Be(new AppStoreContent(AppStoreKind.GooglePlay, "com.whatsapp"));
        outcome.LinkTarget.Should().Be("https://play.google.com/store/apps/details?id=com.whatsapp");
        outcome.Safety!.IsSafe.Should().BeTrue();
    }

    [Fact]
    public async Task Phishing_link_is_reported_high_risk()
    {
        var outcome = await CreateService().ProcessAsync("https://paypal.com.secure-login.xyz/verify", BarcodeFormat.QrCode, ScanSource.Camera, Ct);
        outcome.Safety!.Level.Should().Be(RiskLevel.High);
    }

    [Theory]
    [InlineData("Hello")]
    [InlineData("WIFI:T:WPA;S:Home;P:password1;;")]
    [InlineData("tel:+15551234567")]
    public async Task Non_link_content_has_no_safety_report(string payload)
    {
        var outcome = await CreateService().ProcessAsync(payload, BarcodeFormat.QrCode, ScanSource.Camera, Ct);
        outcome.Safety.Should().BeNull();
        outcome.LinkTarget.Should().BeNull();
    }

    [Fact]
    public async Task Orchestrates_its_collaborators()
    {
        var parser = Substitute.For<IContentParser>();
        var summarizer = Substitute.For<IContentSummarizer>();
        var actions = Substitute.For<IContentActionProvider>();
        var analyzer = Substitute.For<IUrlSafetyAnalyzer>();
        var content = new UrlContent("https://x.test");
        var summary = new ContentSummary("x", null, ContentType.Url, IconGlyphs.Url);
        var list = new[] { new ContentAction(ContentActionKind.OpenUrl, "Open", IconGlyphs.Open, "https://x.test", true) };
        var report = new UrlSafetyReport("https://x.test", RiskLevel.Low, [], "x.test");
        parser.Parse("raw").Returns(content);
        summarizer.Summarize(content).Returns(summary);
        actions.GetActions(content).Returns(list);
        analyzer.Analyze("https://x.test").Returns(report);

        var outcome = await CreateService(parser, summarizer, actions, analyzer).ProcessAsync("raw", BarcodeFormat.DataMatrix, ScanSource.Clipboard, Ct);

        outcome.Should().Be(new ScanOutcome("raw", BarcodeFormat.DataMatrix, content, summary, list, report, _entry));
    }

    [Fact]
    public async Task History_setting_off_skips_recording()
    {
        _settings.Current.Returns(new AppSettings { SaveScansToHistory = false });

        var outcome = await CreateService().ProcessAsync("Hello", BarcodeFormat.QrCode, ScanSource.Camera, Ct);

        outcome.HistoryEntry.Should().BeNull();
        await _history.DidNotReceiveWithAnyArgs().RecordScannedAsync(default!, default!, default, default, Ct);
    }

    [Fact]
    public async Task History_failure_is_swallowed()
    {
        _history.RecordScannedAsync(Arg.Any<string>(), Arg.Any<QrContent>(), Arg.Any<BarcodeFormat>(), Arg.Any<ScanSource>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HistoryEntry>(new IOException("disk full")));

        var outcome = await CreateService().ProcessAsync("Hello", BarcodeFormat.QrCode, ScanSource.Camera, Ct);

        outcome.HistoryEntry.Should().BeNull();
        outcome.Content.Should().Be(new TextContent("Hello"));
    }

    [Fact]
    public async Task History_cancellation_propagates()
    {
        _history.RecordScannedAsync(Arg.Any<string>(), Arg.Any<QrContent>(), Arg.Any<BarcodeFormat>(), Arg.Any<ScanSource>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HistoryEntry>(new OperationCanceledException()));

        var act = () => CreateService().ProcessAsync("Hello", BarcodeFormat.QrCode, ScanSource.Camera, Ct);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Null_payload_throws()
    {
        var act = () => CreateService().ProcessAsync(null!, BarcodeFormat.QrCode, ScanSource.Camera, Ct);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ---- Reputation ----

    [Fact]
    public async Task Reputation_is_skipped_without_entitlement()
    {
        _entitlements.HasFeature(PremiumFeature.UrlReputation).Returns(false);

        (await CreateService().CheckReputationAsync("https://x.test", Ct)).Should().BeNull();
        await _reputation.DidNotReceiveWithAnyArgs().CheckAsync(default!, Ct);
    }

    [Fact]
    public async Task Reputation_is_skipped_when_disabled_in_settings()
    {
        _entitlements.HasFeature(PremiumFeature.UrlReputation).Returns(true);
        _settings.Current.Returns(new AppSettings { OnlineLinkProtection = false });

        (await CreateService().CheckReputationAsync("https://x.test", Ct)).Should().BeNull();
        await _reputation.DidNotReceiveWithAnyArgs().CheckAsync(default!, Ct);
    }

    [Fact]
    public async Task Reputation_is_coming_soon_so_even_business_gets_null()
    {
        _entitlements.HasFeature(Arg.Any<PremiumFeature>()).Returns(ci => FeatureCatalog.IsUnlocked(ci.Arg<PremiumFeature>(), SubscriptionTier.Business));

        (await CreateService().CheckReputationAsync("https://x.test", Ct)).Should().BeNull();
    }

    [Fact]
    public async Task Reputation_result_is_returned_when_enabled()
    {
        var expected = new ReputationResult(ReputationVerdict.Malicious, ["SOCIAL_ENGINEERING"], "test");
        _entitlements.HasFeature(PremiumFeature.UrlReputation).Returns(true);
        _reputation.CheckAsync("https://x.test", Arg.Any<CancellationToken>()).Returns(expected);

        (await CreateService().CheckReputationAsync("https://x.test", Ct)).Should().BeSameAs(expected);
    }

    [Fact]
    public async Task Reputation_errors_become_unknown()
    {
        _entitlements.HasFeature(PremiumFeature.UrlReputation).Returns(true);
        _reputation.CheckAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<ReputationResult>(new HttpRequestException("offline")));

        var result = await CreateService().CheckReputationAsync("https://x.test", Ct);

        result!.Verdict.Should().Be(ReputationVerdict.Unknown);
        result.Provider.Should().Be("error");
        result.Threats.Should().BeEmpty();
    }

    [Fact]
    public async Task Reputation_cancellation_propagates()
    {
        _entitlements.HasFeature(PremiumFeature.UrlReputation).Returns(true);
        _reputation.CheckAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<ReputationResult>(new OperationCanceledException()));

        var act = () => CreateService().CheckReputationAsync("https://x.test", Ct);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
