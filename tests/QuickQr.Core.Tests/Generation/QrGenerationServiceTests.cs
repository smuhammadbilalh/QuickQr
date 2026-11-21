using Microsoft.Extensions.Logging.Abstractions;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Content;
using QuickQr.Core.Generation;
using QuickQr.Core.History;
using QuickQr.Core.Licensing;
using QuickQr.Core.Rendering;
using QuickQr.Core.Settings;

namespace QuickQr.Core.Tests.Generation;

public class QrGenerationServiceTests
{
    private readonly IBarcodeEncoder _barcodeEncoder = Substitute.For<IBarcodeEncoder>();
    private readonly IBarcodeRenderer _renderer = Substitute.For<IBarcodeRenderer>();
    private readonly IEntitlementService _entitlements = Substitute.For<IEntitlementService>();
    private readonly IHistoryService _history = Substitute.For<IHistoryService>();
    private readonly IAppSettingsService _settings = Substitute.For<IAppSettingsService>();
    private readonly RenderedImage _image = new([1, 2, 3], ExportFormat.Png, 100, 100);
    private readonly HistoryEntry _entry = new() { Kind = HistoryKind.Generated, Payload = "Hello", ContentType = ContentType.Text, Title = "Hello" };
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public QrGenerationServiceTests()
    {
        UseTier(SubscriptionTier.Free);
        _settings.Current.Returns(new AppSettings());
        _barcodeEncoder.Encode(Arg.Any<BarcodeEncodeRequest>())
            .Returns(ci => Result<BarcodeMatrix>.Success(new BarcodeMatrix(21, 21, ci.Arg<BarcodeEncodeRequest>().Format) { Payload = ci.Arg<BarcodeEncodeRequest>().Payload }));
        _renderer.RenderAsync(Arg.Any<BarcodeMatrix>(), Arg.Any<QrStyle>(), Arg.Any<RenderOptions>(), Arg.Any<CancellationToken>())
            .Returns(Result<RenderedImage>.Success(_image));
        _history.RecordGeneratedAsync(Arg.Any<string>(), Arg.Any<QrContent>(), Arg.Any<BarcodeFormat>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_entry);
    }

    private void UseTier(SubscriptionTier tier)
    {
        _entitlements.Tier.Returns(tier);
        _entitlements.HasFeature(Arg.Any<PremiumFeature>()).Returns(ci => FeatureCatalog.IsUnlocked(ci.Arg<PremiumFeature>(), tier));
    }

    private QrGenerationService CreateService(IContentEncoder? encoder = null) =>
        new(encoder ?? new ContentEncoder(), _barcodeEncoder, _renderer, _entitlements, _history, _settings, NullLogger<QrGenerationService>.Instance);

    private static readonly QrStyle PremiumStyle = QrStyle.Default with { Gradient = new GradientStyle(GradientType.Linear, "#000000", "#FF0000") };

