using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace QuickQr.Infrastructure.Rendering;

/// <summary>Post-processing of encoded output: physical resolution for PNG/JPEG and a viewBox for SVG.</summary>
internal static class ImageMetadata
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Inserts a pHYs chunk (pixels per metre) right after IHDR. Returns the input unchanged if it is not a plain PNG.</summary>
    public static byte[] WithPngDpi(byte[] png, int dpi)
    {
        const int ihdrEnd = 8 + 4 + 4 + 13 + 4;
        if (dpi <= 0 || png.Length < ihdrEnd || !png.AsSpan(0, 8).SequenceEqual(PngSignature)) return png;
        if (!png.AsSpan(12, 4).SequenceEqual("IHDR"u8) || HasChunkBeforeImageData(png, "pHYs"u8)) return png;

        var pixelsPerMetre = (uint)Math.Round(dpi / 0.0254);
        Span<byte> chunk = stackalloc byte[4 + 4 + 9 + 4];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, 9);
        "pHYs"u8.CopyTo(chunk[4..]);
        BinaryPrimitives.WriteUInt32BigEndian(chunk[8..], pixelsPerMetre);
        BinaryPrimitives.WriteUInt32BigEndian(chunk[12..], pixelsPerMetre);
        chunk[16] = 1; // unit: metre
        BinaryPrimitives.WriteUInt32BigEndian(chunk[17..], Crc32(chunk[4..17]));

        var result = new byte[png.Length + chunk.Length];
        png.AsSpan(0, ihdrEnd).CopyTo(result);
        chunk.CopyTo(result.AsSpan(ihdrEnd));
        png.AsSpan(ihdrEnd).CopyTo(result.AsSpan(ihdrEnd + chunk.Length));
        return result;
    }

    /// <summary>Sets the JFIF APP0 density to <paramref name="dpi"/> dots per inch (in place). No-op without a JFIF header.</summary>
    public static byte[] WithJpegDpi(byte[] jpeg, int dpi)
    {
        if (dpi <= 0 || jpeg.Length < 18) return jpeg;
        var isJfif = jpeg[0] == 0xFF && jpeg[1] == 0xD8 && jpeg[2] == 0xFF && jpeg[3] == 0xE0 && jpeg.AsSpan(6, 5).SequenceEqual("JFIF\0"u8);
        if (!isJfif) return jpeg;

        var density = (ushort)Math.Clamp(dpi, 1, ushort.MaxValue);
        jpeg[13] = 1; // units: dots per inch
        BinaryPrimitives.WriteUInt16BigEndian(jpeg.AsSpan(14), density);
        BinaryPrimitives.WriteUInt16BigEndian(jpeg.AsSpan(16), density);
        return jpeg;
    }

    /// <summary>Adds a viewBox (Skia's SVG canvas only writes width/height) so the SVG scales cleanly.</summary>
    public static byte[] WithSvgViewBox(byte[] svg, float width, float height)
    {
        XDocument document;
        using (var input = new MemoryStream(svg, writable: false))
        {
            document = XDocument.Load(input, LoadOptions.PreserveWhitespace);
        }

        var root = document.Root ?? throw new InvalidOperationException("The SVG writer produced an empty document.");
        var w = width.ToString("0.###", CultureInfo.InvariantCulture);
        var h = height.ToString("0.###", CultureInfo.InvariantCulture);
        root.SetAttributeValue("viewBox", $"0 0 {w} {h}");
        root.SetAttributeValue("width", w);
        root.SetAttributeValue("height", h);

        using var output = new MemoryStream();
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false }))
        {
            document.Save(writer);
        }
        return output.ToArray();
    }

    private static bool HasChunkBeforeImageData(byte[] png, ReadOnlySpan<byte> type)
    {
        var offset = 8;
        while (offset + 8 <= png.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
            var chunkType = png.AsSpan(offset + 4, 4);
            if (chunkType.SequenceEqual(type)) return true;
            if (chunkType.SequenceEqual("IDAT"u8) || length > int.MaxValue - offset - 12) return false;
            offset += 12 + (int)length;
        }
        return false;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
