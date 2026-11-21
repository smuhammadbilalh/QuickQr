using QuickQr.Core;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Batch;
using QuickQr.Core.Licensing;
using SkiaSharp;

namespace QuickQr.Infrastructure.Rendering;

/// <summary>
/// Lays codes out on A4/Letter label sheets as a vector PDF (Business tier). Items that cannot be encoded get a
/// placeholder instead of failing the whole sheet.
/// </summary>
public sealed class PdfPrintSheetGenerator(IBarcodeEncoder encoder, IEntitlementService entitlements) : IPrintSheetGenerator
{
    internal const float PageMargin = 36f;   // 0.5 in
    internal const float Gutter = 8f;
    internal const int MaxColumns = 12;
    internal const int MaxRows = 20;

    public async Task<Result> GeneratePdfAsync(PrintSheetRequest request, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(output);

        if (!entitlements.HasFeature(PremiumFeature.PrintSheets))
        {
            var feature = FeatureCatalog.Get(PremiumFeature.PrintSheets);
            return Result.Failure(Error.FeatureLocked($"{feature.Name} require the {feature.RequiredTier} plan."));
        }

        if (request.Items is not { Count: > 0 }) return Result.Failure(Error.Validation("Add at least one item to print."));
        if (request.Columns is < 1 or > MaxColumns) return Result.Failure(Error.Validation($"Use between 1 and {MaxColumns} columns."));
        if (request.Rows is < 1 or > MaxRows) return Result.Failure(Error.Validation($"Use between 1 and {MaxRows} rows."));
        if (!request.Format.IsWritable()) return Result.Failure(new Error(ErrorCodes.UnsupportedFormat, $"{request.Format.DisplayName()} codes can't be generated."));
        if (!output.CanWrite) throw new ArgumentException("The output stream must be writable.", nameof(output));

        var style = RenderStyle.Resolve(request.Style);
        if (style.IsFailure) return Result.Failure(style.Error);

        var pdf = await Task.Run(() => Build(request, style.Value, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (pdf.IsFailure) return Result.Failure(pdf.Error);

        await output.WriteAsync(pdf.Value, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private Result<byte[]> Build(PrintSheetRequest request, RenderStyle style, CancellationToken cancellationToken)
    {
        SKImage? logo = null;
        if (style.Logo is not null && request.Format.SupportsAdvancedStyling())
        {
            var loaded = SkiaBarcodeRenderer.LoadLogo(style.Logo);
            if (loaded.IsFailure) return loaded.Error;
            logo = loaded.Value;
        }

        // A centre logo hides modules; maximum error correction keeps the codes readable (as for single exports).
        var ecl = logo is not null ? ErrorCorrectionLevel.High : request.ErrorCorrection;
        var (pageWidth, pageHeight) = request.Paper == PaperSize.Letter ? (612f, 792f) : (595f, 842f);
        var perPage = request.Columns * request.Rows;
        var cellWidth = (pageWidth - (2 * PageMargin) - ((request.Columns - 1) * Gutter)) / request.Columns;
        var cellHeight = (pageHeight - (2 * PageMargin) - ((request.Rows - 1) * Gutter)) / request.Rows;
        var labelHeight = request.PrintLabels ? Math.Clamp(cellHeight * 0.12f, 6f, 12f) : 0f;
        var codeBoxHeight = cellHeight - labelHeight;

        using (logo)
        {
            try
            {
                // Skia snaps the page size to the raster-DPI grid; 72 keeps A4/Letter exact (codes stay vector anyway).
                var bytes = PdfWriter.Write("QuickQr print sheet", SKDocument.DefaultRasterDpi, document =>
                {
                    for (var start = 0; start < request.Items.Count; start += perPage)
                    {
                        var canvas = document.BeginPage(pageWidth, pageHeight);
                        var count = Math.Min(perPage, request.Items.Count - start);
                        for (var i = 0; i < count; i++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var item = request.Items[start + i];
                            var cell = SKRect.Create(
                                PageMargin + ((i % request.Columns) * (cellWidth + Gutter)),
                                PageMargin + ((i / request.Columns) * (cellHeight + Gutter)),
                                cellWidth,
                                cellHeight);
                            DrawItem(canvas, item, request.Format, ecl, style, logo, new SKRect(cell.Left, cell.Top, cell.Right, cell.Top + codeBoxHeight));
                            if (labelHeight > 0) DrawLabel(canvas, item.FileName, new SKRect(cell.Left, cell.Bottom - labelHeight, cell.Right, cell.Bottom));
                        }
                        document.EndPage();
                    }
                });
                return bytes;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Error.Unexpected($"The print sheet could not be created: {ex.Message}");
            }
        }
    }

    private void DrawItem(SKCanvas canvas, BatchItem item, BarcodeFormat format, ErrorCorrectionLevel ecl, RenderStyle style, SKImage? logo, SKRect box)
    {
        var encoded = item is null
            ? Result<BarcodeMatrix>.Failure(Error.Validation("Missing item."))
            : encoder.Encode(new BarcodeEncodeRequest(item.Payload ?? string.Empty, format, ecl));

        if (encoded.IsFailure)
        {
            BarcodePainter.DrawPlaceholder(canvas, Inset(box, box.Height * 0.04f), "Could not encode");
            return;
        }

        var matrix = encoded.Value;
        var layout = CodeLayout.Fit(matrix, style, box.Width, box.Height);
        canvas.Save();
        canvas.Translate(box.MidX - (layout.Width / 2f), box.MidY - (layout.Height / 2f));
        BarcodePainter.DrawCode(canvas, matrix, style, layout, logo);
        canvas.Restore();
    }

    private static void DrawLabel(SKCanvas canvas, string? text, SKRect box)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(0x30, 0x30, 0x30) };
        using var font = SkiaFonts.Create(box.Height * 0.8f);
        var label = SkiaFonts.Ellipsize(font, text.Trim(), box.Width);
        canvas.DrawText(label, box.MidX, SkiaFonts.CenteredBaseline(font, box), SKTextAlign.Center, font, paint);
    }

    private static SKRect Inset(SKRect rect, float by) => new(rect.Left + by, rect.Top + by, rect.Right - by, rect.Bottom - by);
}
