using QuickQr.Core.Content;

namespace QuickQr.Core.Tests.Content;

/// <summary>One valid, normalised instance per content variant. Keys are stable so theory rows serialise.</summary>
public static class ContentSamples
{
    public static readonly ContactContent FullContact = new()
    {
        FirstName = "John",
        LastName = "Doe",
        Organization = "Acme, Inc.",
        JobTitle = "CEO",
        Phones = [new ContactPhone("+1 555 123 4567"), new ContactPhone("+1 555 765 4321", PhoneKind.Work), new ContactPhone("+1 555 000 1111", PhoneKind.Fax)],
        Emails = ["john@acme.com", "j.doe@example.org"],
        Website = "https://acme.com",
        Address = new PostalAddress("1 Main St", "Springfield", "IL", "62701", "USA"),
        Note = "Line one\nLine; two, with \\ backslash",
    };

    public static readonly IReadOnlyDictionary<string, QrContent> All = new Dictionary<string, QrContent>
    {
        ["text"] = new TextContent("Just some text"),
        ["text-multiline"] = new TextContent("First line\nSecond line: with colon; and semicolon"),
        ["url"] = new UrlContent("https://example.com/path?x=1&y=two"),
        ["url-http"] = new UrlContent("http://example.org/"),
        ["wifi-wpa"] = new WifiContent("Home Network", "correct horse"),
        ["wifi-wpa3-hidden-escaped"] = new WifiContent("Lab;5G, \"x\"", @"p@ss:word\1", WifiSecurity.Wpa3, Hidden: true),
        ["wifi-open"] = new WifiContent("Guest", null, WifiSecurity.None),
        ["wifi-wep"] = new WifiContent("Old", "abcde", WifiSecurity.Wep),
        ["contact-full"] = FullContact,
        ["contact-organisation-only"] = new ContactContent { Organization = "Acme" },
        ["contact-unicode"] = new ContactContent { FirstName = "Jürgen", LastName = "Müller", Phones = [new ContactPhone("+49 170 1234567", PhoneKind.Home)] },
        ["mecard"] = new MeCardContent("John Doe", "+15551234567", "john@example.com", "https://example.com", "1 Main St, Springfield", "Call me: anytime"),
        ["mecard-name-only"] = new MeCardContent("Jane"),
        ["email"] = new EmailContent("john@example.com", "Hello there", "Line 1\nLine 2 & more = fun?", "a@example.com", "b@example.com"),
        ["email-address-only"] = new EmailContent("jane+news@example.co.uk"),
        ["sms"] = new SmsContent("+15551234567", "Hi: there"),
        ["sms-number-only"] = new SmsContent("12345"),
        ["phone"] = new PhoneContent("+442079460958"),
        ["geo"] = new GeoContent(48.8584, 2.2945),
        ["geo-full"] = new GeoContent(-33.8688, 151.2093, 58, "Sydney Opera House"),
        ["event"] = new CalendarEventContent("Team sync; Q4, plan", new DateTimeOffset(2026, 11, 21, 13, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 21, 14, 30, 0, TimeSpan.Zero), false, "Room 1", "Agenda:\nItem \\1"),
        ["event-all-day"] = new CalendarEventContent("Holiday", new DateTimeOffset(2026, 12, 24, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 26, 0, 0, 0, TimeSpan.Zero), AllDay: true),
        ["whatsapp"] = new WhatsAppContent("15551234567", "Hello there!"),
        ["whatsapp-number-only"] = new WhatsAppContent("447700900123"),
        ["telegram"] = new TelegramContent("durov"),
        ["bitcoin"] = new CryptoPaymentContent(CryptoCurrency.Bitcoin, "bc1qar0srrr7xfkvy5l643lydnw9re59gtzzwf5mdq", 0.0015m, "Coffee shop", "Order #42"),
        ["bitcoin-legacy"] = new CryptoPaymentContent(CryptoCurrency.Bitcoin, "1BoatSLRHtKNngkdXEeobR76b53LETtpyT"),
        ["ethereum"] = new CryptoPaymentContent(CryptoCurrency.Ethereum, "0x89205A3A3b2A69De6Dbf7f01ED13B2108B2c43e7", 1.25m),
        ["litecoin"] = new CryptoPaymentContent(CryptoCurrency.Litecoin, "LVg2kJoFNg45Nbpy53h7Fe1wKyeXVRhMH9", 2m, "Shop"),
        ["bitcoin-cash"] = new CryptoPaymentContent(CryptoCurrency.BitcoinCash, "qpm2qsznhks23z7629mms6s4cwef74vcwvy22gdx6a", 0.1m),
        ["epc-remittance"] = new EpcPaymentContent("Red Cross", "DE89370400440532013000", "COBADEFFXXX", 12.5m, "CHAR", null, "Donation 2026"),
        ["epc-structured"] = new EpcPaymentContent("Acme BV", "NL91ABNA0417164300", "ABNANL2A", 100m, null, "RF18539007547034"),
        ["epc-minimal"] = new EpcPaymentContent("Jane Doe", "DE89370400440532013000"),
        ["upi"] = new UpiPaymentContent("merchant@okbank", "Chai Point", 120.5m, "Order 42"),
        ["upi-no-amount"] = new UpiPaymentContent("a.b@upi", "Shop"),
        ["secure"] = new SecureMessageContent("QQSEC1:AQIDBAUGBwgJ"),
        ["app-apple"] = new AppStoreContent(AppStoreKind.AppleAppStore, "284882215"),
        ["app-google"] = new AppStoreContent(AppStoreKind.GooglePlay, "com.whatsapp"),
        ["app-microsoft"] = new AppStoreContent(AppStoreKind.MicrosoftStore, "9NBLGGH4NNS1"),
        ["social-instagram"] = new SocialProfileContent(SocialNetwork.Instagram, "natgeo"),
        ["social-facebook"] = new SocialProfileContent(SocialNetwork.Facebook, "zuck"),
        ["social-x"] = new SocialProfileContent(SocialNetwork.X, "nasa"),
        ["social-linkedin"] = new SocialProfileContent(SocialNetwork.LinkedIn, "williamhgates"),
        ["social-linkedin-company"] = new SocialProfileContent(SocialNetwork.LinkedIn, "company/microsoft"),
        ["social-tiktok"] = new SocialProfileContent(SocialNetwork.TikTok, "khaby.lame"),
        ["social-youtube"] = new SocialProfileContent(SocialNetwork.YouTube, "MrBeast"),
        ["social-youtube-channel"] = new SocialProfileContent(SocialNetwork.YouTube, "channel/UCX6OQ3DkcsbYNE6H8uQQuVA"),
        ["social-github"] = new SocialProfileContent(SocialNetwork.GitHub, "torvalds"),
        ["social-snapchat"] = new SocialProfileContent(SocialNetwork.Snapchat, "djkhaled305"),
        ["social-pinterest"] = new SocialProfileContent(SocialNetwork.Pinterest, "pinterest"),
        ["social-threads"] = new SocialProfileContent(SocialNetwork.Threads, "zuck"),
        ["product-ean13"] = new ProductContent("4006381333931"),
        ["product-ean8"] = new ProductContent("96385074"),
        ["product-upca"] = new ProductContent("036000291452"),
        ["product-upce"] = new ProductContent("01234565"),
    };

    public static TheoryData<string> Names => [.. All.Keys];
}
