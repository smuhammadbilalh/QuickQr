using QuickQr.Codecs.Tests.TestSupport;
using QuickQr.Core;
using QuickQr.Core.Barcodes;
using QuickQr.Infrastructure.Barcodes;

namespace QuickQr.Codecs.Tests.Barcodes;

public class ZXingBarcodeEncoderTests
{
    private readonly ZXingBarcodeEncoder _encoder = new();

    public static TheoryData<BarcodeFormat, string> ValidPayloads => new()
    {
        { BarcodeFormat.QrCode, "https://example.com/?q=Grüße ✓" },
        { BarcodeFormat.DataMatrix, "DM-0001" },
        { BarcodeFormat.Aztec, "Boarding pass 42" },
        { BarcodeFormat.Pdf417, "PDF417 payload" },
        { BarcodeFormat.Ean8, "9638507" },
        { BarcodeFormat.Ean8, "96385074" },
        { BarcodeFormat.Ean13, "590123412345" },
        { BarcodeFormat.Ean13, "5901234123457" },
        { BarcodeFormat.UpcA, "03600029145" },
        { BarcodeFormat.UpcA, "036000291452" },
        { BarcodeFormat.UpcE, "123456" },
        { BarcodeFormat.UpcE, "0123456" },
        { BarcodeFormat.UpcE, "01234565" },
        { BarcodeFormat.Itf, "10012345678902" },
        { BarcodeFormat.Code39, "CODE-39 $/+%" },
        { BarcodeFormat.Code93, "Code 93 lower" },
        { BarcodeFormat.Code128, "Code128 ~!@#" },
        { BarcodeFormat.Codabar, "A40156B" },
        { BarcodeFormat.Codabar, "31117013206375" }
    };

    public static TheoryData<BarcodeFormat, string> InvalidPayloads => new()
    {
        { BarcodeFormat.Ean8, "123456" },          // too short
        { BarcodeFormat.Ean8, "96385075" },        // bad check digit
        { BarcodeFormat.Ean8, "12345a7" },         // not digits
        { BarcodeFormat.Ean13, "12345678901" },    // 11 digits
        { BarcodeFormat.Ean13, "5901234123458" },  // bad check digit
        { BarcodeFormat.UpcA, "0360002914" },      // 10 digits
        { BarcodeFormat.UpcA, "036000291453" },    // bad check digit
        { BarcodeFormat.UpcE, "12345" },           // too short
        { BarcodeFormat.UpcE, "2123456" },         // number system must be 0/1
        { BarcodeFormat.UpcE, "01234567" },        // bad check digit
        { BarcodeFormat.UpcE, "012345678" },       // too long
        { BarcodeFormat.Itf, "12345" },            // odd digit count
        { BarcodeFormat.Itf, "12AB" },             // not digits
        { BarcodeFormat.Code39, "lower case" },    // outside Code 39 charset
        { BarcodeFormat.Code39, "A*B" },           // '*' is the start/stop character
        { BarcodeFormat.Code93, "naïve" },         // not ASCII
        { BarcodeFormat.Code128, "€uro" },         // not ASCII
        { BarcodeFormat.Codabar, "A1234" },        // start without stop
        { BarcodeFormat.Codabar, "12X34" },        // invalid body character
        { BarcodeFormat.QrCode, "" }               // empty
    };

