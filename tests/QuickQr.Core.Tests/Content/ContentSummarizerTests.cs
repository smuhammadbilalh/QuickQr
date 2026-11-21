using System.Globalization;
using QuickQr.Core.Content;

namespace QuickQr.Core.Tests.Content;

public class ContentSummarizerTests
{
    private readonly ContentSummarizer _summarizer = new();

    public static TheoryData<string> Samples => ContentSamples.Names;

    [Theory]
    [MemberData(nameof(Samples))]
    public void Every_sample_has_a_title_type_and_icon(string sample)
    {
        var content = ContentSamples.All[sample];
        var summary = _summarizer.Summarize(content);

        summary.Title.Should().NotBeNullOrWhiteSpace();
        summary.Title.Length.Should().BeLessThanOrEqualTo(80);
        summary.Title.Should().NotContain("\n");
        summary.Type.Should().Be(content.Type);
        summary.IconGlyph.Should().Be(IconGlyphs.ForContentType(content.Type));
    }

    [Fact]
    public void Wifi_shows_ssid_and_security() =>
        _summarizer.Summarize(new WifiContent("Home", "password1")).Should().Be(new ContentSummary("Home", "Wi-Fi · WPA2", ContentType.Wifi, IconGlyphs.Wifi));

    [Theory]
    [InlineData(WifiSecurity.None, false, "Wi-Fi · Open")]
    [InlineData(WifiSecurity.Wep, false, "Wi-Fi · WEP")]
    [InlineData(WifiSecurity.Wpa3, true, "Wi-Fi · WPA3 · Hidden")]
    public void Wifi_subtitle_variants(WifiSecurity security, bool hidden, string expected) =>
        _summarizer.Summarize(new WifiContent("Net", "password1", security, hidden)).Subtitle.Should().Be(expected);

    [Theory]
    [InlineData("https://www.github.com/dotnet/maui", "github.com")]
    [InlineData("http://example.org:8080/a", "example.org")]
    [InlineData("example.com/path", "example.com")]
    public void Url_shows_host_and_full_url(string url, string host)
    {
        var summary = _summarizer.Summarize(new UrlContent(url));
        summary.Title.Should().Be(host);
        summary.Subtitle.Should().Be(url);
        summary.IconGlyph.Should().Be(IconGlyphs.Language);
    }

    [Fact]
    public void Contact_shows_name_and_organisation()
    {
        var summary = _summarizer.Summarize(ContentSamples.FullContact);
        summary.Title.Should().Be("John Doe");
        summary.Subtitle.Should().Be("Acme, Inc.");
        summary.IconGlyph.Should().Be(IconGlyphs.Person);
    }

    [Fact]
    public void Contact_without_name_shows_organisation() =>
        _summarizer.Summarize(new ContactContent { Organization = "Acme", Phones = [new("+1 555")] }).Should()
            .Be(new ContentSummary("Acme", "+1 555", ContentType.Contact, IconGlyphs.Person));

    [Fact]
    public void Text_uses_first_line_truncated_to_80_characters()
    {
        var text = new string('a', 120) + "\nsecond line";
        var summary = _summarizer.Summarize(new TextContent(text));
        summary.Title.Should().HaveLength(80).And.EndWith("…").And.StartWith("aaaa");
        summary.Subtitle.Should().Be("Text · 132 characters");
    }

    [Fact]
    public void Short_text_is_not_truncated() =>
        _summarizer.Summarize(new TextContent("  Hello\r\nWorld  ")).Title.Should().Be("Hello");

    [Fact]
    public void Truncation_does_not_split_surrogate_pairs()
    {
        var text = new string('a', 78) + "\U0001F600\U0001F600";
        var title = _summarizer.Summarize(new TextContent(text)).Title;
        title.Should().EndWith("…");
        char.IsHighSurrogate(title[^2]).Should().BeFalse();
    }

    [Fact]
    public void Crypto_shows_currency_amount_and_short_address()
    {
        var summary = _summarizer.Summarize(new CryptoPaymentContent(CryptoCurrency.Bitcoin, "bc1qar0srrr7xfkvy5l643lydnw9re59gtzzwf5mdq", 0.0015m));
        summary.Title.Should().Be("Bitcoin payment");
        summary.Subtitle.Should().Be("0.0015 BTC · bc1qar0s…wf5mdq");
        summary.IconGlyph.Should().Be(IconGlyphs.AccountBalanceWallet);
    }

