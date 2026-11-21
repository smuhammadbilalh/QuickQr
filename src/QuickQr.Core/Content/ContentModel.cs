namespace QuickQr.Core.Content;

/// <summary>Semantic type of a payload. Values are persisted (history) — append only, never renumber.</summary>
public enum ContentType
{
    Text = 0,
    Url = 1,
    Wifi = 2,
    Contact = 3,        // vCard 3.0
    MeCard = 4,
    Email = 5,
    Sms = 6,
    Phone = 7,
    Geo = 8,
    CalendarEvent = 9,  // iCalendar VEVENT
    WhatsApp = 10,
    Telegram = 11,
    CryptoPayment = 12, // BIP21 / EIP-681
    EpcPayment = 13,    // SEPA credit transfer (EPC069-12 "GiroCode")
    UpiPayment = 14,    // upi://pay
    SecureMessage = 15, // QuickQr passphrase-encrypted envelope
    AppStore = 16,      // App Store / Google Play / Microsoft Store link
    SocialProfile = 17, // social network profile link
    Product = 18        // retail barcode payload (EAN/UPC digits)
}

public enum WifiSecurity { None = 0, Wep = 1, Wpa = 2, Wpa3 = 3 }
public enum CryptoCurrency { Bitcoin = 0, Ethereum = 1, Litecoin = 2, BitcoinCash = 3 }
public enum PhoneKind { Mobile = 0, Work = 1, Home = 2, Fax = 3, Other = 4 }
public enum AppStoreKind { AppleAppStore = 0, GooglePlay = 1, MicrosoftStore = 2 }
public enum SocialNetwork { Instagram = 0, Facebook = 1, X = 2, LinkedIn = 3, TikTok = 4, YouTube = 5, GitHub = 6, Snapchat = 7, Pinterest = 8, Threads = 9 }

/// <summary>A typed payload. <see cref="IContentEncoder"/> turns it into a string; <see cref="IContentParser"/> does the reverse.</summary>
public abstract record QrContent
{
    public abstract ContentType Type { get; }
}

public sealed record TextContent(string Text) : QrContent
{
    public override ContentType Type => ContentType.Text;
}

public sealed record UrlContent(string Url) : QrContent
{
    public override ContentType Type => ContentType.Url;
}

public sealed record WifiContent(string Ssid, string? Password, WifiSecurity Security = WifiSecurity.Wpa, bool Hidden = false) : QrContent
{
    public override ContentType Type => ContentType.Wifi;
}

public sealed record ContactPhone(string Number, PhoneKind Kind = PhoneKind.Mobile);

public sealed record PostalAddress(
    string? Street = null,
    string? City = null,
    string? Region = null,
    string? PostalCode = null,
    string? Country = null);

public sealed record ContactContent : QrContent
{
    public override ContentType Type => ContentType.Contact;
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Organization { get; init; }
    public string? JobTitle { get; init; }
    public IReadOnlyList<ContactPhone> Phones { get; init; } = [];
    public IReadOnlyList<string> Emails { get; init; } = [];
    public string? Website { get; init; }
    public PostalAddress? Address { get; init; }
    public string? Note { get; init; }

