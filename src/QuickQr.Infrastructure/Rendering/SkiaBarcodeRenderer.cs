using System.Text;
using QuickQr.Core;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Rendering;
using SkiaSharp;

namespace QuickQr.Infrastructure.Rendering;

/// <summary>
/// SkiaSharp renderer: PNG/JPEG/WebP raster, vector PDF (<see cref="SKDocument"/>) and SVG (<see cref="SKSvgCanvas"/>),
/// all produced by the same <see cref="BarcodePainter.DrawCode"/> routine. Stateless and thread-safe.
/// </summary>
public sealed class SkiaBarcodeRenderer : IBarcodeRenderer
{
    internal const int MinSize = 16;
    internal const int MaxSize = 8192;
    internal const int MaxLogoSide = 1024;

    public Task<Result<RenderedImage>> RenderAsync(BarcodeMatrix matrix, QrStyle style, RenderOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(options);

        var error = ValidateOptions(options);
        if (error is not null) return Task.FromResult(Result<RenderedImage>.Failure(error));

        var resolved = RenderStyle.Resolve(style);
        if (resolved.IsFailure) return Task.FromResult(Result<RenderedImage>.Failure(resolved.Error));

        return Task.Run(() =>
        {
            try
            {
                return Render(matrix, resolved.Value, options, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Result<RenderedImage>.Failure(Error.Unexpected($"The code could not be rendered: {ex.Message}"));
            }
        }, cancellationToken);
    }

    private static Error? ValidateOptions(RenderOptions options)
    {
        if (options.SizePixels is < MinSize or > MaxSize)
            return Error.Validation($"The image size must be between {MinSize} and {MaxSize} pixels.");
        if (options.Dpi is < 1 or > 4800)
            return Error.Validation("The resolution must be between 1 and 4800 DPI.");
        if (!Enum.IsDefined(options.Format))
            return new Error(ErrorCodes.UnsupportedFormat, "Choose PNG, JPEG, WebP, SVG or PDF.");
        return null;
    }

    private static Result<RenderedImage> Render(BarcodeMatrix matrix, RenderStyle style, RenderOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        SKImage? logo = null;
        if (style.Logo is not null && matrix.Format.SupportsAdvancedStyling())
        {
            var loaded = LoadLogo(style.Logo);
            if (loaded.IsFailure) return loaded.Error;
            logo = loaded.Value;
        }

        using (logo)
        {
            // JPEG has no alpha channel: a transparent request becomes white rather than black.
            if (options.Format == ExportFormat.Jpeg && style.Transparent)
                style = style with { Transparent = false, Background = SKColors.White };

            var layout = CodeLayout.Compute(matrix, style, options.SizePixels);
            cancellationToken.ThrowIfCancellationRequested();

            return options.Format switch
            {
                ExportFormat.Svg => RenderSvg(matrix, style, layout, logo),
                ExportFormat.Pdf => RenderPdf(matrix, style, layout, logo, options),
                _ => RenderRaster(matrix, style, layout, logo, options)
            };
        }
    }

    private static Result<RenderedImage> RenderRaster(BarcodeMatrix matrix, RenderStyle style, CodeLayout layout, SKImage? logo, RenderOptions options)
    {
        var width = Math.Max(1, (int)MathF.Ceiling(layout.Width - 0.01f));
        var height = Math.Max(1, (int)MathF.Ceiling(layout.Height - 0.01f));

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null) return Error.Unexpected("Not enough memory to render an image this large.");

        var canvas = surface.Canvas;
        canvas.Clear(options.Format == ExportFormat.Jpeg ? SKColors.White : SKColors.Transparent);
        if (!style.Transparent)
        {
            // Cover the rounding slack of the last pixel row/column too.
            using var background = new SKPaint { Color = style.Background };
            canvas.DrawRect(SKRect.Create(width, height), background);
        }
        BarcodePainter.DrawCode(canvas, matrix, style, layout, logo);
        canvas.Flush();

        using var image = surface.Snapshot();
        using var pixmap = image.PeekPixels();
        var quality = Math.Clamp(options.Quality, 1, 100);
        using var data = options.Format switch
        {
            ExportFormat.Jpeg => pixmap.Encode(new SKJpegEncoderOptions(quality, SKJpegEncoderDownsample.Downsample444, SKJpegEncoderAlphaOption.Ignore)),
            ExportFormat.Webp => pixmap.Encode(new SKWebpEncoderOptions(quality >= 100 ? SKWebpEncoderCompression.Lossless : SKWebpEncoderCompression.Lossy, quality)),
            _ => pixmap.Encode(SKPngEncoderOptions.Default)
        };
        if (data is null) return Error.Unexpected($"The image could not be encoded as {options.Format}.");

        var bytes = options.Format switch
        {
            ExportFormat.Png => ImageMetadata.WithPngDpi(data.ToArray(), options.Dpi),
            ExportFormat.Jpeg => ImageMetadata.WithJpegDpi(data.ToArray(), options.Dpi),
            _ => data.ToArray()
        };
        return new RenderedImage(bytes, options.Format, width, height);
    }

