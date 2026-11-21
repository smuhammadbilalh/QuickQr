using System.Globalization;
using System.Text;
using QuickQr.Core;
using QuickQr.Core.Barcodes;

namespace QuickQr.Infrastructure.Barcodes;

/// <summary>
/// Per-symbology payload rules (character set, length, check digit, QR capacity) with user-presentable messages,
/// plus normalisation of numeric retail codes (implied number system, appended check digit).
/// </summary>
internal static class BarcodePayloadRules
{
    /// <summary>Generous upper bound: no writable 2D symbology holds more than ~3,800 characters.</summary>
    private const int Max2DCharacters = 4000;

    /// <summary>Upper bound used by the ZXing 1D writers.</summary>
    private const int Max1DCharacters = 80;

    private const string Code39Characters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ -.$/+%";
    private const string CodabarBodyCharacters = "0123456789-$:/.+";
    private const string CodabarGuards = "ABCD";
    private const string QrAlphanumericCharacters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    // QR version 40 capacities: [level] = (numeric, alphanumeric, byte).
    private static readonly (int Numeric, int Alphanumeric, int Bytes)[] QrCapacity =
    [
        (7089, 4296, 2953), // Low
        (5596, 3391, 2331), // Medium
        (3993, 2420, 1663), // Quartile
        (3057, 1852, 1273)  // High
    ];

    public static Result Validate(string? payload, BarcodeFormat format, ErrorCorrectionLevel errorCorrection, string characterSet = "UTF-8")
    {
        if (!format.IsWritable())
            return Result.Failure(UnsupportedFormat(format));

        if (string.IsNullOrEmpty(payload))
            return Result.Failure(Error.Validation("Enter some content to encode."));

        return format switch
        {
            BarcodeFormat.QrCode => ValidateQrCapacity(payload, errorCorrection, characterSet),
            BarcodeFormat.DataMatrix or BarcodeFormat.Aztec or BarcodeFormat.Pdf417 => payload.Length > Max2DCharacters
                ? Result.Failure(new Error(ErrorCodes.PayloadTooLarge,
                    $"Too much content for {format.DisplayName()} ({payload.Length:N0} characters). Shorten it or use a QR code with a link instead."))
                : Result.Success(),
            BarcodeFormat.Ean8 => ValidateRetail(payload, "EAN-8", dataDigits: 7),
            BarcodeFormat.Ean13 => ValidateRetail(payload, "EAN-13", dataDigits: 12),
            BarcodeFormat.UpcA => ValidateRetail(payload, "UPC-A", dataDigits: 11),
            BarcodeFormat.UpcE => ValidateUpcE(payload),
            BarcodeFormat.Itf => ValidateItf(payload),
            BarcodeFormat.Code39 => ValidateCode39(payload),
            BarcodeFormat.Code93 => ValidateAscii(payload, "Code 93", allowFnc: false),
            BarcodeFormat.Code128 => ValidateAscii(payload, "Code 128", allowFnc: true),
            BarcodeFormat.Codabar => ValidateCodabar(payload),
            _ => Result.Failure(UnsupportedFormat(format))
        };
    }

    /// <summary>
    /// The canonical payload actually encoded: retail codes get their implied number system and check digit,
    /// everything else is returned unchanged. Call only after <see cref="Validate"/> succeeded.
    /// </summary>
    public static string Normalize(string payload, BarcodeFormat format) => format switch
    {
        BarcodeFormat.Ean8 or BarcodeFormat.Ean13 or BarcodeFormat.UpcA => WithCheckDigit(payload, format),
        BarcodeFormat.UpcE => NormalizeUpcE(payload),
        _ => payload
    };

    public static Error UnsupportedFormat(BarcodeFormat format) => new(
        ErrorCodes.UnsupportedFormat,
        format == BarcodeFormat.Unknown
            ? "Choose a barcode format."
            : $"{format.DisplayName()} codes can be scanned but not generated. Choose QR Code, Data Matrix, Aztec, PDF417 or a 1D format.");

    public static bool IsAscii(string text)
    {
        foreach (var c in text)
        {
            if (c > 127) return false;
        }
        return true;
    }

