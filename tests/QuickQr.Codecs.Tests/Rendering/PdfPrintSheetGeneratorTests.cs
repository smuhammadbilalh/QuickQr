using System.Text;
using System.Text.RegularExpressions;
using QuickQr.Codecs.Tests.TestSupport;
using QuickQr.Core;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Batch;
using QuickQr.Core.Licensing;
using QuickQr.Core.Rendering;
using QuickQr.Infrastructure.Rendering;

namespace QuickQr.Codecs.Tests.Rendering;

public partial class PdfPrintSheetGeneratorTests
{
    private readonly IEntitlementService _entitlements = Substitute.For<IEntitlementService>();
    private readonly PdfPrintSheetGenerator _generator;

    public PdfPrintSheetGeneratorTests()
    {
        _entitlements.HasFeature(PremiumFeature.PrintSheets).Returns(true);
        _generator = new PdfPrintSheetGenerator(Codec.Encoder, _entitlements);
    }

    private static List<BatchItem> Items(int count) =>
        Enumerable.Range(1, count).Select(i => new BatchItem($"https://quickqr.app/item/{i}", $"Item {i:000}")).ToList();

    [Fact]
    public async Task Free_tier_is_locked()
    {
        _entitlements.HasFeature(PremiumFeature.PrintSheets).Returns(false);
        using var output = new MemoryStream();

        var result = await _generator.GeneratePdfAsync(new PrintSheetRequest(Items(3), BarcodeFormat.QrCode, ErrorCorrectionLevel.Medium, QrStyle.Default), output, Codec.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.FeatureLocked);
        output.Length.Should().Be(0);
    }

    [Theory]
    [InlineData(PaperSize.A4, 4, 6, 30, 2)]
    [InlineData(PaperSize.Letter, 3, 8, 49, 3)]
    [InlineData(PaperSize.A4, 1, 1, 3, 3)]
    public async Task Produces_as_many_pages_as_needed(PaperSize paper, int columns, int rows, int items, int expectedPages)
    {
        using var output = new MemoryStream();
        var request = new PrintSheetRequest(Items(items), BarcodeFormat.QrCode, ErrorCorrectionLevel.Medium, QrStyle.Default, paper, columns, rows);

        var result = await _generator.GeneratePdfAsync(request, output, Codec.Token);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        var pdf = output.ToArray();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        CountPages(pdf).Should().Be(expectedPages);
    }

    [Fact]
    public async Task Page_size_matches_the_paper()
    {
        using var a4 = new MemoryStream();
        using var letter = new MemoryStream();

        await _generator.GeneratePdfAsync(new PrintSheetRequest(Items(1), BarcodeFormat.QrCode, ErrorCorrectionLevel.Low, QrStyle.Default, PaperSize.A4), a4, Codec.Token);
        await _generator.GeneratePdfAsync(new PrintSheetRequest(Items(1), BarcodeFormat.QrCode, ErrorCorrectionLevel.Low, QrStyle.Default, PaperSize.Letter), letter, Codec.Token);

        Encoding.ASCII.GetString(a4.ToArray()).Should().MatchRegex(@"/MediaBox\s*\[\s*0\s+0\s+595\s+842\s*\]");
        Encoding.ASCII.GetString(letter.ToArray()).Should().MatchRegex(@"/MediaBox\s*\[\s*0\s+0\s+612\s+792\s*\]");
    }

    [Fact]
    public async Task Items_that_cannot_be_encoded_get_a_placeholder_instead_of_failing()
    {
        using var output = new MemoryStream();
        List<BatchItem> items = [new("5901234123457", "valid"), new("not-a-number", "invalid"), new("96385074", "too short for EAN-13")];

        var result = await _generator.GeneratePdfAsync(new PrintSheetRequest(items, BarcodeFormat.Ean13, ErrorCorrectionLevel.Medium, QrStyle.Default), output, Codec.Token);

        result.IsSuccess.Should().BeTrue();
        CountPages(output.ToArray()).Should().Be(1);
    }

    [Fact]
    public async Task Styled_sheet_with_logo_frame_and_labels_is_generated()
    {
        using var output = new MemoryStream();
        var style = new QrStyle
        {
            ModuleShape = ModuleShape.Rounded,
            Gradient = new GradientStyle(GradientType.Linear, "#0D47A1", "#6A1B9A"),
            Logo = new LogoOptions(TestImages.LogoPng()),
            Frame = new FrameOptions(FrameStyle.BannerBottom, "SCAN ME")
        };
        var items = Items(5).Append(new BatchItem("https://quickqr.app/long", new string('W', 200))).ToList();

        var result = await _generator.GeneratePdfAsync(new PrintSheetRequest(items, BarcodeFormat.QrCode, ErrorCorrectionLevel.Low, style, Columns: 3, Rows: 4), output, Codec.Token);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        CountPages(output.ToArray()).Should().Be(1);
    }

    [Fact]
    public async Task Invalid_requests_are_rejected()
    {
        using var output = new MemoryStream();

        var empty = await _generator.GeneratePdfAsync(new PrintSheetRequest([], BarcodeFormat.QrCode, ErrorCorrectionLevel.Medium, QrStyle.Default), output, Codec.Token);
        var noColumns = await _generator.GeneratePdfAsync(new PrintSheetRequest(Items(1), BarcodeFormat.QrCode, ErrorCorrectionLevel.Medium, QrStyle.Default, Columns: 0), output, Codec.Token);
        var badColour = await _generator.GeneratePdfAsync(new PrintSheetRequest(Items(1), BarcodeFormat.QrCode, ErrorCorrectionLevel.Medium, new QrStyle { BackgroundColor = "white" }), output, Codec.Token);
        var readOnlyFormat = await _generator.GeneratePdfAsync(new PrintSheetRequest(Items(1), BarcodeFormat.MaxiCode, ErrorCorrectionLevel.Medium, QrStyle.Default), output, Codec.Token);

        empty.Error.Code.Should().Be(ErrorCodes.Validation);
        noColumns.Error.Code.Should().Be(ErrorCodes.Validation);
        badColour.Error.Code.Should().Be(ErrorCodes.Validation);
        readOnlyFormat.Error.Code.Should().Be(ErrorCodes.UnsupportedFormat);
        output.Length.Should().Be(0);
    }

    private static int CountPages(byte[] pdf) => PageObject().Matches(Encoding.ASCII.GetString(pdf)).Count;

    [GeneratedRegex(@"/Type\s*/Page(?![s\w])")]
    private static partial Regex PageObject();
}
