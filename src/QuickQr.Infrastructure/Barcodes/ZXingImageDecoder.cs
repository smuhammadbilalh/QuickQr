using QuickQr.Core.Barcodes;
using QuickQr.Infrastructure.Rendering;
using SkiaSharp;
using ZXing.Common;
using ZXing.Multi;
using ZXing.Multi.QrCode;
using BinaryBitmap = ZXing.BinaryBitmap;
using DecodeHintType = ZXing.DecodeHintType;
using InvertedLuminanceSource = ZXing.InvertedLuminanceSource;
using LuminanceSource = ZXing.LuminanceSource;
using MultiFormatReader = ZXing.MultiFormatReader;
using RGBLuminanceSource = ZXing.RGBLuminanceSource;
using ZxFormat = ZXing.BarcodeFormat;
using ZxResult = ZXing.Result;

namespace QuickQr.Infrastructure.Barcodes;

/// <summary>
/// Decodes still images with SkiaSharp (EXIF-aware) and ZXing.Net. Finds several codes per image and falls back through
/// rotation, inversion and downscaling before giving up. Never throws for unreadable input: it returns an empty list.
/// </summary>
public sealed class ZXingImageDecoder : IBarcodeImageDecoder
{
    /// <summary>Longest image side handed to ZXing; larger photos are downscaled (keeps decoding fast and binarisation sane).</summary>
    internal const int MaxDecodeSide = 2000;

    public async Task<IReadOnlyList<DecodedBarcode>> DecodeAsync(Stream image, DecodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        options ??= new DecodeOptions();

        byte[] bytes;
        try
        {
            if (image is MemoryStream { Position: 0 } memory && memory.TryGetBuffer(out var segment))
            {
                bytes = segment.AsSpan(0, (int)memory.Length).ToArray();
            }
            else
            {
                using var copy = new MemoryStream();
                await image.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
                bytes = copy.ToArray();
            }
        }
        catch (IOException)
        {
            return [];
        }
        catch (NotSupportedException)
        {
            return [];
        }

        if (bytes.Length == 0) return [];

        return await Task.Run(() => DecodeCore(bytes, options, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<DecodedBarcode> DecodeCore(byte[] bytes, DecodeOptions options, CancellationToken cancellationToken)
    {
        LuminanceSource source;
        using (var bitmap = SafeDecode(bytes))
        {
            if (bitmap is null) return [];
            cancellationToken.ThrowIfCancellationRequested();
            source = ToLuminance(bitmap);
        }

        var hints = BuildHints(options);
        var found = new List<DecodedBarcode>();

        // Cheapest first; stop at the first pass that finds anything.
        Func<LuminanceSource, LuminanceSource>[] passes =
        [
            s => s,
            s => s.RotateSupported ? s.rotateCounterClockwise() : s,
            s => new InvertedLuminanceSource(s),
            HalfScale
        ];

        for (var i = 0; i < passes.Length && found.Count == 0; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (i == 2 && !options.TryInverted) continue;

            LuminanceSource pass;
            try
            {
                pass = passes[i](source);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                continue;
            }

            Collect(found, DecodePass(pass, hints, options.DetectMultiple, cancellationToken));
        }

        return found;
    }

    private static SKBitmap? SafeDecode(byte[] bytes)
    {
        try
        {
            return SkiaImageLoader.Decode(bytes, MaxDecodeSide);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static Dictionary<DecodeHintType, object> BuildHints(DecodeOptions options)
    {
        var formats = (options.Formats is { Count: > 0 } requested ? requested : ZXingFormatMap.Readable)
            .Select(ZXingFormatMap.ToZXing)
            .OfType<ZxFormat>()
            .Distinct()
            .ToList();

        var hints = new Dictionary<DecodeHintType, object>
        {
            [DecodeHintType.POSSIBLE_FORMATS] = formats
        };
        if (options.TryHarder) hints[DecodeHintType.TRY_HARDER] = true;
        if (options.TryInverted) hints[DecodeHintType.ALSO_INVERTED] = true;
        return hints;
    }

    private static List<ZxResult> DecodePass(LuminanceSource source, Dictionary<DecodeHintType, object> hints, bool multiple, CancellationToken cancellationToken)
    {
        var results = new List<ZxResult>();
        var formats = (List<ZxFormat>)hints[DecodeHintType.POSSIBLE_FORMATS];

        if (multiple)
        {
            // The QR multi-reader finds every finder-pattern triple; the generic reader splits the image around each hit.
            if (formats.Contains(ZxFormat.QR_CODE))
                results.AddRange(Try(() => new QRCodeMultiReader().decodeMultiple(Bitmap(source), hints)));

            cancellationToken.ThrowIfCancellationRequested();
            results.AddRange(Try(() => new GenericMultipleBarcodeReader(new MultiFormatReader()).decodeMultiple(Bitmap(source), hints)));
        }
        else
        {
            var single = Try(() => new MultiFormatReader().decode(Bitmap(source), hints) is { } r ? [r] : null);
            results.AddRange(single);
        }

        return results;
    }

    private static BinaryBitmap Bitmap(LuminanceSource source) => new(new HybridBinarizer(source));

    private static ZxResult[] Try(Func<ZxResult[]?> decode)
    {
        try
        {
            return decode() ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // ZXing signals "nothing here" with null, but a few readers throw on degenerate input.
            return [];
        }
    }

    private static void Collect(List<DecodedBarcode> found, List<ZxResult> results)
    {
        foreach (var result in results)
        {
            if (string.IsNullOrEmpty(result.Text)) continue;
            var format = ZXingFormatMap.FromZXing(result.BarcodeFormat);
            if (found.Exists(d => d.Format == format && string.Equals(d.Text, result.Text, StringComparison.Ordinal))) continue;
            found.Add(new DecodedBarcode(result.Text, format, result.RawBytes));
        }
    }

    /// <summary>Builds an 8-bit luminance plane straight from the pixel span, compositing transparency onto white.</summary>
    internal static RGBLuminanceSource ToLuminance(SKBitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var rowBytes = bitmap.RowBytes;
        var pixels = bitmap.GetPixelSpan();
        var luminance = new byte[width * height];
        var bgra = bitmap.ColorType == SKColorType.Bgra8888;

        for (var y = 0; y < height; y++)
        {
            var row = pixels.Slice(y * rowBytes, width * 4);
            var offset = y * width;
            for (var x = 0; x < width; x++)
            {
                var p = row.Slice(x * 4, 4);
                int r = bgra ? p[2] : p[0], g = p[1], b = bgra ? p[0] : p[2], a = p[3];
                // Premultiplied colour over white: c + (255 - a). Rec. 601 weights in fixed point.
                var lum = ((r * 77) + (g * 150) + (b * 29)) >> 8;
                luminance[offset + x] = (byte)Math.Min(255, lum + (255 - a));
            }
        }

        return new RGBLuminanceSource(luminance, width, height, RGBLuminanceSource.BitmapFormat.Gray8);
    }

    private static LuminanceSource HalfScale(LuminanceSource source)
    {
        var width = source.Width / 2;
        var height = source.Height / 2;
        if (width < 32 || height < 32) return source;

        var full = source.Matrix;
        var stride = source.Width;
        var half = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            var top = 2 * y * stride;
            var bottom = top + stride;
            for (var x = 0; x < width; x++)
            {
                var i = 2 * x;
                half[(y * width) + x] = (byte)((full[top + i] + full[top + i + 1] + full[bottom + i] + full[bottom + i + 1] + 2) >> 2);
            }
        }
        return new RGBLuminanceSource(half, width, height, RGBLuminanceSource.BitmapFormat.Gray8);
    }
}
