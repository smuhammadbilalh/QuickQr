using QuickQr.Core.Content.Internal;
using QuickQr.Core.Security;

namespace QuickQr.Core.Content;

/// <summary>
/// Validates typed content and serialises it into the payload format scanners understand
/// (Wi-Fi config, vCard 3.0, RFC 6068 mailto, iCalendar, BIP21/EIP-681, EPC069-12, UPI, ...).
/// </summary>
public sealed class ContentEncoder : IContentEncoder
{
    public Result<string> Encode(QrContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        Error? error = content switch
        {
            TextContent c => string.IsNullOrWhiteSpace(c.Text) ? Error.Validation("Enter some text.") : null,
            UrlContent c => UrlFormat.Validate(c),
            WifiContent c => WifiFormat.Validate(c),
            ContactContent c => VCardFormat.Validate(c),
            MeCardContent c => MeCardFormat.Validate(c),
            EmailContent c => EmailFormat.Validate(c),
            SmsContent c => TelephonyFormat.ValidateNumber(c.Number),
            PhoneContent c => TelephonyFormat.ValidateNumber(c.Number),
            GeoContent c => GeoFormat.Validate(c),
            CalendarEventContent c => CalendarFormat.Validate(c),
            WhatsAppContent c => MessengerFormat.ValidateWhatsApp(c),
            TelegramContent c => MessengerFormat.ValidateTelegram(c),
            CryptoPaymentContent c => CryptoFormat.Validate(c),
            EpcPaymentContent c => EpcFormat.Validate(c),
            UpiPaymentContent c => UpiFormat.Validate(c),
            SecureMessageContent c => ValidateEnvelope(c),
            AppStoreContent c => AppStoreFormat.Validate(c),
            SocialProfileContent c => SocialFormat.Validate(c),
            ProductContent c => ValidateProduct(c),
            _ => Error.Validation("This kind of content isn't supported."),
        };

        return error is null
            ? Result<string>.Success(PayloadWriter.Write(content))
            : Result<string>.Failure(error);
    }

    private static Error? ValidateEnvelope(SecureMessageContent content)
    {
        var envelope = (content.Envelope ?? string.Empty).Trim();
        return envelope.StartsWith(ISecurePayloadProtector.EnvelopePrefix, StringComparison.Ordinal) &&
               envelope.Length > ISecurePayloadProtector.EnvelopePrefix.Length
            ? null
            : Error.Validation("This isn't a QuickQr secure message. Encrypt the text first.");
    }

    private static Error? ValidateProduct(ProductContent content)
    {
        var code = PayloadWriter.ProductCode(content.Code);
        if (code.Length == 0) return Error.Validation("Enter the product barcode number.");
        if (!Validators.IsAllDigits(code)) return Error.Validation("A product code contains digits only.");
        if (code.Length is not (8 or 12 or 13)) return Error.Validation("Product codes have 8 (EAN-8/UPC-E), 12 (UPC-A) or 13 (EAN-13) digits.");
        return Validators.IsValidProductCode(code) ? null : Error.Validation("The check digit is wrong. Please check the number.");
    }
}
