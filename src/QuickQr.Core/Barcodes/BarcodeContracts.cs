namespace QuickQr.Core.Barcodes;

/// <summary>Symbologies QuickQr understands. Values are persisted (history) — append only, never renumber.</summary>
public enum BarcodeFormat
{
    Unknown = 0,
    QrCode = 1,
    DataMatrix = 2,
    Aztec = 3,
    Pdf417 = 4,
    Code128 = 5,
    Code39 = 6,
    Code93 = 7,
    Ean8 = 8,
    Ean13 = 9,
    UpcA = 10,
    UpcE = 11,
    Itf = 12,
    Codabar = 13,
    MaxiCode = 14,   // read-only
    Rss14 = 15,      // read-only (GS1 DataBar)
    RssExpanded = 16 // read-only (GS1 DataBar Expanded)
}

/// <summary>QR error-correction level. Higher levels survive more damage (and logos) but hold less data.</summary>
public enum ErrorCorrectionLevel
{
    Low = 0,      // ~7 %
    Medium = 1,   // ~15 %
    Quartile = 2, // ~25 %
    High = 3      // ~30 %
}

public static class BarcodeFormatExtensions
{
    /// <summary>Formats QuickQr can generate.</summary>
    public static IReadOnlyList<BarcodeFormat> Writable { get; } =
    [
        BarcodeFormat.QrCode, BarcodeFormat.DataMatrix, BarcodeFormat.Aztec, BarcodeFormat.Pdf417,
        BarcodeFormat.Code128, BarcodeFormat.Code39, BarcodeFormat.Code93, BarcodeFormat.Ean8,
        BarcodeFormat.Ean13, BarcodeFormat.UpcA, BarcodeFormat.UpcE, BarcodeFormat.Itf, BarcodeFormat.Codabar
    ];

    public static bool IsTwoDimensional(this BarcodeFormat format) => format is
        BarcodeFormat.QrCode or BarcodeFormat.DataMatrix or BarcodeFormat.Aztec or
        BarcodeFormat.Pdf417 or BarcodeFormat.MaxiCode;

    /// <summary>Only QR codes support the full styling engine (shapes, eyes, logo).</summary>
    public static bool SupportsAdvancedStyling(this BarcodeFormat format) => format == BarcodeFormat.QrCode;

    public static bool IsWritable(this BarcodeFormat format) => Writable.Contains(format);

    public static string DisplayName(this BarcodeFormat format) => format switch
    {
        BarcodeFormat.QrCode => "QR Code",
        BarcodeFormat.DataMatrix => "Data Matrix",
        BarcodeFormat.Aztec => "Aztec",
        BarcodeFormat.Pdf417 => "PDF417",
        BarcodeFormat.Code128 => "Code 128",
        BarcodeFormat.Code39 => "Code 39",
        BarcodeFormat.Code93 => "Code 93",
        BarcodeFormat.Ean8 => "EAN-8",
        BarcodeFormat.Ean13 => "EAN-13",
        BarcodeFormat.UpcA => "UPC-A",
        BarcodeFormat.UpcE => "UPC-E",
        BarcodeFormat.Itf => "ITF",
        BarcodeFormat.Codabar => "Codabar",
        BarcodeFormat.MaxiCode => "MaxiCode",
        BarcodeFormat.Rss14 => "GS1 DataBar",
        BarcodeFormat.RssExpanded => "GS1 DataBar Expanded",
        _ => "Unknown"
    };
}

/// <summary>
/// A barcode at module resolution: one cell per module, no quiet zone.
/// For 1D symbologies <see cref="Height"/> is 1 and the renderer stretches bars vertically.
/// </summary>
public sealed class BarcodeMatrix
{
    private readonly bool[] _modules;

    public BarcodeMatrix(int width, int height, BarcodeFormat format)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        Format = format;
        _modules = new bool[width * height];
    }

    public int Width { get; }
    public int Height { get; }
    public BarcodeFormat Format { get; }

    /// <summary>The payload that produced this matrix (used for human-readable text under 1D codes).</summary>
    public string Payload { get; init; } = string.Empty;

    /// <summary>True when the module at (x, y) is dark.</summary>
    public bool this[int x, int y]
    {
        get => _modules[(y * Width) + x];
        set => _modules[(y * Width) + x] = value;
    }

    /// <summary>
    /// True when (x, y) belongs to one of the three 7×7 QR finder patterns ("eyes").
    /// Always false for non-QR formats.
    /// </summary>
    public bool IsFinderPattern(int x, int y)
    {
        if (Format != BarcodeFormat.QrCode) return false;
        const int size = 7;
        var inLeft = x < size;
        var inTop = y < size;
        var inRight = x >= Width - size;
        var inBottom = y >= Height - size;
        return (inLeft && inTop) || (inRight && inTop) || (inLeft && inBottom);
    }
}

public sealed record BarcodeEncodeRequest(
    string Payload,
    BarcodeFormat Format = BarcodeFormat.QrCode,
    ErrorCorrectionLevel ErrorCorrection = ErrorCorrectionLevel.Medium,
    string CharacterSet = "UTF-8");

/// <summary>Turns a payload string into a module matrix for the requested symbology.</summary>
public interface IBarcodeEncoder
{
    /// <summary>Validates <paramref name="request"/> against the symbology's rules (charset, length, check digit) and encodes it.</summary>
    Result<BarcodeMatrix> Encode(BarcodeEncodeRequest request);

    /// <summary>Validates without encoding. Returns a failure with a user-presentable message when invalid.</summary>
    Result Validate(string payload, BarcodeFormat format);
}

public sealed record DecodedBarcode(string Text, BarcodeFormat Format, byte[]? RawBytes = null);

public sealed record DecodeOptions(
    bool TryHarder = true,
    bool TryInverted = true,
    bool DetectMultiple = true,
    IReadOnlyCollection<BarcodeFormat>? Formats = null);

/// <summary>Decodes barcodes from a still image (gallery, file, screenshot, clipboard).</summary>
public interface IBarcodeImageDecoder
{
    /// <summary>Returns every barcode found; an empty list when none were found.</summary>
    Task<IReadOnlyList<DecodedBarcode>> DecodeAsync(Stream image, DecodeOptions? options = null, CancellationToken cancellationToken = default);
}
