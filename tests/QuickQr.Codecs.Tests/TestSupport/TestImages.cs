using System.Buffers.Binary;
using SkiaSharp;

namespace QuickQr.Codecs.Tests.TestSupport;

/// <summary>Builds and manipulates images for codec tests.</summary>
internal static class TestImages
{
    /// <summary>A colourful square logo (orange disc with a white bar) as PNG.</summary>
    public static byte[] LogoPng(int size = 256)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(0xFF, 0x6D, 0x00) };
        canvas.DrawCircle(size / 2f, size / 2f, size * 0.48f, paint);
        paint.Color = SKColors.White;
        canvas.DrawRect(size * 0.25f, size * 0.42f, size * 0.5f, size * 0.16f, paint);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static SKBitmap DecodeBitmap(byte[] encoded) =>
        SKBitmap.Decode(encoded) ?? throw new InvalidOperationException("Not an image.");

    /// <summary>Places the images left to right on a white canvas with gaps, returned as PNG.</summary>
    public static byte[] SideBySide(params byte[][] images)
    {
        var bitmaps = images.Select(DecodeBitmap).ToList();
        try
        {
            const int gap = 80;
            var width = bitmaps.Sum(b => b.Width) + (gap * (bitmaps.Count + 1));
            var height = bitmaps.Max(b => b.Height) + (2 * gap);
            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);
            var x = gap;
            foreach (var bitmap in bitmaps)
            {
                canvas.DrawBitmap(bitmap, x, gap + ((height - (2 * gap) - bitmap.Height) / 2f), new SKSamplingOptions(SKFilterMode.Nearest));
                x += bitmap.Width + gap;
            }
            return Encode(surface, SKEncodedImageFormat.Png);
        }
        finally
        {
            bitmaps.ForEach(b => b.Dispose());
        }
    }

    /// <summary>Rotates the image 90° clockwise, returned as PNG.</summary>
    public static byte[] Rotate90(byte[] encoded)
    {
        using var source = DecodeBitmap(encoded);
        using var surface = SKSurface.Create(new SKImageInfo(source.Height, source.Width, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.Translate(source.Height, 0);
        canvas.RotateDegrees(90);
        canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        return Encode(surface, SKEncodedImageFormat.Png);
    }

    /// <summary>Draws the image scaled onto a larger white canvas (e.g. a phone-camera sized photo).</summary>
    public static byte[] Enlarge(byte[] encoded, int canvasSide, SKEncodedImageFormat format = SKEncodedImageFormat.Jpeg)
    {
        using var source = DecodeBitmap(encoded);
        using var surface = SKSurface.Create(new SKImageInfo(canvasSide, canvasSide, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        var side = canvasSide * 0.6f;
        canvas.DrawBitmap(source, SKRect.Create((canvasSide - side) / 2f, (canvasSide - side) / 2f, side, side), new SKSamplingOptions(SKFilterMode.Linear));
        return Encode(surface, format);
    }

    /// <summary>A plain image without any code.</summary>
    public static byte[] Blank(int width, int height, SKColor color)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(color);
        return Encode(surface, SKEncodedImageFormat.Png);
    }

    /// <summary>A JPEG of the given size with an EXIF APP1 segment carrying <paramref name="orientation"/> (1–8).</summary>
    public static byte[] JpegWithOrientation(int width, int height, ushort orientation)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.LightGray);
        var jpeg = Encode(surface, SKEncodedImageFormat.Jpeg);

        // "Exif\0\0" + little-endian TIFF header + IFD0 with a single Orientation (0x0112) SHORT entry.
        var tiff = new byte[8 + 2 + 12 + 4];
        "II"u8.CopyTo(tiff);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(10), 0x0112);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(12), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(14), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(18), orientation);
        var payload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();

        var segment = new byte[4 + payload.Length];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(payload.Length + 2));
        payload.CopyTo(segment, 4);

        return [.. jpeg.AsSpan(0, 2), .. segment, .. jpeg.AsSpan(2)];
    }

    private static byte[] Encode(SKSurface surface, SKEncodedImageFormat format)
    {
        using var image = surface.Snapshot();
        using var data = image.Encode(format, 95);
        return data.ToArray();
    }
}
