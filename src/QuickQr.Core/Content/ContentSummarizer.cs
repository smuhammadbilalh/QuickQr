using System.Globalization;
using QuickQr.Core.Content.Internal;

namespace QuickQr.Core.Content;

/// <summary>Builds the one-line title/subtitle shown in scan results and history lists.</summary>
public sealed class ContentSummarizer : IContentSummarizer
{
    private const int MaxTitleLength = 80;
    private const int MaxSubtitleLength = 120;

    public ContentSummary Summarize(QrContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var (title, subtitle) = Describe(content);
        title = PayloadText.Truncate(FirstLine(title), MaxTitleLength);
        subtitle = subtitle is null ? null : PayloadText.Truncate(FirstLine(subtitle), MaxSubtitleLength);
        if (title.Length == 0) title = TypeName(content.Type);
        return new ContentSummary(title, string.IsNullOrWhiteSpace(subtitle) ? null : subtitle, content.Type, IconGlyphs.ForContentType(content.Type));
    }

    private static (string Title, string? Subtitle) Describe(QrContent content) => content switch
    {
        TextContent c => DescribeText(c.Text ?? string.Empty),
        UrlContent c => (UrlFormat.DisplayHost(c.Url) ?? c.Url, c.Url),
        WifiContent c => (c.Ssid, "Wi-Fi · " + WifiFormat.SecurityDisplayName(c.Security) + (c.Hidden ? " · Hidden" : string.Empty)),
        ContactContent c => DescribeContact(c),
        MeCardContent c => (c.Name, PayloadText.Clean(c.Phone) ?? PayloadText.Clean(c.Email) ?? "Contact"),
        EmailContent c => (c.To, PayloadText.Clean(c.Subject) ?? "Email"),
        SmsContent c => (c.Number, PayloadText.Clean(c.Message) ?? "SMS"),
        PhoneContent c => (c.Number, "Phone number"),
        GeoContent c => (PayloadText.Clean(c.Query) ?? "Location", FormatCoordinates(c)),
        CalendarEventContent c => (PayloadText.Clean(c.Title) ?? "Event", CalendarFormat.DescribeRange(c) + (PayloadText.Clean(c.Location) is { } l ? " · " + l : string.Empty)),
        WhatsAppContent c => (Validators.DigitsOnly(c.Number) is { Length: > 0 } d ? "+" + d : "WhatsApp", PayloadText.Clean(c.Message) ?? "WhatsApp chat"),
        TelegramContent c => ("@" + MessengerFormat.NormalizeTelegram(c.Username), "Telegram"),
        CryptoPaymentContent c => DescribeCrypto(c),
        EpcPaymentContent c => DescribeEpc(c),
        UpiPaymentContent c => DescribeUpi(c),
        SecureMessageContent => ("Secure message", "Encrypted · passphrase required"),
        AppStoreContent c => (AppStoreFormat.DisplayName(c.Store) + " app", AppStoreFormat.NormalizeId(c.Store, c.AppId)),
        SocialProfileContent c => DescribeSocial(c),
        ProductContent c => (PayloadWriter.ProductCode(c.Code), Validators.ProductSymbology(PayloadWriter.ProductCode(c.Code)) + " product code"),
        _ => (TypeName(content.Type), null),
    };

    private static (string, string?) DescribeText(string text)
    {
        var trimmed = text.Trim();
        var length = trimmed.Length.ToString(CultureInfo.InvariantCulture);
        return (trimmed, $"Text · {length} characters");
    }

    private static (string, string?) DescribeContact(ContactContent c)
    {
        var name = PayloadText.Clean(c.FullName);
        var organization = PayloadText.Clean(c.Organization);
        if (name is not null) return (name, organization ?? PayloadText.Clean(c.JobTitle) ?? FirstPhoneOrEmail(c) ?? "Contact");
        return (organization ?? FirstPhoneOrEmail(c) ?? "Contact", organization is null ? null : FirstPhoneOrEmail(c) ?? "Contact");
    }

    private static string? FirstPhoneOrEmail(ContactContent c) =>
        c.Phones?.Select(p => PayloadText.Clean(p.Number)).FirstOrDefault(n => n is not null) ??
        c.Emails?.Select(PayloadText.Clean).FirstOrDefault(e => e is not null);

    private static (string, string?) DescribeCrypto(CryptoPaymentContent c)
    {
        var title = CryptoFormat.DisplayName(c.Currency) + " payment";
        var address = ShortenAddress(CryptoFormat.NormalizeAddress(c.Currency, c.Address));
        var subtitle = c.Amount is { } amount
            ? $"{PayloadText.FormatDecimal(amount)} {CryptoFormat.Ticker(c.Currency)} · {address}"
            : address;
        if (PayloadText.Clean(c.Label) is { } label) subtitle = $"{label} · {subtitle}";
        return (title, subtitle);
    }

    private static (string, string?) DescribeEpc(EpcPaymentContent c)
    {
        var iban = Validators.FormatIban(Validators.NormalizeIban(c.Iban));
        var subtitle = c.Amount is { } amount ? $"EUR {PayloadText.FormatMoney(amount)} · {iban}" : iban;
        return (PayloadText.Clean(c.BeneficiaryName) ?? "SEPA transfer", subtitle);
    }

    private static (string, string?) DescribeUpi(UpiPaymentContent c)
    {
        var currency = PayloadText.Clean(c.Currency)?.ToUpperInvariant() ?? "INR";
        var subtitle = c.Amount is { } amount ? $"{currency} {PayloadText.FormatMoney(amount)} · {c.PayeeAddress}" : c.PayeeAddress;
        return (PayloadText.Clean(c.PayeeName) ?? c.PayeeAddress, subtitle);
    }

    private static (string, string?) DescribeSocial(SocialProfileContent c)
    {
        var handle = SocialFormat.NormalizeHandle(c.Network, c.Handle);
        var title = handle.Contains('/', StringComparison.Ordinal) || handle.Contains('?', StringComparison.Ordinal) ? handle : "@" + handle;
        return (title, SocialFormat.DisplayName(c.Network) + " profile");
    }

    private static string FormatCoordinates(GeoContent c) =>
        string.Create(CultureInfo.InvariantCulture, $"{c.Latitude:0.######}, {c.Longitude:0.######}");

    private static string ShortenAddress(string address) =>
        address.Length <= 16 ? address : $"{address[..8]}…{address[^6..]}";

    private static string FirstLine(string value)
    {
        var trimmed = value.Trim();
        var newline = trimmed.IndexOfAny(['\r', '\n']);
        return newline < 0 ? trimmed : trimmed[..newline].TrimEnd();
    }

    private static string TypeName(ContentType type) => type switch
    {
        ContentType.Text => "Text",
        ContentType.Url => "Link",
        ContentType.Wifi => "Wi-Fi network",
        ContentType.Contact or ContentType.MeCard => "Contact",
        ContentType.Email => "Email",
        ContentType.Sms => "SMS",
        ContentType.Phone => "Phone number",
        ContentType.Geo => "Location",
        ContentType.CalendarEvent => "Event",
        ContentType.WhatsApp => "WhatsApp",
        ContentType.Telegram => "Telegram",
        ContentType.CryptoPayment => "Crypto payment",
        ContentType.EpcPayment => "SEPA transfer",
        ContentType.UpiPayment => "UPI payment",
        ContentType.SecureMessage => "Secure message",
        ContentType.AppStore => "App",
        ContentType.SocialProfile => "Social profile",
        ContentType.Product => "Product",
        _ => "QR code",
    };
}