    private static Result ValidateQrCapacity(string payload, ErrorCorrectionLevel level, string characterSet)
    {
        var limits = QrCapacity[(int)level is >= 0 and <= 3 ? (int)level : 1];

        // Mirrors ZXing's mode selection: numeric ⊂ alphanumeric ⊂ byte.
        int count, limit;
        string unit;
        if (payload.All(char.IsAsciiDigit))
        {
            (count, limit, unit) = (payload.Length, limits.Numeric, "digits");
        }
        else if (payload.All(c => QrAlphanumericCharacters.Contains(c, StringComparison.Ordinal)))
        {
            (count, limit, unit) = (payload.Length, limits.Alphanumeric, "characters");
        }
        else if (IsAscii(payload))
        {
            (count, limit, unit) = (payload.Length, limits.Bytes, "bytes");
        }
        else
        {
            // Non-ASCII text is encoded in the requested character set behind a 12-bit ECI header.
            (count, limit, unit) = (ByteCount(payload, characterSet), limits.Bytes - 2, "bytes");
        }

        if (count <= limit) return Result.Success();

        var levelName = level.ToString().ToLowerInvariant();
        var hint = level == ErrorCorrectionLevel.Low
            ? "Shorten the content or put it behind a link."
            : "Shorten the content, put it behind a link, or lower the error correction level (a logo always uses High).";
        return Result.Failure(new Error(
            ErrorCodes.PayloadTooLarge,
            string.Create(CultureInfo.CurrentCulture, $"Too much content for a QR code at {levelName} error correction: {count:N0} {unit}, the maximum is {limit:N0}. {hint}")));
    }

    private static int ByteCount(string payload, string characterSet)
    {
        try
        {
            return Encoding.GetEncoding(characterSet).GetByteCount(payload);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8.GetByteCount(payload);
        }
    }

    private static Result ValidateRetail(string payload, string name, int dataDigits)
    {
        if (!payload.All(char.IsAsciiDigit))
            return Invalid($"{name} codes contain digits only.");

        if (payload.Length == dataDigits) return Result.Success();

        if (payload.Length == dataDigits + 1)
        {
            var expected = CheckDigit(payload.AsSpan(0, dataDigits));
            return payload[^1] - '0' == expected
                ? Result.Success()
                : Invalid($"The {name} check digit should be {expected}, not {payload[^1]}. Enter the first {dataDigits} digits to have it calculated.");
        }

        return Invalid($"{name} needs {dataDigits} digits (or {dataDigits + 1} including the check digit); you entered {payload.Length}.");
    }

    private static Result ValidateUpcE(string payload)
    {
        if (!payload.All(char.IsAsciiDigit))
            return Invalid("UPC-E codes contain digits only.");

        switch (payload.Length)
        {
            case 6:
                return Result.Success(); // number system 0 is implied
            case 7 or 8 when payload[0] is not ('0' or '1'):
                return Invalid("UPC-E codes start with number system 0 or 1.");
            case 7:
                return Result.Success();
            case 8:
            {
                var expected = UpcECheckDigit(payload.AsSpan(0, 7));
                return payload[7] - '0' == expected
                    ? Result.Success()
                    : Invalid($"The UPC-E check digit should be {expected}, not {payload[7]}. Enter the first 7 digits to have it calculated.");
            }
            default:
                return Invalid($"UPC-E needs 6 to 8 digits; you entered {payload.Length}.");
        }
    }

    private static Result ValidateItf(string payload)
    {
        if (!payload.All(char.IsAsciiDigit))
            return Invalid("ITF codes contain digits only.");
        if (payload.Length % 2 != 0)
            return Invalid($"ITF encodes digits in pairs, so it needs an even number of digits; you entered {payload.Length}. Add a leading 0.");
        return payload.Length > Max1DCharacters ? TooLong("ITF", payload.Length) : Result.Success();
    }

