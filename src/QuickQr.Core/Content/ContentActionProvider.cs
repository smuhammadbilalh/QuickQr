using QuickQr.Core.Content.Internal;

namespace QuickQr.Core.Content;

/// <summary>
/// Suggests what the user can do with content, primary action first. <see cref="ContentAction.Target"/> conventions:
/// <list type="bullet">
/// <item>OpenUrl / OpenStore / OpenWhatsApp / OpenTelegram / SearchWeb / SearchProduct: an absolute https URL.</item>
/// <item>Call / SendSms: the normalised phone number ("+15551234567"); the SMS text is on the <see cref="SmsContent"/>.</item>
/// <item>SendEmail: a mailto: URI (RFC 6068). ConnectWifi: the WIFI: payload. CopyWifiPassword: the password.</item>
/// <item>AddContact: vCard 3.0 text. AddToCalendar: a complete iCalendar (.ics) document.</item>
/// <item>OpenMap / Navigate: "lat,lon" (invariant). OpenPaymentApp: the payment URI. Decrypt: the envelope.</item>
/// <item>Copy / Share: the text to copy or share.</item>
/// </list>
/// </summary>
public sealed class ContentActionProvider : IContentActionProvider
{
    public IReadOnlyList<ContentAction> GetActions(QrContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var actions = Build(content).Where(a => a is not null).Select(a => a!).ToList();
        if (actions.Count == 0) actions.Add(Copy("Copy", PayloadWriter.Write(content)));
        actions[0] = actions[0] with { IsPrimary = true };
        return actions;
    }

    private static IEnumerable<ContentAction?> Build(QrContent content) => content switch
    {
        UrlContent c => LinkActions(UrlFormat.Normalize(c.Url), ContentActionKind.OpenUrl, "Open link"),
        WifiContent c => WifiActions(c),
        ContactContent c => ContactActions(VCardFormat.Write(c), c.Phones?.Select(p => p.Number), c.Emails, VCardFormat.Describe(c)),
        MeCardContent c => ContactActions(VCardFormat.Write(c), [c.Phone], [c.Email], MeCardDescription(c)),
        EmailContent c =>
        [
            new(ContentActionKind.SendEmail, "Send email", IconGlyphs.Email, EmailFormat.Write(c)),
            Copy("Copy address", string.Join(", ", Validators.SplitAddresses(c.To))),
        ],
        SmsContent c =>
        [
            new(ContentActionKind.SendSms, "Send SMS", IconGlyphs.Sms, Validators.StripPhone(c.Number)),
            string.IsNullOrEmpty(c.Message) ? null : Copy("Copy message", c.Message),
            Copy("Copy number", Validators.StripPhone(c.Number)),
        ],
        PhoneContent c =>
        [
            new(ContentActionKind.Call, "Call", IconGlyphs.Call, Validators.StripPhone(c.Number)),
            new(ContentActionKind.SendSms, "Send SMS", IconGlyphs.Sms, Validators.StripPhone(c.Number)),
            Copy("Copy number", Validators.StripPhone(c.Number)),
        ],
        GeoContent c =>
        [
            new(ContentActionKind.OpenMap, "Open in maps", IconGlyphs.Map, GeoFormat.Coordinates(c)),
            new(ContentActionKind.Navigate, "Directions", IconGlyphs.Navigate, GeoFormat.Coordinates(c)),
            Copy("Copy coordinates", GeoFormat.Coordinates(c)),
        ],
        CalendarEventContent c =>
        [
            new(ContentActionKind.AddToCalendar, "Add to calendar", IconGlyphs.AddToCalendar, CalendarFormat.WriteCalendar(c, DateTimeOffset.UtcNow)),
            Copy("Copy", CalendarFormat.Write(c)),
        ],
        WhatsAppContent c =>
        [
            new(ContentActionKind.OpenWhatsApp, "Open WhatsApp", IconGlyphs.WhatsApp, MessengerFormat.WriteWhatsApp(c)),
            Copy("Copy link", MessengerFormat.WriteWhatsApp(c)),
        ],
        TelegramContent c =>
        [
            new(ContentActionKind.OpenTelegram, "Open Telegram", IconGlyphs.Telegram, MessengerFormat.WriteTelegram(c)),
            Copy("Copy link", MessengerFormat.WriteTelegram(c)),
        ],
        CryptoPaymentContent c =>
        [
            new(ContentActionKind.OpenPaymentApp, "Open wallet", IconGlyphs.Wallet, CryptoFormat.Write(c)),
            Copy("Copy address", CryptoFormat.NormalizeAddress(c.Currency, c.Address)),
        ],
        UpiPaymentContent c =>
        [
            new(ContentActionKind.OpenPaymentApp, "Pay with UPI app", IconGlyphs.Wallet, UpiFormat.Write(c)),
            Copy("Copy UPI ID", (c.PayeeAddress ?? string.Empty).Trim()),
        ],
        EpcPaymentContent c =>
        [
            Copy("Copy IBAN", Validators.NormalizeIban(c.Iban)),
            Copy("Copy all details", EpcFormat.Describe(c)),
        ],
        SecureMessageContent c =>
        [
            new(ContentActionKind.Decrypt, "Decrypt", IconGlyphs.Decrypt, (c.Envelope ?? string.Empty).Trim()),
            Copy("Copy encrypted text", (c.Envelope ?? string.Empty).Trim()),
        ],
        AppStoreContent c => LinkActions(AppStoreFormat.Write(c), ContentActionKind.OpenStore, "Open in " + AppStoreFormat.DisplayName(c.Store)),
        SocialProfileContent c => LinkActions(SocialFormat.Write(c), ContentActionKind.OpenUrl, "Open " + SocialFormat.DisplayName(c.Network) + " profile"),
        ProductContent c =>
        [
            new(ContentActionKind.SearchProduct, "Search product", IconGlyphs.Search, GoogleSearch(PayloadWriter.ProductCode(c.Code))),
            Copy("Copy code", PayloadWriter.ProductCode(c.Code)),
        ],
        TextContent c =>
        [
            Copy("Copy text", c.Text ?? string.Empty),
            new(ContentActionKind.Share, "Share", IconGlyphs.Share, c.Text ?? string.Empty),
            new(ContentActionKind.SearchWeb, "Search the web", IconGlyphs.Search, GoogleSearch((c.Text ?? string.Empty).Trim())),
        ],
        _ => [],
    };

