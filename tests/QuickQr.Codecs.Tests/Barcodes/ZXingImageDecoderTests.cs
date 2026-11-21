using QuickQr.Codecs.Tests.TestSupport;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Rendering;
using QuickQr.Infrastructure.Rendering;
using SkiaSharp;

namespace QuickQr.Codecs.Tests.Barcodes;

public class ZXingImageDecoderTests
{
    [Fact]
    public async Task Finds_several_codes_in_one_image()
    {
        var first = await Codec.RenderAsync(Codec.Encode("FIRST CODE"), QrStyle.Default, new RenderOptions(360));
        var second = await Codec.RenderAsync(Codec.Encode("https://quickqr.app/second"), new QrStyle { ModuleShape = ModuleShape.Circle }, new RenderOptions(360));
        var third = await Codec.RenderAsync(Codec.Encode("BARCODE-3", BarcodeFormat.Code128), QrStyle.Default, new RenderOptions(600));
        var composite = TestImages.SideBySide(first.Data, second.Data, third.Data);

        var decoded = await Codec.DecodeAsync(composite);

        decoded.Select(d => d.Text).Should().Contain(["FIRST CODE", "https://quickqr.app/second"]);
        decoded.Count.Should().BeGreaterThanOrEqualTo(2);
        decoded.Should().OnlyHaveUniqueItems(d => d.Text + "|" + d.Format);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Decodes_a_90_degree_rotated_image(bool tryHarder)
    {
        var image = await Codec.RenderAsync(Codec.Encode("ROTATED-128", BarcodeFormat.Code128), QrStyle.Default, new RenderOptions(800));
        var rotated = TestImages.Rotate90(image.Data);

        var decoded = await Codec.DecodeAsync(rotated, new DecodeOptions(TryHarder: tryHarder, Formats: [BarcodeFormat.Code128]));

        decoded.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Text = "ROTATED-128", Format = BarcodeFormat.Code128 });
    }

    [Fact]
    public async Task Decodes_a_rotated_qr_code()
    {
        var image = await Codec.RenderAsync(Codec.Encode("ROTATED QR"), QrStyle.Default, new RenderOptions(400));

        var decoded = await Codec.DecodeAsync(TestImages.Rotate90(image.Data));

        decoded.Select(d => d.Text).Should().Contain("ROTATED QR");
    }

    [Fact]
    public async Task Decodes_inverted_codes()
    {
        var style = new QrStyle { ForegroundColor = "#FFFFFF", BackgroundColor = "#000000" };
        var image = await Codec.RenderAsync(Codec.Encode("INVERTED"), style, new RenderOptions(400));

        var decoded = await Codec.DecodeAsync(image.Data, Codec.Only(BarcodeFormat.QrCode));

        decoded.Should().ContainSingle().Which.Text.Should().Be("INVERTED");
    }

    [Fact]
    public async Task Inverted_codes_are_skipped_when_inversion_is_disabled()
    {
        var style = new QrStyle { ForegroundColor = "#FFFFFF", BackgroundColor = "#000000" };
        var image = await Codec.RenderAsync(Codec.Encode("INVERTED"), style, new RenderOptions(400));

        var decoded = await Codec.DecodeAsync(image.Data, new DecodeOptions(TryInverted: false, Formats: [BarcodeFormat.QrCode]));

        decoded.Should().BeEmpty();
    }

    [Fact]
    public async Task Large_photos_are_downscaled_and_still_decode()
    {
        var image = await Codec.RenderAsync(Codec.Encode("https://quickqr.app/large-photo"), QrStyle.Default, new RenderOptions(1000));
        var photo = TestImages.Enlarge(image.Data, 4200);

        var decoded = await Codec.DecodeAsync(photo, Codec.Only(BarcodeFormat.QrCode));

        decoded.Should().ContainSingle().Which.Text.Should().Be("https://quickqr.app/large-photo");
    }

    [Fact]
    public async Task Single_mode_returns_one_code()
    {
        var image = await Codec.RenderAsync(Codec.Encode("SINGLE"), QrStyle.Default, new RenderOptions(300));

        var decoded = await Codec.DecodeAsync(image.Data, new DecodeOptions(DetectMultiple: false));

        decoded.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Text = "SINGLE", Format = BarcodeFormat.QrCode });
    }

    [Fact]
    public async Task Format_filter_excludes_other_symbologies()
    {
        var image = await Codec.RenderAsync(Codec.Encode("ONLY QR"), QrStyle.Default, new RenderOptions(300));

        var decoded = await Codec.DecodeAsync(image.Data, Codec.Only(BarcodeFormat.DataMatrix));

        decoded.Should().BeEmpty();
    }

    [Fact]
    public async Task Images_without_codes_return_an_empty_list()
    {
        var decoded = await Codec.DecodeAsync(TestImages.Blank(640, 480, SKColors.Beige));

        decoded.Should().BeEmpty();
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 })] // truncated PNG
    public async Task Undecodable_input_returns_an_empty_list(byte[] data)
    {
        var decoded = await Codec.DecodeAsync(data);

        decoded.Should().BeEmpty();
    }

    [Fact]
    public async Task Reads_from_non_seekable_streams()
    {
        var image = await Codec.RenderAsync(Codec.Encode("STREAMED"), QrStyle.Default, new RenderOptions(300));
        await using var stream = new NonSeekableStream(image.Data);

        var decoded = await Codec.Decoder.DecodeAsync(stream, cancellationToken: Codec.Token);

        decoded.Select(d => d.Text).Should().Contain("STREAMED");
    }

    [Fact]
    public async Task Cancellation_is_honoured()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var stream = new MemoryStream(TestImages.Blank(64, 64, SKColors.White));

        var act = () => Codec.Decoder.DecodeAsync(stream, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(1, 200, 100)]
    [InlineData(3, 200, 100)]
    [InlineData(6, 100, 200)]
    [InlineData(8, 100, 200)]
    public void Exif_orientation_is_honoured(ushort orientation, int expectedWidth, int expectedHeight)
    {
        var jpeg = TestImages.JpegWithOrientation(200, 100, orientation);

        using var bitmap = SkiaImageLoader.Decode(jpeg);

        bitmap.Should().NotBeNull();
        bitmap!.Width.Should().Be(expectedWidth);
        bitmap.Height.Should().Be(expectedHeight);
    }

    [Fact]
    public void Loader_downscales_to_the_requested_maximum()
    {
        var png = TestImages.Blank(3000, 1500, SKColors.White);

        using var bitmap = SkiaImageLoader.Decode(png, 2000);

        bitmap!.Width.Should().Be(2000);
        bitmap.Height.Should().Be(1000);
    }

    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
        public override long Position { get => base.Position; set => throw new NotSupportedException(); }
    }
}