    private static Result ValidateCode39(string payload)
    {
        foreach (var c in payload)
        {
            if (!Code39Characters.Contains(c, StringComparison.Ordinal))
                return Invalid($"Code 39 supports digits, upper-case letters A–Z, space and - . $ / + % only (found '{Printable(c)}'). Use Code 128 for other characters.");
        }
        return payload.Length > Max1DCharacters ? TooLong("Code 39", payload.Length) : Result.Success();
    }

    private static Result ValidateAscii(string payload, string name, bool allowFnc)
    {
        foreach (var c in payload)
        {
            var ok = c <= 127 || (allowFnc && c is >= 'ñ' and <= 'ô');
            if (!ok)
                return Invalid($"{name} supports plain ASCII characters only (found '{Printable(c)}'). Use a QR code for accented letters, emoji or other scripts.");
        }
        return payload.Length > Max1DCharacters ? TooLong(name, payload.Length) : Result.Success();
    }

    private static Result ValidateCodabar(string payload)
    {
        var body = payload.AsSpan();
        var startsWithGuard = CodabarGuards.Contains(char.ToUpperInvariant(payload[0]), StringComparison.Ordinal);
        var endsWithGuard = CodabarGuards.Contains(char.ToUpperInvariant(payload[^1]), StringComparison.Ordinal);
        if (startsWithGuard || endsWithGuard)
        {
            if (!(startsWithGuard && endsWithGuard) || payload.Length < 3)
                return Invalid("Codabar start/stop letters (A–D) must appear at both ends, e.g. A12345B, or be left out.");
            body = body[1..^1];
        }

        foreach (var c in body)
        {
            if (!CodabarBodyCharacters.Contains(c, StringComparison.Ordinal))
                return Invalid($"Codabar supports digits and - $ : / . + only (found '{Printable(c)}').");
        }
        return payload.Length > Max1DCharacters ? TooLong("Codabar", payload.Length) : Result.Success();
    }

    private static string WithCheckDigit(string payload, BarcodeFormat format)
    {
        var dataDigits = format switch
        {
            BarcodeFormat.Ean8 => 7,
            BarcodeFormat.Ean13 => 12,
            _ => 11
        };
        return payload.Length == dataDigits
            ? payload + (char)('0' + CheckDigit(payload))
            : payload;
    }

    private static string NormalizeUpcE(string payload)
    {
        var withSystem = payload.Length == 6 ? "0" + payload : payload;
        return withSystem.Length == 7
            ? withSystem + (char)('0' + UpcECheckDigit(withSystem))
            : withSystem;
    }

    /// <summary>GS1 mod-10 check digit: weights 3,1,3,… from the right-most data digit.</summary>
    internal static int CheckDigit(ReadOnlySpan<char> digits)
    {
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var weight = (digits.Length - i) % 2 == 1 ? 3 : 1;
            sum += (digits[i] - '0') * weight;
        }
        return (10 - (sum % 10)) % 10;
    }

    /// <summary>UPC-E check digit: computed on the expanded UPC-A form of number system + 6 digits.</summary>
    internal static int UpcECheckDigit(ReadOnlySpan<char> upce7) => CheckDigit(ExpandUpcE(upce7));

    internal static string ExpandUpcE(ReadOnlySpan<char> upce7)
    {
        var d = upce7[1..7];
        var sb = new StringBuilder(11);
        sb.Append(upce7[0]);
        switch (d[5])
        {
            case '0' or '1' or '2':
                sb.Append(d[..2]).Append(d[5]).Append("0000").Append(d[2..5]);
                break;
            case '3':
                sb.Append(d[..3]).Append("00000").Append(d[3..5]);
                break;
            case '4':
                sb.Append(d[..4]).Append("00000").Append(d[4]);
                break;
            default:
                sb.Append(d[..5]).Append("0000").Append(d[5]);
                break;
        }
        return sb.ToString();
    }

    private static Result Invalid(string message) => Result.Failure(Error.Validation(message));

    private static Result TooLong(string name, int length) => Result.Failure(new Error(
        ErrorCodes.PayloadTooLarge,
        $"{name} holds at most {Max1DCharacters} characters; you entered {length}. Use a QR code for longer content."));

    private static string Printable(char c) => char.IsControl(c) ? $"U+{(int)c:X4}" : c.ToString();
}
