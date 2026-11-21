using QuickQr.Core.Content;
using QuickQr.Core.Licensing;

namespace QuickQr.Features.Generate;

/// <summary>Entries of the Create tab's content-type picker (Contact covers vCard and MeCard).</summary>
public enum ContentKind
{
    Text,
    Url,
    Wifi,
    Contact,
    Email,
    Sms,
    Phone,
    Location,
    Event,
    WhatsApp,
    Telegram,
    Crypto,
    Sepa,
    Upi,
    AppStore,
    Social,
    Product,
    Secure
}

/// <param name="Glyph">Material Icons codepoint.</param>
/// <param name="RequiredFeature">Premium feature needed to export this type, if any.</param>
public sealed record ContentKindInfo(ContentKind Kind, string Name, string Glyph, PremiumFeature? RequiredFeature = null);

public static class ContentKinds
{
    public static IReadOnlyList<ContentKindInfo> All { get; } = new ContentKindInfo[]
    {
        new(ContentKind.Url, "Website", "\ue250"),          // link
        new(ContentKind.Text, "Text", "\ue262"),            // text_fields
        new(ContentKind.Wifi, "Wi-Fi", "\ue63e"),           // wifi
        new(ContentKind.Contact, "Contact", "\uf0d3"),      // person
        new(ContentKind.Email, "Email", "\ue159"),          // email
        new(ContentKind.Sms, "SMS", "\ue625"),              // sms
        new(ContentKind.Phone, "Phone", "\uf0d4"),          // phone
        new(ContentKind.Location, "Location", "\uf1db"),    // location_on
        new(ContentKind.Event, "Event", "\ue878"),          // event
        new(ContentKind.WhatsApp, "WhatsApp", "\ue0c9"),    // chat
        new(ContentKind.Telegram, "Telegram", "\ue163"),    // send
        new(ContentKind.Crypto, "Crypto", "\ue850"),        // account_balance_wallet
        new(ContentKind.Sepa, "SEPA", "\ue84f"),            // account_balance
        new(ContentKind.Upi, "UPI", "\ue8a1"),              // payment
        new(ContentKind.AppStore, "App", "\ue8c9"),         // shop
        new(ContentKind.Social, "Social", "\uea21"),        // group
        new(ContentKind.Product, "Product", "\ue8cc"),      // shopping_cart
        new(ContentKind.Secure, "Secure", "\ue899", PremiumFeature.SecureQr), // lock
    };

    public static ContentKindInfo Get(ContentKind kind) => All.First(k => k.Kind == kind);

    public static ContentKind FromContentType(ContentType type) => type switch
    {
        ContentType.Text => ContentKind.Text,
        ContentType.Url => ContentKind.Url,
        ContentType.Wifi => ContentKind.Wifi,
        ContentType.Contact or ContentType.MeCard => ContentKind.Contact,
        ContentType.Email => ContentKind.Email,
        ContentType.Sms => ContentKind.Sms,
        ContentType.Phone => ContentKind.Phone,
        ContentType.Geo => ContentKind.Location,
        ContentType.CalendarEvent => ContentKind.Event,
        ContentType.WhatsApp => ContentKind.WhatsApp,
        ContentType.Telegram => ContentKind.Telegram,
        ContentType.CryptoPayment => ContentKind.Crypto,
        ContentType.EpcPayment => ContentKind.Sepa,
        ContentType.UpiPayment => ContentKind.Upi,
        ContentType.SecureMessage => ContentKind.Secure,
        ContentType.AppStore => ContentKind.AppStore,
        ContentType.SocialProfile => ContentKind.Social,
        ContentType.Product => ContentKind.Product,
        _ => ContentKind.Text
    };
}