    [Fact]
    public async Task Export_with_free_style_encodes_renders_and_records_history()
    {
        var result = await CreateService().GenerateAsync(new GenerationRequest(new TextContent("Hello")), GenerationMode.Export, Ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Payload.Should().Be("Hello");
        result.Value.Image.Should().BeSameAs(_image);
        result.Value.Matrix.Payload.Should().Be("Hello");
        result.Value.HistoryEntry.Should().BeSameAs(_entry);
        _barcodeEncoder.Received(1).Encode(new BarcodeEncodeRequest("Hello", BarcodeFormat.QrCode, ErrorCorrectionLevel.Medium));
        await _history.Received(1).RecordGeneratedAsync("Hello", Arg.Any<TextContent>(), BarcodeFormat.QrCode, Arg.Is<string?>(s => s != null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Custom_colours_are_free()
    {
        var request = new GenerationRequest(new TextContent("Hi"), Style: QrStyle.Default with { ForegroundColor = "#336699" });
        var result = await CreateService().GenerateAsync(request, GenerationMode.Export, Ct);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Preview_renders_premium_styles_without_entitlement_and_saves_nothing()
    {
        var request = new GenerationRequest(new UrlContent("example.com"), Style: PremiumStyle);

        var result = await CreateService().GenerateAsync(request, GenerationMode.Preview, Ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Payload.Should().Be("https://example.com");
        result.Value.HistoryEntry.Should().BeNull();
        await _renderer.Received(1).RenderAsync(Arg.Any<BarcodeMatrix>(), PremiumStyle, Arg.Any<RenderOptions>(), Arg.Any<CancellationToken>());
        await _history.DidNotReceiveWithAnyArgs().RecordGeneratedAsync(default!, default!, default, default, Ct);
    }

    [Fact]
    public async Task Export_with_locked_feature_fails_before_encoding()
    {
        var encoder = Substitute.For<IContentEncoder>();
        var request = new GenerationRequest(new TextContent("Hi"), Style: PremiumStyle, RenderOptions: new RenderOptions(Format: ExportFormat.Svg));

        var result = await CreateService(encoder).GenerateAsync(request, GenerationMode.Export, Ct);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.FeatureLocked);
        result.Error.Message.Should().Be("Upgrade to use: Gradients, SVG export.");
        encoder.DidNotReceiveWithAnyArgs().Encode(default!);
        _barcodeEncoder.DidNotReceiveWithAnyArgs().Encode(default!);
    }

    [Fact]
    public async Task Export_of_non_qr_format_requires_advanced_formats()
    {
        var request = new GenerationRequest(new ProductContent("4006381333931"), BarcodeFormat.Ean13);

        var result = await CreateService().GenerateAsync(request, GenerationMode.Export, Ct);

        result.Error.Code.Should().Be(ErrorCodes.FeatureLocked);
        result.Error.Message.Should().Contain(FeatureCatalog.Get(PremiumFeature.AdvancedFormats).Name);
    }

    [Fact]
    public async Task Export_of_non_qr_format_succeeds_on_pro()
    {
        UseTier(SubscriptionTier.Pro);
        var request = new GenerationRequest(new ProductContent("4006381333931"), BarcodeFormat.Ean13);

        var result = await CreateService().GenerateAsync(request, GenerationMode.Export, Ct);

        result.IsSuccess.Should().BeTrue();
        _barcodeEncoder.Received(1).Encode(Arg.Is<BarcodeEncodeRequest>(r => r.Format == BarcodeFormat.Ean13 && r.Payload == "4006381333931"));
    }

    [Fact]
    public void Locked_features_on_free_tier()
    {
        var request = new GenerationRequest(
            new SecureMessageContent("QQSEC1:abc"),
            BarcodeFormat.DataMatrix,
            Style: PremiumStyle with { BackgroundColor = "#EEEEEE" },
            RenderOptions: new RenderOptions(2048, ExportFormat.Pdf));

        CreateService().GetLockedFeatures(request).Should().Equal(
            PremiumFeature.Gradients, PremiumFeature.PdfExport, PremiumFeature.HighResolutionExport,
            PremiumFeature.AdvancedFormats, PremiumFeature.SecureQr);
    }

    [Fact]
    public void Nothing_is_locked_on_business()
    {
        UseTier(SubscriptionTier.Business);
        var request = new GenerationRequest(new SecureMessageContent("QQSEC1:abc"), BarcodeFormat.Aztec, Style: PremiumStyle, RenderOptions: new RenderOptions(4096, ExportFormat.Svg));
        CreateService().GetLockedFeatures(request).Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_content_returns_validation_error()
    {
        var result = await CreateService().GenerateAsync(new GenerationRequest(new WifiContent("", "x")), GenerationMode.Preview, Ct);

        result.Error.Code.Should().Be(ErrorCodes.Validation);
        _barcodeEncoder.DidNotReceiveWithAnyArgs().Encode(default!);
    }

    [Fact]
    public async Task Logo_forces_high_error_correction_for_qr_codes()
    {
        UseTier(SubscriptionTier.Pro);
        var request = new GenerationRequest(new TextContent("Logo"), ErrorCorrection: ErrorCorrectionLevel.Low, Style: QrStyle.Default with { Logo = new LogoOptions([1]) });

        (await CreateService().GenerateAsync(request, GenerationMode.Export, Ct)).IsSuccess.Should().BeTrue();

        _barcodeEncoder.Received(1).Encode(Arg.Is<BarcodeEncodeRequest>(r => r.ErrorCorrection == ErrorCorrectionLevel.High));
    }

    [Fact]
    public async Task Logo_does_not_change_error_correction_for_other_formats()
    {
        UseTier(SubscriptionTier.Pro);
        var request = new GenerationRequest(new TextContent("Logo"), BarcodeFormat.DataMatrix, ErrorCorrectionLevel.Low, QrStyle.Default with { Logo = new LogoOptions([1]) });

        await CreateService().GenerateAsync(request, GenerationMode.Export, Ct);

        _barcodeEncoder.Received(1).Encode(Arg.Is<BarcodeEncodeRequest>(r => r.ErrorCorrection == ErrorCorrectionLevel.Low));
    }

    [Fact]
    public async Task Barcode_encoder_failure_is_returned()
    {
        _barcodeEncoder.Encode(Arg.Any<BarcodeEncodeRequest>()).Returns(Result<BarcodeMatrix>.Failure(new Error(ErrorCodes.PayloadTooLarge, "Too much data.")));

        var result = await CreateService().GenerateAsync(new GenerationRequest(new TextContent("x")), GenerationMode.Export, Ct);

        result.Error.Should().Be(new Error(ErrorCodes.PayloadTooLarge, "Too much data."));
        await _renderer.DidNotReceiveWithAnyArgs().RenderAsync(default!, default!, default!, Ct);
    }

    [Fact]
    public async Task Renderer_failure_is_returned()
    {
        _renderer.RenderAsync(Arg.Any<BarcodeMatrix>(), Arg.Any<QrStyle>(), Arg.Any<RenderOptions>(), Arg.Any<CancellationToken>())
            .Returns(Result<RenderedImage>.Failure(Error.Unexpected("Render failed.")));

        var result = await CreateService().GenerateAsync(new GenerationRequest(new TextContent("x")), GenerationMode.Export, Ct);

        result.Error.Message.Should().Be("Render failed.");
        await _history.DidNotReceiveWithAnyArgs().RecordGeneratedAsync(default!, default!, default, default, Ct);
    }

    [Fact]
    public async Task History_failure_does_not_fail_the_export()
    {
        _history.RecordGeneratedAsync(Arg.Any<string>(), Arg.Any<QrContent>(), Arg.Any<BarcodeFormat>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HistoryEntry>(new InvalidOperationException("db locked")));

        var result = await CreateService().GenerateAsync(new GenerationRequest(new TextContent("x")), GenerationMode.Export, Ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.HistoryEntry.Should().BeNull();
    }

    [Fact]
    public async Task History_cancellation_propagates()
    {
        _history.RecordGeneratedAsync(Arg.Any<string>(), Arg.Any<QrContent>(), Arg.Any<BarcodeFormat>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HistoryEntry>(new OperationCanceledException()));

        var act = () => CreateService().GenerateAsync(new GenerationRequest(new TextContent("x")), GenerationMode.Export, Ct);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task History_setting_off_skips_recording()
    {
        _settings.Current.Returns(new AppSettings { SaveGeneratedToHistory = false });

        var result = await CreateService().GenerateAsync(new GenerationRequest(new TextContent("x")), GenerationMode.Export, Ct);

        result.Value.HistoryEntry.Should().BeNull();
        await _history.DidNotReceiveWithAnyArgs().RecordGeneratedAsync(default!, default!, default, default, Ct);
    }

    [Fact]
    public async Task Style_json_in_history_excludes_logo_bytes()
    {
        UseTier(SubscriptionTier.Pro);
        var style = QrStyle.Default with { Logo = new LogoOptions([9, 9, 9, 9]), ModuleShape = ModuleShape.Circle };

        await CreateService().GenerateAsync(new GenerationRequest(new TextContent("x"), Style: style), GenerationMode.Export, Ct);

        await _history.Received(1).RecordGeneratedAsync("x", Arg.Any<QrContent>(), BarcodeFormat.QrCode,
            Arg.Is<string?>(json => json != null && json.Contains("circle", StringComparison.OrdinalIgnoreCase) && !json.Contains("logo", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Default_render_options_are_used_when_none_given()
    {
        await CreateService().GenerateAsync(new GenerationRequest(new TextContent("x")), GenerationMode.Preview, Ct);
        await _renderer.Received(1).RenderAsync(Arg.Any<BarcodeMatrix>(), QrStyle.Default, new RenderOptions(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Null_request_throws()
    {
        var act = () => CreateService().GenerateAsync(null!, GenerationMode.Export, Ct);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
