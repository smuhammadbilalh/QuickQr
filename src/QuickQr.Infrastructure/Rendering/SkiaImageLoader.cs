using SkiaSharp;

namespace QuickQr.Infrastructure.Rendering;

/// <summary>Decodes encoded images (PNG/JPEG/WebP/GIF/BMP/HEIF…) into upright RGBA bitmaps.</summary>
internal static class SkiaImageLoader
{
    /// <summary>
    /// Decodes <paramref name="data"/> honouring the EXIF orientation and downscaling so the longest side is at most
    /// <paramref name="maxSide"/> pixels. Returns null when the data is not a decodable image. The bitmap is RGBA 8888 premultiplied.
    /// </summary>
    public static SKBitmap? Decode(ReadOnlySpan<byte> data, int maxSide = int.MaxValue)
    {
        if (data.IsEmpty) return null;

        using var skData = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(skData);
        if (codec is null) return null;

        var raw = codec.Info;
        if (raw.Width <= 0 || raw.Height <= 0) return null;

        // Let the codec do the coarse downscale (JPEG decodes at 1/2, 1/4, 1/8 for free).
        var size = new SKSizeI(raw.Width, raw.Height);
        var longest = Math.Max(raw.Width, raw.Height);
        if (longest > maxSide)
        {
            var scaled = codec.GetScaledDimensions((float)maxSide / longest);
            if (scaled.Width >= 1 && scaled.Height >= 1) size = scaled;
        }

        var decoded = DecodeAt(codec, size) ?? (size.Width != raw.Width ? DecodeAt(codec, new SKSizeI(raw.Width, raw.Height)) : null);
        if (decoded is null) return null;

        // Exact resample down to the limit if the codec could not get there on its own.
        if (Math.Max(decoded.Width, decoded.Height) > maxSide)
        {
            var factor = (float)maxSide / Math.Max(decoded.Width, decoded.Height);
            var target = new SKSizeI(Math.Max(1, (int)MathF.Round(decoded.Width * factor)), Math.Max(1, (int)MathF.Round(decoded.Height * factor)));
            var resized = decoded.Resize(target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            if (resized is not null)
            {
                decoded.Dispose();
                decoded = resized;
            }
        }

        return ApplyOrigin(decoded, codec.EncodedOrigin);
    }

    private static SKBitmap? DecodeAt(SKCodec codec, SKSizeI size)
    {
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is SKCodecResult.Success or SKCodecResult.IncompleteInput) return bitmap;
        bitmap.Dispose();
        return null;
    }

    /// <summary>Rotates/flips <paramref name="source"/> so it displays upright (disposes the source when a new bitmap is made).</summary>
    internal static SKBitmap ApplyOrigin(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft) return source;

        float w = source.Width, h = source.Height;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

        // Same transforms as Skia's SkEncodedOriginToMatrix.
        var m = origin switch
        {
            SKEncodedOrigin.TopRight => Matrix(-1, 0, w, 0, 1, 0),
            SKEncodedOrigin.BottomRight => Matrix(-1, 0, w, 0, -1, h),
            SKEncodedOrigin.BottomLeft => Matrix(1, 0, 0, 0, -1, h),
            SKEncodedOrigin.LeftTop => Matrix(0, 1, 0, 1, 0, 0),
            SKEncodedOrigin.RightTop => Matrix(0, -1, h, 1, 0, 0),
            SKEncodedOrigin.RightBottom => Matrix(0, -1, h, -1, 0, w),
            SKEncodedOrigin.LeftBottom => Matrix(0, 1, 0, -1, 0, w),
            _ => SKMatrix.Identity
        };

        var info = new SKImageInfo(swap ? source.Height : source.Width, swap ? source.Width : source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var upright = new SKBitmap(info);
        using (var canvas = new SKCanvas(upright))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.SetMatrix(in m);
            canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        }
        source.Dispose();
        return upright;
    }

    private static SKMatrix Matrix(float scaleX, float skewX, float transX, float skewY, float scaleY, float transY) => new()
    {
        ScaleX = scaleX,
        SkewX = skewX,
        TransX = transX,
        SkewY = skewY,
        ScaleY = scaleY,
        TransY = transY,
        Persp2 = 1
    };
}