    [Theory]
    [MemberData(nameof(ValidPayloads))]
    public void Valid_payloads_validate_and_encode(BarcodeFormat format, string payload)
    {
        _encoder.Validate(payload, format).IsSuccess.Should().BeTrue();

        var result = _encoder.Encode(new BarcodeEncodeRequest(payload, format));

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        result.Value.Format.Should().Be(format);
        if (format.IsTwoDimensional()) result.Value.Height.Should().BeGreaterThan(1);
        else result.Value.Height.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public void Invalid_payloads_fail_validation_with_a_message(BarcodeFormat format, string payload)
    {
        var validation = _encoder.Validate(payload, format);
        var encoded = _encoder.Encode(new BarcodeEncodeRequest(payload, format));

        validation.IsFailure.Should().BeTrue();
        validation.Error.Code.Should().Be(ErrorCodes.Validation);
        validation.Error.Message.Should().NotBeNullOrWhiteSpace();
        encoded.IsFailure.Should().BeTrue();
        encoded.Error.Code.Should().Be(ErrorCodes.Validation);
    }

    [Fact]
    public void Wrong_check_digit_message_names_the_expected_digit()
    {
        var result = _encoder.Validate("5901234123458", BarcodeFormat.Ean13);

        result.Error.Message.Should().Contain("should be 7");
    }

    [Theory]
    [InlineData(BarcodeFormat.Ean13, "590123412345", "5901234123457")]
    [InlineData(BarcodeFormat.Ean8, "9638507", "96385074")]
    [InlineData(BarcodeFormat.UpcA, "03600029145", "036000291452")]
    [InlineData(BarcodeFormat.UpcE, "123456", "01234565")]
    [InlineData(BarcodeFormat.UpcE, "0123456", "01234565")]
    public void Retail_codes_get_their_check_digit_in_the_payload(BarcodeFormat format, string input, string expected)
    {
        var matrix = _encoder.Encode(new BarcodeEncodeRequest(input, format)).Value;

        matrix.Payload.Should().Be(expected);
    }

    [Fact]
    public void Qr_matrix_has_one_cell_per_module_and_no_quiet_zone()
    {
        var matrix = _encoder.Encode(new BarcodeEncodeRequest("HELLO", BarcodeFormat.QrCode, ErrorCorrectionLevel.Medium)).Value;

        matrix.Width.Should().Be(21);
        matrix.Height.Should().Be(21);
        matrix.Payload.Should().Be("HELLO");
        // Finder pattern starts at the very edge: dark corner, light separator.
        matrix[0, 0].Should().BeTrue();
        matrix[6, 6].Should().BeTrue();
        matrix[7, 0].Should().BeFalse();
        matrix.IsFinderPattern(3, 3).Should().BeTrue();
    }

    [Fact]
    public void One_dimensional_codes_are_a_single_row_starting_and_ending_with_a_bar()
    {
        var matrix = _encoder.Encode(new BarcodeEncodeRequest("5901234123457", BarcodeFormat.Ean13)).Value;

        matrix.Height.Should().Be(1);
        matrix.Width.Should().Be(95); // 3 + 42 + 5 + 42 + 3 modules
        matrix[0, 0].Should().BeTrue();
        matrix[matrix.Width - 1, 0].Should().BeTrue();
    }

    [Fact]
    public void Data_matrix_symbols_are_square()
    {
        var matrix = _encoder.Encode(new BarcodeEncodeRequest("HELLO WORLD", BarcodeFormat.DataMatrix)).Value;

        matrix.Width.Should().Be(matrix.Height);
    }

    [Fact]
    public void Higher_error_correction_produces_a_larger_symbol()
    {
        const string payload = "https://example.com/products/12345?utm_source=print";
        var low = _encoder.Encode(new BarcodeEncodeRequest(payload, BarcodeFormat.QrCode, ErrorCorrectionLevel.Low)).Value;
        var high = _encoder.Encode(new BarcodeEncodeRequest(payload, BarcodeFormat.QrCode, ErrorCorrectionLevel.High)).Value;

        high.Width.Should().BeGreaterThan(low.Width);
    }

    [Theory]
    [InlineData(ErrorCorrectionLevel.Low, 2953)]
    [InlineData(ErrorCorrectionLevel.Medium, 2331)]
    [InlineData(ErrorCorrectionLevel.Quartile, 1663)]
    [InlineData(ErrorCorrectionLevel.High, 1273)]
    public void Qr_byte_capacity_is_enforced_per_level(ErrorCorrectionLevel level, int capacity)
    {
        var atLimit = new string('a', capacity);
        var overLimit = new string('a', capacity + 1);

        _encoder.Encode(new BarcodeEncodeRequest(atLimit, BarcodeFormat.QrCode, level)).IsSuccess.Should().BeTrue();

        var result = _encoder.Encode(new BarcodeEncodeRequest(overLimit, BarcodeFormat.QrCode, level));
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.PayloadTooLarge);
        result.Error.Message.Should().Contain("maximum").And.Contain("Shorten");
    }

    [Fact]
    public void Numeric_qr_content_uses_the_numeric_capacity()
    {
        var digits = new string('7', 7089);

        _encoder.Encode(new BarcodeEncodeRequest(digits, BarcodeFormat.QrCode, ErrorCorrectionLevel.Low)).IsSuccess.Should().BeTrue();
        _encoder.Validate(digits + "7", BarcodeFormat.QrCode).Error.Code.Should().Be(ErrorCodes.PayloadTooLarge);
    }

    [Fact]
    public void Non_ascii_content_near_the_limit_reports_payload_too_large()
    {
        var text = new string('é', 1500); // 3,000 UTF-8 bytes

        var result = _encoder.Encode(new BarcodeEncodeRequest(text, BarcodeFormat.QrCode, ErrorCorrectionLevel.Low));

        result.Error.Code.Should().Be(ErrorCodes.PayloadTooLarge);
    }

    [Theory]
    [InlineData(BarcodeFormat.DataMatrix)]
    [InlineData(BarcodeFormat.Aztec)]
    [InlineData(BarcodeFormat.Pdf417)]
    public void Oversized_2d_content_reports_payload_too_large(BarcodeFormat format)
    {
        var result = _encoder.Encode(new BarcodeEncodeRequest(new string('x', 3500), format));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.PayloadTooLarge);
    }

    [Theory]
    [InlineData(BarcodeFormat.MaxiCode)]
    [InlineData(BarcodeFormat.Rss14)]
    [InlineData(BarcodeFormat.RssExpanded)]
    [InlineData(BarcodeFormat.Unknown)]
    public void Read_only_formats_are_unsupported(BarcodeFormat format)
    {
        _encoder.Validate("12345", format).Error.Code.Should().Be(ErrorCodes.UnsupportedFormat);
        _encoder.Encode(new BarcodeEncodeRequest("12345", format)).Error.Code.Should().Be(ErrorCodes.UnsupportedFormat);
    }

    [Fact]
    public void Too_long_1d_content_is_rejected()
    {
        var result = _encoder.Encode(new BarcodeEncodeRequest(new string('A', 81), BarcodeFormat.Code128));

        result.Error.Code.Should().Be(ErrorCodes.PayloadTooLarge);
    }

    [Fact]
    public async Task Unicode_qr_content_round_trips_through_the_decoder()
    {
        const string payload = "Grüße aus Köln — 你好 👋";
        var matrix = Codec.Encode(payload);
        var image = await Codec.RenderAsync(matrix);

        var decoded = await Codec.DecodeAsync(image.Data, Codec.Only(BarcodeFormat.QrCode));

        decoded.Should().ContainSingle().Which.Text.Should().Be(payload);
    }
}
