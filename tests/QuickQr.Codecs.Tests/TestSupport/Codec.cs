using QuickQr.Core.Barcodes;
using QuickQr.Core.Rendering;
using QuickQr.Infrastructure.Barcodes;
using QuickQr.Infrastructure.Rendering;

namespace QuickQr.Codecs.Tests.TestSupport;

/// <summary>The real codec stack wired together, plus encode → render → decode helpers.</summary>
internal static class Codec
{
    public static ZXingBarcodeEncoder Encoder { get; } = new();
    public static SkiaBarcodeRenderer Renderer { get; } = new();
    public static ZXingImageDecoder Decoder { get; } = new();

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public static BarcodeMatrix Encode(string payload, BarcodeFormat format = BarcodeFormat.QrCode, ErrorCorrectionLevel level = ErrorCorrectionLevel.Medium)
    {
        var result = Encoder.Encode(new BarcodeEncodeRequest(payload, format, level));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    public static async Task<RenderedImage> RenderAsync(BarcodeMatrix matrix, QrStyle? style = null, RenderOptions? options = null)
    {
        var result = await Renderer.RenderAsync(matrix, style ?? QrStyle.Default, options ?? new RenderOptions(640), Token);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    public static async Task<IReadOnlyList<DecodedBarcode>> DecodeAsync(byte[] image, DecodeOptions? options = null)
    {
        using var stream = new MemoryStream(image);
        return await Decoder.DecodeAsync(stream, options, Token);
    }

    public static DecodeOptions Only(BarcodeFormat format) => new(Formats: [format]);
}