    private static ContentAction Copy(string label, string target) => new(ContentActionKind.Copy, label, IconGlyphs.Copy, target);

    private static List<ContentAction?> LinkActions(string url, ContentActionKind open, string label) =>
    [
        new(open, label, IconGlyphs.Open, url),
        Copy("Copy link", url),
        new(ContentActionKind.Share, "Share", IconGlyphs.Share, url),
    ];

    private static List<ContentAction?> WifiActions(WifiContent c) =>
    [
        new(ContentActionKind.ConnectWifi, "Connect", IconGlyphs.Wifi, WifiFormat.Write(c)),
        string.IsNullOrEmpty(c.Password) || c.Security == WifiSecurity.None
            ? null
            : new ContentAction(ContentActionKind.CopyWifiPassword, "Copy password", IconGlyphs.Password, c.Password),
        Copy("Copy network name", c.Ssid ?? string.Empty),
        new(ContentActionKind.Share, "Share", IconGlyphs.Share, WifiFormat.Write(c)),
    ];

    private static List<ContentAction?> ContactActions(string vCard, IEnumerable<string?>? phones, IEnumerable<string?>? emails, string description)
    {
        var phone = phones?.Select(PayloadText.Clean).FirstOrDefault(p => p is not null);
        var email = emails?.Select(PayloadText.Clean).FirstOrDefault(e => e is not null);
        return
        [
            new(ContentActionKind.AddContact, "Add contact", IconGlyphs.AddContact, vCard),
            phone is null ? null : new ContentAction(ContentActionKind.Call, "Call", IconGlyphs.Call, Validators.StripPhone(phone)),
            email is null ? null : new ContentAction(ContentActionKind.SendEmail, "Send email", IconGlyphs.Email, EmailFormat.MailtoFor(email)),
            Copy("Copy details", description),
            new(ContentActionKind.Share, "Share", IconGlyphs.Share, vCard),
        ];
    }

    private static string MeCardDescription(MeCardContent c) =>
        string.Join('\n', new[] { c.Name, c.Phone, c.Email, c.Url, c.Address, c.Note }.Select(PayloadText.Clean).Where(s => s is not null));

    private static string GoogleSearch(string query) => "https://www.google.com/search?q=" + PayloadText.PercentEncode(query);
}