    private static Result<RenderedImage> RenderSvg(BarcodeMatrix matrix, RenderStyle style, CodeLayout layout, SKImage? logo)
    {
        using var stream = new MemoryStream();
        using (var canvas = SKSvgCanvas.Create(SKRect.Create(layout.Width, layout.Height), stream))
        {
            BarcodePainter.DrawCode(canvas, matrix, style, layout, logo);
        }

        var svg = ImageMetadata.WithSvgViewBox(stream.ToArray(), layout.Width, layout.Height);
        return new RenderedImage(svg, ExportFormat.Svg, (int)MathF.Round(layout.Width), (int)MathF.Round(layout.Height));
    }

    private static Result<RenderedImage> RenderPdf(BarcodeMatrix matrix, RenderStyle style, CodeLayout layout, SKImage? logo, RenderOptions options)
    {
        // The code side is SizePixels / Dpi inches; PDF user space is 1/72 inch.
        var scale = 72f / options.Dpi;
        var pageWidth = layout.Width * scale;
        var pageHeight = layout.Height * scale;

        var bytes = PdfWriter.Write(matrix.Format.DisplayName(), options.Dpi, document =>
        {
            var canvas = document.BeginPage(pageWidth, pageHeight);
            canvas.Scale(scale);
            BarcodePainter.DrawCode(canvas, matrix, style, layout, logo);
            document.EndPage();
        });

        return new RenderedImage(bytes, ExportFormat.Pdf, (int)MathF.Round(pageWidth), (int)MathF.Round(pageHeight));
    }

    /// <summary>Decodes a logo (PNG/JPEG/WebP…). SVG logos are not rasterised.</summary>
    internal static Result<SKImage> LoadLogo(LogoOptions logo)
    {
        var bytes = logo.ImageBytes;
        if (bytes is null || bytes.Length == 0) return Error.Validation("The logo image is empty. Choose a PNG, JPEG or WebP file.");

        if (LooksLikeSvg(bytes))
            return new Error(ErrorCodes.UnsupportedFormat, "SVG logos aren't supported yet. Use a PNG, JPEG or WebP image.");

        using var bitmap = SkiaImageLoader.Decode(bytes, MaxLogoSide);
        if (bitmap is null)
            return new Error(ErrorCodes.UnsupportedFormat, "The logo couldn't be read. Use a PNG, JPEG or WebP image.");

        bitmap.SetImmutable();
        var image = SKImage.FromBitmap(bitmap);
        return image is null
            ? Error.Unexpected("The logo couldn't be prepared for drawing.")
            : Result<SKImage>.Success(image);
    }

    private static bool LooksLikeSvg(byte[] bytes)
    {
        var head = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 512)).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
            || (head.StartsWith('<') && head.Contains("<svg", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Writes a PDF with <see cref="SKDocument"/> into a byte array.</summary>
internal static class PdfWriter
{
    public static byte[] Write(string title, float rasterDpi, Action<SKDocument> drawPages)
    {
        using var stream = new MemoryStream();
        using (var managed = new SKManagedWStream(stream))
        {
            var metadata = new SKDocumentPdfMetadata(rasterDpi, SKDocumentPdfMetadata.DefaultEncodingQuality)
            {
                Title = title,
                Creator = "QuickQr",
                Producer = "QuickQr"
            };
            using var document = SKDocument.CreatePdf(managed, metadata)
                ?? throw new InvalidOperationException("PDF output is not available on this platform.");
            drawPages(document);
            document.Close();
        }
        return stream.ToArray();
    }
}
