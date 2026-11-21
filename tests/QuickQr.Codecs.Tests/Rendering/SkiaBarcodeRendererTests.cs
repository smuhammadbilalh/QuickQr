using System.Buffers.Binary;
using System.Text;
using System.Xml.Linq;
using QuickQr.Codecs.Tests.TestSupport;
using QuickQr.Core;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Rendering;
using QuickQr.Infrastructure.Rendering;
using SkiaSharp;

namespace QuickQr.Codecs.Tests.Rendering;

public class SkiaBarcodeRendererTests
{
    private static readonly BarcodeMatrix Qr = Codec.Encode("https://quickqr.app", BarcodeFormat.QrCode, ErrorCorrectionLevel.High);

    private readonly SkiaBarcodeRenderer _renderer = new();

    [Fact]
    public async Task Png_has_the_requested_size_and_embeds_the_dpi()
    {
        var image = await Codec.RenderAsync(Qr, QrStyle.Default, new RenderOptions(512, ExportFormat.Png, Dpi: 300));

        image.Width.Should().Be(512);
        image.Height.Should().Be(512);
        image.MimeType.Should().Be("image/png");
        using var bitmap = TestImages.DecodeBitmap(image.Data);
        bitmap.Width.Should().Be(512);

        var pHYs = IndexOf(image.Data, "pHYs"u8);
        pHYs.Should().BePositive();
        BinaryPrimitives.ReadUInt32BigEndian(image.Data.AsSpan(pHYs + 4)).Should().Be(11811); // 300 dpi in pixels per metre
    }

    [Fact]
    public async Task Transparent_png_keeps_alpha()
    {
        var image = await Codec.RenderAsync(Qr, new QrStyle { TransparentBackground = true });

        using var bitmap = TestImages.DecodeBitmap(image.Data);
        bitmap.GetPixel(1, 1).Alpha.Should().Be(0);
    }

    [Fact]
    public async Task Jpeg_with_a_transparent_request_is_opaque_white()
    {
        var image = await Codec.RenderAsync(Qr, new QrStyle { TransparentBackground = true, BackgroundColor = "#000000" }, new RenderOptions(400, ExportFormat.Jpeg, Dpi: 150));

        image.Data.AsSpan(0, 2).ToArray().Should().Equal(0xFF, 0xD8);
        using var bitmap = TestImages.DecodeBitmap(image.Data);
        var corner = bitmap.GetPixel(2, 2);
        corner.Alpha.Should().Be(255);
        corner.Red.Should().BeGreaterThan(240);
        corner.Green.Should().BeGreaterThan(240);
        corner.Blue.Should().BeGreaterThan(240);

        // JFIF density patched to 150 dpi.
        image.Data[13].Should().Be(1);
        BinaryPrimitives.ReadUInt16BigEndian(image.Data.AsSpan(14)).Should().Be(150);
    }

    [Fact]
    public async Task Webp_output_is_a_riff_webp()
    {
        var image = await Codec.RenderAsync(Qr, QrStyle.Default, new RenderOptions(300, ExportFormat.Webp, Quality: 100));

        Encoding.ASCII.GetString(image.Data, 0, 4).Should().Be("RIFF");
        Encoding.ASCII.GetString(image.Data, 8, 4).Should().Be("WEBP");
    }

    [Fact]
    public async Task Svg_is_well_formed_xml_with_a_view_box()
    {
        var style = new QrStyle
        {
            ModuleShape = ModuleShape.Rounded,
            EyeFrameShape = EyeFrameShape.Circle,
            Gradient = new GradientStyle(GradientType.Linear, "#0D47A1", "#6A1B9A"),
            Logo = new LogoOptions(TestImages.LogoPng()),
            Frame = new FrameOptions(FrameStyle.BannerBottom, "Scan & <go>")
        };

        var image = await Codec.RenderAsync(Qr, style, new RenderOptions(600, ExportFormat.Svg));

        image.MimeType.Should().Be("image/svg+xml");
        var document = XDocument.Parse(Encoding.UTF8.GetString(image.Data));
        var root = document.Root!;
        root.Name.LocalName.Should().Be("svg");
        var viewBox = root.Attribute("viewBox")!.Value.Split(' ');
        viewBox.Should().HaveCount(4);
        float.Parse(viewBox[2], System.Globalization.CultureInfo.InvariantCulture).Should().BeApproximately(image.Width, 1f);
        float.Parse(viewBox[3], System.Globalization.CultureInfo.InvariantCulture).Should().BeApproximately(image.Height, 1f);
        root.Descendants().Should().Contain(e => e.Name.LocalName == "path");
    }

    [Fact]
    public async Task Pdf_is_a_vector_document_sized_from_pixels_and_dpi()
    {
        var image = await Codec.RenderAsync(Qr, QrStyle.Default, new RenderOptions(600, ExportFormat.Pdf, Dpi: 300));

        Encoding.ASCII.GetString(image.Data, 0, 5).Should().Be("%PDF-");
        image.MimeType.Should().Be("application/pdf");
        image.Width.Should().Be(144);  // 600 px / 300 dpi = 2 in = 144 pt
        image.Height.Should().Be(144);
    }

    [Theory]
    [InlineData(FrameStyle.BannerBottom)]
    [InlineData(FrameStyle.BannerTop)]
    [InlineData(FrameStyle.RoundedBox)]
    public async Task Frames_grow_the_output(FrameStyle frame)
    {
        var image = await Codec.RenderAsync(Qr, new QrStyle { Frame = new FrameOptions(frame) }, new RenderOptions(500));

        image.Width.Should().BeGreaterThan(500);
        image.Height.Should().BeGreaterThan(image.Width);
    }

