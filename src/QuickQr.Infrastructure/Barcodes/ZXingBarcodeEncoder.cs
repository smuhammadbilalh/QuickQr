using QuickQr.Core;
using QuickQr.Core.Barcodes;
using ZXing.Common;
using ZXing.Datamatrix.Encoder;
using EncodeHintType = ZXing.EncodeHintType;
using MultiFormatWriter = ZXing.MultiFormatWriter;
using QrEcl = ZXing.QrCode.Internal.ErrorCorrectionLevel;
using QrEncoder = ZXing.QrCode.Internal.Encoder;
using WriterException = ZXing.WriterException;

namespace QuickQr.Infrastructure.Barcodes;

/// <summary>
/// ZXing.Net-backed encoder. Produces matrices at one cell per module with no quiet zone
/// (2D codes at native size, 1D codes as a single row of bars); the renderer adds quiet zones and styling.
/// Stateless and thread-safe.
/// </summary>
public sealed class ZXingBarcodeEncoder : IBarcodeEncoder
{
    public Result<BarcodeMatrix> Encode(BarcodeEncodeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var characterSet = string.IsNullOrWhiteSpace(request.CharacterSet) ? "UTF-8" : request.CharacterSet;
        var validation = BarcodePayloadRules.Validate(request.Payload, request.Format, request.ErrorCorrection, characterSet);
        if (validation.IsFailure) return validation.Error;

        var payload = BarcodePayloadRules.Normalize(request.Payload, request.Format);
        var needsCharacterSet = !BarcodePayloadRules.IsAscii(payload);

        try
        {
            var matrix = request.Format switch
            {
                BarcodeFormat.QrCode => EncodeQr(payload, request.ErrorCorrection, needsCharacterSet ? characterSet : null),
                _ when request.Format.IsTwoDimensional() => Encode2D(payload, request.Format, request.ErrorCorrection, needsCharacterSet ? characterSet : null),
                _ => Encode1D(payload, request.Format)
            };
            return matrix;
        }
        catch (Exception ex) when (ex is WriterException or ArgumentException)
        {
            // ZXing reports capacity problems through both exception types.
            return MapWriterError(request.Format, ex.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IndexOutOfRangeException or FormatException or NotSupportedException)
        {
            return Error.Validation($"This content can't be encoded as {request.Format.DisplayName()}: {ex.Message}");
        }
    }

    /// <summary>Validates at the most permissive error-correction level; <see cref="Encode"/> re-checks at the requested level.</summary>
    public Result Validate(string payload, BarcodeFormat format) =>
        BarcodePayloadRules.Validate(payload, format, ErrorCorrectionLevel.Low);

    private static BarcodeMatrix EncodeQr(string payload, ErrorCorrectionLevel level, string? characterSet)
    {
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.ERROR_CORRECTION] = level switch
            {
                ErrorCorrectionLevel.Low => QrEcl.L,
                ErrorCorrectionLevel.Quartile => QrEcl.Q,
                ErrorCorrectionLevel.High => QrEcl.H,
                _ => QrEcl.M
            }
        };
        if (characterSet is not null) hints[EncodeHintType.CHARACTER_SET] = characterSet;

        // The low-level encoder yields the bare symbol: exactly one cell per module, no quiet zone.
        var code = QrEncoder.encode(payload, (QrEcl)hints[EncodeHintType.ERROR_CORRECTION], hints);
        var source = code.Matrix;
        var matrix = new BarcodeMatrix(source.Width, source.Height, BarcodeFormat.QrCode) { Payload = payload };
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
                matrix[x, y] = source[x, y] == 1;
        }
        return matrix;
    }

    private static BarcodeMatrix Encode2D(string payload, BarcodeFormat format, ErrorCorrectionLevel level, string? characterSet)
    {
        var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 0 };
        switch (format)
        {
            case BarcodeFormat.DataMatrix:
                // ECC 200 has fixed error correction; square symbols match user expectations (and the preview).
                hints[EncodeHintType.DATA_MATRIX_SHAPE] = SymbolShapeHint.FORCE_SQUARE;
                if (characterSet is not null)
                {
                    hints[EncodeHintType.CHARACTER_SET] = characterSet;
                    hints[EncodeHintType.DATA_MATRIX_COMPACT] = true; // minimal encoder with ECI support
                }
                break;
            case BarcodeFormat.Aztec:
                // Percentage of the symbol devoted to error correction (ISO minimum recommendation is 23 %).
                hints[EncodeHintType.ERROR_CORRECTION] = level switch
                {
                    ErrorCorrectionLevel.Low => 15,
                    ErrorCorrectionLevel.Quartile => 36,
                    ErrorCorrectionLevel.High => 50,
                    _ => 23
                };
                if (characterSet is not null) hints[EncodeHintType.CHARACTER_SET] = characterSet;
                break;
            case BarcodeFormat.Pdf417:
                // PDF417 security levels 0–8; 2–5 cover the recommended range for typical payloads.
                hints[EncodeHintType.ERROR_CORRECTION] = level switch
                {
                    ErrorCorrectionLevel.Low => 2,
                    ErrorCorrectionLevel.Quartile => 4,
                    ErrorCorrectionLevel.High => 5,
                    _ => 3
                };
                if (characterSet is not null) hints[EncodeHintType.CHARACTER_SET] = characterSet;
                break;
        }

        var bits = new MultiFormatWriter().encode(payload, ZXingFormatMap.ToZXing(format)!.Value, 0, 0, hints);
        return FromBitMatrix(bits, format, payload);
    }

    private static BarcodeMatrix Encode1D(string payload, BarcodeFormat format)
    {
        var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 0 };
        var bits = new MultiFormatWriter().encode(payload, ZXingFormatMap.ToZXing(format)!.Value, 0, 0, hints);

        // ZXing renders 1D codes as a 1-pixel-high row; trim any padding the writer may have added.
        var first = -1;
        var last = -1;
        for (var x = 0; x < bits.Width; x++)
        {
            if (!bits[x, 0]) continue;
            if (first < 0) first = x;
            last = x;
        }
        if (first < 0) throw new WriterException("The encoder produced an empty barcode.");

        var matrix = new BarcodeMatrix(last - first + 1, 1, format) { Payload = payload };
        for (var x = first; x <= last; x++)
            matrix[x - first, 0] = bits[x, 0];
        return matrix;
    }

    private static BarcodeMatrix FromBitMatrix(BitMatrix bits, BarcodeFormat format, string payload)
    {
        var matrix = new BarcodeMatrix(bits.Width, bits.Height, format) { Payload = payload };
        for (var y = 0; y < bits.Height; y++)
        {
            for (var x = 0; x < bits.Width; x++)
                matrix[x, y] = bits[x, y];
        }
        return matrix;
    }

    private static Error MapWriterError(BarcodeFormat format, string message)
    {
        string[] capacityHints = ["too big", "too large", "to large", "larger", "symbol arrangement", "more than", "capacity", "exceed"];
        var isCapacity = capacityHints.Any(h => message.Contains(h, StringComparison.OrdinalIgnoreCase));
        return isCapacity
            ? new Error(ErrorCodes.PayloadTooLarge,
                $"Too much content for {format.DisplayName()}. Shorten it, put it behind a link, or lower the error correction level.")
            : Error.Validation($"This content can't be encoded as {format.DisplayName()}: {message}");
    }
}
