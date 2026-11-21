using QuickQr.Codecs.Tests.TestSupport;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Rendering;

namespace QuickQr.Codecs.Tests.Rendering;

/// <summary>The key proof: every style the studio offers still produces a code that scans.</summary>
public class RoundTripTests
{
    private const string Url = "https://quickqr.app/c/7Hq2xK?src=test-suite";

    private static readonly byte[] Logo = TestImages.LogoPng();

    private static readonly GradientStyle Linear = new(GradientType.Linear, "#0D47A1", "#6A1B9A");
    private static readonly GradientStyle Radial = new(GradientType.Radial, "#1B5E20", "#004D40");

    private static readonly Dictionary<string, QrStyle> Styles = BuildStyles();

    public static TheoryData<string> StyleNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in Styles.Keys) data.Add(name);
            return data;
        }
    }

    private static readonly (BarcodeFormat Format, string Payload)[] TwoDimensional =
    [
        (BarcodeFormat.DataMatrix, "SN-2026-000123"),
        (BarcodeFormat.DataMatrix, "Ünïcødé ✓ 数据"),
        (BarcodeFormat.Aztec, "M1DOE/JOHN EABC123 LHRJFKBA 0123 001Y"),
        (BarcodeFormat.Aztec, "Grüße ✓"),
        (BarcodeFormat.Pdf417, "@ANSI 636000 DL DAQ12345678 DCSDOE"),
        (BarcodeFormat.Pdf417, "Grüße ✓")
    ];

    private static readonly (BarcodeFormat Format, string Payload, string Expected)[] OneDimensional =
    [
        (BarcodeFormat.Code128, "QuickQr-128 ok", "QuickQr-128 ok"),
        (BarcodeFormat.Code39, "QUICKQR 39", "QUICKQR 39"),
        (BarcodeFormat.Code93, "QuickQr 93", "QuickQr 93"),
        (BarcodeFormat.Ean8, "9638507", "96385074"),
        (BarcodeFormat.Ean13, "590123412345", "5901234123457"),
        (BarcodeFormat.UpcA, "03600029145", "036000291452"),
        (BarcodeFormat.UpcE, "0123456", "01234565"),
        (BarcodeFormat.Itf, "10012345678902", "10012345678902"),
        (BarcodeFormat.Codabar, "A40156B", "40156") // start/stop characters are not part of the decoded text
    ];

    private static Dictionary<string, QrStyle> BuildStyles()
    {
        var styles = new Dictionary<string, QrStyle>(StringComparer.Ordinal)
        {
            ["default"] = QrStyle.Default,
            ["colours"] = new() { ForegroundColor = "#1A237E", BackgroundColor = "#FFF8E1", EyeColor = "#B71C1C" },
            ["short-hex"] = new() { ForegroundColor = "#123", BackgroundColor = "#FFFFFFFF" },
            ["transparent"] = new() { TransparentBackground = true },
            ["gradient-linear-45"] = new() { Gradient = Linear },
            ["gradient-linear-0"] = new() { Gradient = Linear with { AngleDegrees = 0 } },
            ["gradient-linear-120"] = new() { Gradient = Linear with { AngleDegrees = 120 } },
            ["gradient-radial"] = new() { Gradient = Radial },
            ["gradient-with-eye-colour"] = new() { Gradient = Linear, EyeColor = "#000000" },
            ["logo-rounded-022"] = new() { Logo = new LogoOptions(Logo) },
            ["logo-square"] = new() { Logo = new LogoOptions(Logo, Background: LogoBackgroundShape.Square) },
            ["logo-circle"] = new() { Logo = new LogoOptions(Logo, Background: LogoBackgroundShape.Circle) },
            ["logo-no-plate"] = new() { Logo = new LogoOptions(Logo, Background: LogoBackgroundShape.None) },
            ["logo-030"] = new() { Logo = new LogoOptions(Logo, SizeRatio: 0.30f) },
            ["frame-banner-bottom"] = new() { Frame = new FrameOptions() },
            ["frame-banner-top"] = new() { Frame = new FrameOptions(FrameStyle.BannerTop, "SCAN TO PAY", "#0D47A1", "#FFFFFF") },
            ["frame-rounded-box"] = new() { Frame = new FrameOptions(FrameStyle.RoundedBox, "quickqr.app", "#263238") },
            ["frame-rounded-box-no-text"] = new() { Frame = new FrameOptions(FrameStyle.RoundedBox, "") },
            ["frame-transparent"] = new() { TransparentBackground = true, Frame = new FrameOptions() },
            ["quiet-zone-2"] = new() { QuietZoneModules = 2 },
            ["everything"] = new()
            {
                ModuleShape = ModuleShape.Rounded,
                EyeFrameShape = EyeFrameShape.Circle,
                EyeBallShape = EyeBallShape.Diamond,
                Gradient = Linear,
                Logo = new LogoOptions(Logo, 0.22f),
                Frame = new FrameOptions(FrameStyle.BannerBottom, "SCAN ME", "#311B92")
            }
        };

        foreach (var shape in Enum.GetValues<ModuleShape>())
            styles[$"modules-{shape}"] = new QrStyle { ModuleShape = shape };

        foreach (var frame in Enum.GetValues<EyeFrameShape>())
        {
            foreach (var ball in Enum.GetValues<EyeBallShape>())
                styles[$"eyes-{frame}-{ball}"] = new QrStyle { EyeFrameShape = frame, EyeBallShape = ball, ModuleShape = ModuleShape.Rounded };
        }

        foreach (var shape in new[] { ModuleShape.Circle, ModuleShape.Diamond, ModuleShape.VerticalBars, ModuleShape.HorizontalBars })
            styles[$"logo-with-{shape}"] = new QrStyle { ModuleShape = shape, Logo = new LogoOptions(Logo), EyeFrameShape = EyeFrameShape.Rounded, EyeBallShape = EyeBallShape.Circle };

        return styles;
    }

    [Theory]
    [MemberData(nameof(StyleNames))]
    public async Task Styled_qr_codes_decode_back_to_the_payload(string styleName)
    {
        var style = Styles[styleName];
        // As in QrGenerationService: a logo forces High error correction.
        var level = style.Logo is null ? ErrorCorrectionLevel.Medium : ErrorCorrectionLevel.High;
        var matrix = Codec.Encode(Url, BarcodeFormat.QrCode, level);

        var image = await Codec.RenderAsync(matrix, style, new RenderOptions(720));
        var decoded = await Codec.DecodeAsync(image.Data, Codec.Only(BarcodeFormat.QrCode));

        decoded.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Text = Url, Format = BarcodeFormat.QrCode });
    }

    [Theory]
    [InlineData(ExportFormat.Jpeg)]
    [InlineData(ExportFormat.Webp)]
    public async Task Lossy_exports_still_decode(ExportFormat format)
    {
        var matrix = Codec.Encode(Url, BarcodeFormat.QrCode, ErrorCorrectionLevel.High);
        var style = new QrStyle { ModuleShape = ModuleShape.Circle, Gradient = Radial, Logo = new LogoOptions(Logo) };

        var image = await Codec.RenderAsync(matrix, style, new RenderOptions(600, format, Quality: 80));
        var decoded = await Codec.DecodeAsync(image.Data, Codec.Only(BarcodeFormat.QrCode));

        decoded.Select(d => d.Text).Should().Contain(Url);
    }

    public static TheoryData<BarcodeFormat, string> TwoDimensionalPayloads
    {
        get
        {
            var data = new TheoryData<BarcodeFormat, string>();
            foreach (var (format, payload) in TwoDimensional) data.Add(format, payload);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(TwoDimensionalPayloads))]
    public async Task Other_2d_symbologies_round_trip(BarcodeFormat format, string payload)
    {
        var matrix = Codec.Encode(payload, format);
        var style = new QrStyle { ForegroundColor = "#102030", Frame = new FrameOptions() };

        var image = await Codec.RenderAsync(matrix, style, new RenderOptions(800));
        var decoded = await Codec.DecodeAsync(image.Data, Codec.Only(format));

        decoded.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Text = payload, Format = format });
    }

    public static TheoryData<BarcodeFormat, string, string> OneDimensionalPayloads
    {
        get
        {
            var data = new TheoryData<BarcodeFormat, string, string>();
            foreach (var (format, payload, expected) in OneDimensional) data.Add(format, payload, expected);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(OneDimensionalPayloads))]
    public async Task Every_writable_1d_format_round_trips(BarcodeFormat format, string payload, string expected)
    {
        var matrix = Codec.Encode(payload, format);

        var image = await Codec.RenderAsync(matrix, QrStyle.Default, new RenderOptions(900));
        var decoded = await Codec.DecodeAsync(image.Data, Codec.Only(format));

        decoded.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Text = expected, Format = format });
    }

    [Fact]
    public void All_writable_formats_are_covered_by_the_round_trip_tests()
    {
        var covered = OneDimensional.Select(row => row.Format)
            .Concat(TwoDimensional.Select(row => row.Format))
            .Append(BarcodeFormat.QrCode)
            .Distinct();

        covered.Should().BeEquivalentTo(BarcodeFormatExtensions.Writable);
    }

    [Fact]
    public async Task Framed_1d_code_with_gradient_decodes()
    {
        var matrix = Codec.Encode("FRAMED-128", BarcodeFormat.Code128);
        var style = new QrStyle { Gradient = Linear, Frame = new FrameOptions(FrameStyle.BannerBottom, "SCAN ME") };

        var image = await Codec.RenderAsync(matrix, style, new RenderOptions(900));
        var decoded = await Codec.DecodeAsync(image.Data, Codec.Only(BarcodeFormat.Code128));

        decoded.Select(d => d.Text).Should().ContainSingle().Which.Should().Be("FRAMED-128");
    }
}