    public string FullName => string.Join(' ', new[] { FirstName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public sealed record MeCardContent(string Name, string? Phone = null, string? Email = null, string? Url = null, string? Address = null, string? Note = null) : QrContent
{
    public override ContentType Type => ContentType.MeCard;
}

public sealed record EmailContent(string To, string? Subject = null, string? Body = null, string? Cc = null, string? Bcc = null) : QrContent
{
    public override ContentType Type => ContentType.Email;
}

public sealed record SmsContent(string Number, string? Message = null) : QrContent
{
    public override ContentType Type => ContentType.Sms;
}

public sealed record PhoneContent(string Number) : QrContent
{
    public override ContentType Type => ContentType.Phone;
}

public sealed record GeoContent(double Latitude, double Longitude, double? Altitude = null, string? Query = null) : QrContent
{
    public override ContentType Type => ContentType.Geo;
}

public sealed record CalendarEventContent(
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool AllDay = false,
    string? Location = null,
    string? Description = null) : QrContent
{
    public override ContentType Type => ContentType.CalendarEvent;
}

public sealed record WhatsAppContent(string Number, string? Message = null) : QrContent
{
    public override ContentType Type => ContentType.WhatsApp;
}

public sealed record TelegramContent(string Username) : QrContent
{
    public override ContentType Type => ContentType.Telegram;
}

public sealed record CryptoPaymentContent(
    CryptoCurrency Currency,
    string Address,
    decimal? Amount = null,
    string? Label = null,
    string? Message = null) : QrContent
{
    public override ContentType Type => ContentType.CryptoPayment;
}

/// <summary>SEPA credit transfer per EPC069-12 (version 002). Amount in EUR, max 999,999,999.99.</summary>
public sealed record EpcPaymentContent(
    string BeneficiaryName,
    string Iban,
    string? Bic = null,
    decimal? Amount = null,
    string? Purpose = null,
    string? StructuredReference = null,
    string? RemittanceText = null) : QrContent
{
    public override ContentType Type => ContentType.EpcPayment;
}

public sealed record UpiPaymentContent(
    string PayeeAddress,
    string PayeeName,
    decimal? Amount = null,
    string? Note = null,
    string Currency = "INR") : QrContent
{
    public override ContentType Type => ContentType.UpiPayment;
}

/// <summary>An encrypted envelope produced by <see cref="Security.ISecurePayloadProtector"/>.</summary>
public sealed record SecureMessageContent(string Envelope) : QrContent
{
    public override ContentType Type => ContentType.SecureMessage;
}

public sealed record AppStoreContent(AppStoreKind Store, string AppId) : QrContent
{
    public override ContentType Type => ContentType.AppStore;
}

public sealed record SocialProfileContent(SocialNetwork Network, string Handle) : QrContent
{
    public override ContentType Type => ContentType.SocialProfile;
}

public sealed record ProductContent(string Code) : QrContent
{
    public override ContentType Type => ContentType.Product;
}

/// <summary>Short, human-readable description of a payload for lists and history.</summary>
/// <param name="IconGlyph">A Material Icons glyph (e.g. "\uea07" for language) representing the type.</param>
public sealed record ContentSummary(string Title, string? Subtitle, ContentType Type, string IconGlyph);

/// <summary>Things a user can do with scanned content. The app maps each to a platform action.</summary>
public enum ContentActionKind
{
    Copy = 0,
    Share = 1,
    OpenUrl = 2,
    ConnectWifi = 3,
    CopyWifiPassword = 4,
    AddContact = 5,
    Call = 6,
    SendSms = 7,
    SendEmail = 8,
    OpenMap = 9,
    Navigate = 10,
    AddToCalendar = 11,
    OpenWhatsApp = 12,
    OpenTelegram = 13,
    OpenPaymentApp = 14,
    Decrypt = 15,
    SearchWeb = 16,
    SearchProduct = 17,
    OpenStore = 18
}

/// <param name="Target">Data the action needs (URL, phone number, ICS text, ...). The app interprets it per kind.</param>
public sealed record ContentAction(ContentActionKind Kind, string Label, string IconGlyph, string? Target = null, bool IsPrimary = false);

/// <summary>Serialises typed content into a standards-compliant payload string.</summary>
public interface IContentEncoder
{
    /// <summary>Returns the payload, or a validation failure with a user-presentable message.</summary>
    Result<string> Encode(QrContent content);
}

/// <summary>Classifies and parses a raw scanned payload. Never fails: unknown payloads become <see cref="TextContent"/>.</summary>
public interface IContentParser
{
    QrContent Parse(string payload);
}

public interface IContentSummarizer
{
    ContentSummary Summarize(QrContent content);
}

/// <summary>Suggests the actions that make sense for a piece of content, primary action first.</summary>
public interface IContentActionProvider
{
    IReadOnlyList<ContentAction> GetActions(QrContent content);
}