    [Fact]
    public async Task One_dimensional_codes_are_wider_than_tall_with_text_below()
    {
        var matrix = Codec.Encode("5901234123457", BarcodeFormat.Ean13);

        var withText = await Codec.RenderAsync(matrix, QrStyle.Default, new RenderOptions(800));
        var withoutText = await Codec.RenderAsync(matrix, new QrStyle { ShowHumanReadableText = false }, new RenderOptions(800));

        withText.Width.Should().Be(800);
        withText.Height.Should().BeLessThan(800);
        withoutText.Height.Should().BeLessThan(withText.Height);
        withoutText.Height.Should().BeCloseTo(400, 10); // bars ≈ 40 % of the width plus padding
    }

    [Fact]
    public async Task Pdf417_keeps_its_wide_aspect_ratio()
    {
        var matrix = Codec.Encode("PDF417 aspect", BarcodeFormat.Pdf417);

        var image = await Codec.RenderAsync(matrix, QrStyle.Default, new RenderOptions(800));

        image.Width.Should().Be(800);
        image.Height.Should().BeLessThan(800);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12")]
    [InlineData("#GGGGGG")]
    [InlineData("#1234567")]
    [InlineData("")]
    public async Task Invalid_colours_are_validation_errors(string colour)
    {
        var result = await _renderer.RenderAsync(Qr, new QrStyle { ForegroundColor = colour }, new RenderOptions(), Codec.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Validation);
        result.Error.Message.Should().Contain("foreground");
    }

    [Fact]
    public async Task Invalid_gradient_and_eye_colours_are_rejected()
    {
        var gradient = await _renderer.RenderAsync(Qr, new QrStyle { Gradient = new GradientStyle(GradientType.Radial, "#000", "nope") }, new RenderOptions(), Codec.Token);
        var eye = await _renderer.RenderAsync(Qr, new QrStyle { EyeColor = "#XYZ" }, new RenderOptions(), Codec.Token);

        gradient.Error.Code.Should().Be(ErrorCodes.Validation);
        eye.Error.Code.Should().Be(ErrorCodes.Validation);
    }

    [Theory]
    [InlineData("#FFF", 255, 255, 255, 255)]
    [InlineData("#0D47A1", 0x0D, 0x47, 0xA1, 255)]
    [InlineData("#800D47A1", 0x0D, 0x47, 0xA1, 0x80)]
    [InlineData("0d47a1", 0x0D, 0x47, 0xA1, 255)]
    public void Hex_colours_parse(string value, int r, int g, int b, int a)
    {
        HexColor.TryParse(value, out var colour).Should().BeTrue();

        colour.Should().Be(new SKColor((byte)r, (byte)g, (byte)b, (byte)a));
    }

    [Fact]
    public async Task Svg_logos_are_unsupported()
    {
        var svg = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"/>");

        var result = await _renderer.RenderAsync(Qr, new QrStyle { Logo = new LogoOptions(svg) }, new RenderOptions(), Codec.Token);

        result.Error.Code.Should().Be(ErrorCodes.UnsupportedFormat);
    }

    [Fact]
    public async Task Undecodable_logos_are_unsupported()
    {
        var result = await _renderer.RenderAsync(Qr, new QrStyle { Logo = new LogoOptions([1, 2, 3, 4, 5]) }, new RenderOptions(), Codec.Token);

        result.Error.Code.Should().Be(ErrorCodes.UnsupportedFormat);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8193)]
    public async Task Out_of_range_sizes_are_rejected(int size)
    {
        var result = await _renderer.RenderAsync(Qr, QrStyle.Default, new RenderOptions(size), Codec.Token);

        result.Error.Code.Should().Be(ErrorCodes.Validation);
    }

    [Fact]
    public async Task Logo_area_has_no_modules_and_shows_the_plate()
    {
        var style = new QrStyle { Logo = new LogoOptions(TestImages.LogoPng(), Background: LogoBackgroundShape.Square), BackgroundColor = "#FFFFFF" };

        var image = await Codec.RenderAsync(Qr, style, new RenderOptions(600));

        using var bitmap = TestImages.DecodeBitmap(image.Data);
        // The centre of the logo is orange; the corner of the plate (just inside it) is background white.
        var centre = bitmap.GetPixel(300, 300 - 40);
        centre.Red.Should().BeGreaterThan(200);
        centre.Blue.Should().BeLessThan(80);
    }

    [Fact]
    public async Task Rendering_is_thread_safe()
    {
        var styles = new[]
        {
            QrStyle.Default,
            new QrStyle { ModuleShape = ModuleShape.Circle, Gradient = new GradientStyle(GradientType.Radial, "#000000", "#333333") },
            new QrStyle { Frame = new FrameOptions(), Logo = new LogoOptions(TestImages.LogoPng()) }
        };

        var tasks = Enumerable.Range(0, 24)
            .Select(i => _renderer.RenderAsync(Qr, styles[i % styles.Length], new RenderOptions(256, (ExportFormat)(i % 5)), Codec.Token));
        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r.IsSuccess && r.Value.Data.Length > 0);
    }

    [Fact]
    public async Task Cancellation_is_honoured()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => _renderer.RenderAsync(Qr, QrStyle.Default, new RenderOptions(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static int IndexOf(byte[] data, ReadOnlySpan<byte> value) => data.AsSpan().IndexOf(value);
}