    [Theory]
    [InlineData(CryptoCurrency.Ethereum, "Ethereum payment")]
    [InlineData(CryptoCurrency.Litecoin, "Litecoin payment")]
    [InlineData(CryptoCurrency.BitcoinCash, "Bitcoin Cash payment")]
    public void Crypto_titles(CryptoCurrency currency, string title) =>
        _summarizer.Summarize(new CryptoPaymentContent(currency, "short")).Should().Be(new ContentSummary(title, "short", ContentType.CryptoPayment, IconGlyphs.Wallet));

    [Fact]
    public void All_day_event_shows_date_range()
    {
        var summary = _summarizer.Summarize(ContentSamples.All["event-all-day"]);
        summary.Title.Should().Be("Holiday");
        summary.Subtitle.Should().Be("24 Dec 2026 – 26 Dec 2026");
    }

    [Fact]
    public void Single_day_event_is_marked_all_day()
    {
        var day = new DateTimeOffset(2026, 12, 25, 0, 0, 0, TimeSpan.Zero);
        _summarizer.Summarize(new CalendarEventContent("Xmas", day, day, AllDay: true, Location: "Home")).Subtitle
            .Should().Be("25 Dec 2026 (all day) · Home");
    }

    [Fact]
    public void Timed_event_shows_local_time_range()
    {
        var start = new DateTimeOffset(2026, 11, 21, 13, 0, 0, TimeSpan.Zero);
        var end = start.AddMinutes(90);
        var localStart = start.ToLocalTime();
        var localEnd = end.ToLocalTime();
        var expected = localStart.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " – " +
                       (localStart.Date == localEnd.Date
                           ? localEnd.ToString("HH:mm", CultureInfo.InvariantCulture)
                           : localEnd.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture));

        _summarizer.Summarize(new CalendarEventContent("Sync", start, end)).Subtitle.Should().Be(expected);
    }

    [Fact]
    public void Formatting_is_culture_invariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            _summarizer.Summarize(new GeoContent(48.8584, 2.2945)).Subtitle.Should().Be("48.8584, 2.2945");
            _summarizer.Summarize(new EpcPaymentContent("Red Cross", "DE89370400440532013000", Amount: 1234.5m)).Subtitle
                .Should().Be("EUR 1234.50 · DE89 3704 0044 0532 0130 00");
            _summarizer.Summarize(ContentSamples.All["event-all-day"]).Subtitle.Should().Be("24 Dec 2026 – 26 Dec 2026");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("geo-full", "Sydney Opera House", "-33.8688, 151.2093")]
    [InlineData("email", "john@example.com", "Hello there")]
    [InlineData("sms", "+15551234567", "Hi: there")]
    [InlineData("phone", "+442079460958", "Phone number")]
    [InlineData("whatsapp", "+15551234567", "Hello there!")]
    [InlineData("telegram", "@durov", "Telegram")]
    [InlineData("upi", "Chai Point", "INR 120.50 · merchant@okbank")]
    [InlineData("secure", "Secure message", "Encrypted · passphrase required")]
    [InlineData("app-google", "Google Play app", "com.whatsapp")]
    [InlineData("social-instagram", "@natgeo", "Instagram profile")]
    [InlineData("social-linkedin-company", "company/microsoft", "LinkedIn profile")]
    [InlineData("product-ean13", "4006381333931", "EAN-13 product code")]
    [InlineData("product-upca", "036000291452", "UPC-A product code")]
    [InlineData("mecard", "John Doe", "+15551234567")]
    [InlineData("epc-remittance", "Red Cross", "EUR 12.50 · DE89 3704 0044 0532 0130 00")]
    public void Summaries_per_type(string sample, string title, string subtitle)
    {
        var summary = _summarizer.Summarize(ContentSamples.All[sample]);
        summary.Title.Should().Be(title);
        summary.Subtitle.Should().Be(subtitle);
    }

    [Fact]
    public void Icon_glyphs_are_material_icons_private_use_codepoints()
    {
        foreach (var type in Enum.GetValues<ContentType>())
        {
            var glyph = IconGlyphs.ForContentType(type);
            glyph.Should().HaveLength(1);
            glyph[0].Should().BeInRange('\ue000', '\uf8ff');
        }
    }

    [Fact]
    public void Icon_glyph_constants_match_material_icons_codepoints()
    {
        IconGlyphs.Language.Should().Be("\uea07");
        IconGlyphs.Wifi.Should().Be("\ue63e");
        IconGlyphs.Person.Should().Be("\uf0d3");
        IconGlyphs.Email.Should().Be("\ue159");
        IconGlyphs.Copy.Should().Be("\ue14d");
        IconGlyphs.Share.Should().Be("\ue80d");
        IconGlyphs.QrCode.Should().Be("\ue00a");
        IconGlyphs.VpnKey.Should().Be("\ue0da");
    }
}
