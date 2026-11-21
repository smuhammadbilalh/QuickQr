using QuickQr.Core.Barcodes;
using QuickQr.Core.Rendering;

namespace QuickQr.Core.Batch;

/// <param name="FileName">File name without extension; sanitised by the generator.</param>
public sealed record BatchItem(string Payload, string FileName);

public sealed record BatchParseOptions(
    bool HasHeaderRow = true,
    char? Delimiter = null,      // null = auto-detect (',', ';', '\t')
    int PayloadColumn = 0,
    int? FileNameColumn = 1,
    int MaxItems = 5000);

public sealed record BatchParseResult(IReadOnlyList<BatchItem> Items, IReadOnlyList<string> Warnings);

/// <summary>Parses CSV or newline-separated text into batch items (RFC 4180 quoting supported).</summary>
public interface IBatchInputParser
{
    BatchParseResult Parse(string text, BatchParseOptions? options = null);
}

public sealed record BatchRequest(
    IReadOnlyList<BatchItem> Items,
    BarcodeFormat Format,
    ErrorCorrectionLevel ErrorCorrection,
    QrStyle Style,
    RenderOptions RenderOptions);

public sealed record BatchProgress(int Completed, int Total, string? CurrentItem);

public sealed record BatchFailure(int Index, string FileName, string Reason);

public sealed record BatchResult(int Succeeded, IReadOnlyList<BatchFailure> Failures);

/// <summary>Generates many codes and writes them into a ZIP archive (plus a manifest.csv).</summary>
public interface IBatchGenerator
{
    /// <summary>Requires <see cref="Licensing.PremiumFeature.BatchGeneration"/>.</summary>
    Task<Result<BatchResult>> GenerateZipAsync(
        BatchRequest request,
        Stream output,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public enum PaperSize { A4 = 0, Letter = 1 }

public sealed record PrintSheetRequest(
    IReadOnlyList<BatchItem> Items,
    BarcodeFormat Format,
    ErrorCorrectionLevel ErrorCorrection,
    QrStyle Style,
    PaperSize Paper = PaperSize.A4,
    int Columns = 4,
    int Rows = 6,
    bool PrintLabels = true);

/// <summary>Lays codes out on printable label sheets (PDF).</summary>
public interface IPrintSheetGenerator
{
    /// <summary>Requires <see cref="Licensing.PremiumFeature.PrintSheets"/>.</summary>
    Task<Result> GeneratePdfAsync(PrintSheetRequest request, Stream output, CancellationToken cancellationToken = default);
}
