using System.Globalization;
using System.Text;
using QuickQr.Core.Content;

namespace QuickQr.Core.Tests.Content;

public class ContentEncoderTests
{
    private readonly ContentEncoder _encoder = new();

    private string Encode(QrContent content)
    {
        var result = _encoder.Encode(content);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    private Error Fail(QrContent content)
    {
        var result = _encoder.Encode(content);
        result.IsFailure.Should().BeTrue("the content is invalid");
        result.Error.Code.Should().Be(ErrorCodes.Validation);
        result.Error.Message.Should().NotBeNullOrWhiteSpace();
        return result.Error;
    }

    [Fact]
    public void Null_content_throws() =>
        FluentActions.Invoking(() => _encoder.Encode(null!)).Should().Throw<ArgumentNullException>();

    // ---- Text & URL ----

    [Fact]
    public void Text_is_kept_verbatim() => Encode(new TextContent("  Hello, world!\n")).Should().Be("  Hello, world!\n");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Text_must_not_be_empty(string text) => Fail(new TextContent(text)).Message.Should().Contain("text");

    [Theory]
    [InlineData("https://example.com", "https://example.com")]
    [InlineData("  https://example.com/path?q=1  ", "https://example.com/path?q=1")]
    [InlineData("example.com", "https://example.com")]
    [InlineData("www.example.com/a b", "https://www.example.com/a%20b")]
    [InlineData("http://example.com", "http://example.com")]
    [InlineData("localhost:8080/x", "https://localhost:8080/x")]
    public void Url_is_trimmed_and_gets_https_when_scheme_missing(string input, string expected) =>
        Encode(new UrlContent(input)).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("https://")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://exa\nmple.com")]
    public void Invalid_urls_are_rejected(string input) => Fail(new UrlContent(input));

    // ---- Wi-Fi ----

    [Theory]
    [InlineData("MyNet", "secret123", WifiSecurity.Wpa, false, "WIFI:T:WPA;S:MyNet;P:secret123;;")]
    [InlineData("Cafe", null, WifiSecurity.None, false, "WIFI:T:nopass;S:Cafe;;")]
    [InlineData("Open", "ignored-for-open", WifiSecurity.None, false, "WIFI:T:nopass;S:Open;;")]
    [InlineData("Hidden", "password1", WifiSecurity.Wpa3, true, "WIFI:T:SAE;S:Hidden;P:password1;H:true;;")]
    [InlineData("Old", "abcde", WifiSecurity.Wep, false, "WIFI:T:WEP;S:Old;P:abcde;;")]
    [InlineData("Old", "0123456789", WifiSecurity.Wep, false, "WIFI:T:WEP;S:Old;P:0123456789;;")]
    [InlineData("My;Net,\"1\"", @"pa:ss\word;", WifiSecurity.Wpa, false, @"WIFI:T:WPA;S:My\;Net\,\""1\"";P:pa\:ss\\word\;;;")]
    public void Wifi_golden(string ssid, string? password, WifiSecurity security, bool hidden, string expected) =>
        Encode(new WifiContent(ssid, password, security, hidden)).Should().Be(expected);

    [Theory]
    [InlineData("", "password1", WifiSecurity.Wpa)]
    [InlineData("Net", "short", WifiSecurity.Wpa)]
    [InlineData("Net", null, WifiSecurity.Wpa3)]
    [InlineData("Net", "abcd", WifiSecurity.Wep)]
    [InlineData("Net", "0123456789ABCDEFG", WifiSecurity.Wep)]
    [InlineData("This SSID is far too long for Wi-Fi!", "password1", WifiSecurity.Wpa)]
    public void Wifi_validation(string ssid, string? password, WifiSecurity security) =>
        Fail(new WifiContent(ssid, password, security));

    [Fact]
    public void Wifi_rejects_64_character_passphrase() =>
        Fail(new WifiContent("Net", new string('a', 64))).Message.Should().Contain("8 to 63");

    // ---- Contact (vCard 3.0) ----

    [Fact]
    public void Contact_golden_vcard()
    {
        var expected = string.Join("\r\n",
            "BEGIN:VCARD",
            "VERSION:3.0",
            "N:Doe;John;;;",
            "FN:John Doe",
            @"ORG:Acme\, Inc.",
            "TITLE:CEO",
            "TEL;TYPE=CELL:+1 555 123 4567",
            "TEL;TYPE=WORK:+1 555 765 4321",
            "TEL;TYPE=FAX:+1 555 000 1111",
            "EMAIL;TYPE=INTERNET:john@acme.com",
            "EMAIL;TYPE=INTERNET:j.doe@example.org",
            "URL:https://acme.com",
            "ADR;TYPE=WORK:;;1 Main St;Springfield;IL;62701;USA",
            @"NOTE:Line one\nLine\; two\, with \\ backslash",
            "END:VCARD");

        Encode(ContentSamples.FullContact).Should().Be(expected);
    }

    [Fact]
    public void Contact_with_only_an_organisation_uses_it_as_formatted_name() =>
        Encode(new ContactContent { Organization = "Acme" })
            .Should().Be("BEGIN:VCARD\r\nVERSION:3.0\r\nN:;;;;\r\nFN:Acme\r\nORG:Acme\r\nEND:VCARD");

    [Fact]
    public void Contact_maps_every_phone_kind()
    {
        var payload = Encode(new ContactContent
        {
            FirstName = "A",
            Phones = [new("1001"), new("1002", PhoneKind.Work), new("1003", PhoneKind.Home), new("1004", PhoneKind.Fax), new("1005", PhoneKind.Other)],
        });

        payload.Should().Contain("TEL;TYPE=CELL:1001").And.Contain("TEL;TYPE=WORK:1002").And.Contain("TEL;TYPE=HOME:1003")
            .And.Contain("TEL;TYPE=FAX:1004").And.Contain("TEL;TYPE=VOICE:1005");
    }

    [Fact]
    public void Contact_uses_crlf_and_never_folds_long_lines()
    {
        var payload = Encode(new ContactContent { FirstName = "A", Note = new string('x', 200) });
        payload.Replace("\r\n", string.Empty, StringComparison.Ordinal).Should().NotContain("\n");
        payload.Should().Contain("NOTE:" + new string('x', 200));
    }

    [Fact]
    public void Contact_skips_blank_phones_and_emails()
    {
        var payload = Encode(new ContactContent { LastName = "Solo", Phones = [new("  ")], Emails = [""] });
        payload.Should().NotContain("TEL").And.NotContain("EMAIL").And.Contain("N:Solo;;;;").And.Contain("FN:Solo");
    }

    [Fact]
    public void Contact_requires_name_or_organisation() =>
        Fail(new ContactContent { Emails = ["a@b.com"] }).Message.Should().Contain("name");

    [Fact]
    public void Contact_rejects_invalid_email() => Fail(new ContactContent { FirstName = "A", Emails = ["not-an-email"] });

    [Fact]
    public void Contact_rejects_invalid_phone() => Fail(new ContactContent { FirstName = "A", Phones = [new("call me")] });

    // ---- MeCard ----

    [Fact]
    public void MeCard_golden_with_escaping() =>
        Encode(new MeCardContent("Doe, John", "+15551234567", "john@x.com", "https://x.com", "1 Main St, City", "Hi: there; ok"))
            .Should().Be(@"MECARD:N:Doe\, John;TEL:+15551234567;EMAIL:john@x.com;URL:https\://x.com;ADR:1 Main St\, City;NOTE:Hi\: there\; ok;;");

    [Fact]
    public void MeCard_omits_empty_fields() => Encode(new MeCardContent("Jane")).Should().Be("MECARD:N:Jane;;");

    [Fact]
    public void MeCard_requires_name() => Fail(new MeCardContent(" "));

    [Fact]
    public void MeCard_rejects_invalid_email() => Fail(new MeCardContent("Jane", Email: "jane@"));

    // ---- Email ----

    [Fact]
    public void Email_golden_rfc6068() =>
        Encode(new EmailContent("john@example.com", "Hello there", "Line 1\nLine 2 & more", "a@x.com", "b@y.com"))
            .Should().Be("mailto:john@example.com?subject=Hello%20there&body=Line%201%0D%0ALine%202%20%26%20more&cc=a@x.com&bcc=b@y.com");

    [Theory]
    [InlineData("john@example.com", "mailto:john@example.com")]
    [InlineData("john+tag@example.com", "mailto:john%2Btag@example.com")]
    [InlineData(" a@x.com; b@y.com ", "mailto:a@x.com,b@y.com")]
    public void Email_address_encoding(string to, string expected) => Encode(new EmailContent(to)).Should().Be(expected);

    [Fact]
    public void Email_encodes_unicode_and_reserved_characters_in_subject() =>
        Encode(new EmailContent("a@x.com", "Grüße ?&=#")).Should().Be("mailto:a@x.com?subject=Gr%C3%BC%C3%9Fe%20%3F%26%3D%23");

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    public void Email_rejects_invalid_recipient(string to) => Fail(new EmailContent(to));

    [Fact]
    public void Email_rejects_invalid_cc() => Fail(new EmailContent("a@x.com", Cc: "nope"));

    // ---- SMS & phone ----

    [Theory]
    [InlineData("+1 (555) 123-4567", "Hi there: ok", "SMSTO:+15551234567:Hi there: ok")]
    [InlineData("12345", null, "SMSTO:12345")]
    [InlineData("0044.20.7946.0958", "", "SMSTO:00442079460958")]
    public void Sms_golden(string number, string? message, string expected) =>
        Encode(new SmsContent(number, message)).Should().Be(expected);

    [Theory]
    [InlineData("+44 20 7946-0958", "tel:+442079460958")]
    [InlineData("(555) 123.4567", "tel:5551234567")]
    [InlineData("+1/555/1234567", "tel:+15551234567")]
    public void Phone_golden(string number, string expected) => Encode(new PhoneContent(number)).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("call me")]
    [InlineData("12")]
    [InlineData("+1 555 + 1234")]
    public void Phone_numbers_are_validated(string number)
    {
        Fail(new PhoneContent(number));
        Fail(new SmsContent(number, "hi"));
    }

    // ---- Geo ----

    [Theory]
    [InlineData(48.8584, 2.2945, null, null, "geo:48.8584,2.2945")]
    [InlineData(-33.8688, 151.2093, 58.0, "Sydney Opera House", "geo:-33.8688,151.2093,58?q=Sydney%20Opera%20House")]
    [InlineData(0.0, 0.0, null, null, "geo:0,0")]
    [InlineData(0.00001, -0.00002, null, null, "geo:0.00001,-0.00002")]
    [InlineData(90.0, -180.0, null, "Café & Co", "geo:90,-180?q=Caf%C3%A9%20%26%20Co")]
    public void Geo_golden(double lat, double lon, double? alt, string? query, string expected) =>
        Encode(new GeoContent(lat, lon, alt, query)).Should().Be(expected);

    [Theory]
    [InlineData(90.0001, 0.0)]
    [InlineData(-91.0, 0.0)]
    [InlineData(0.0, 180.5)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(0.0, double.PositiveInfinity)]
    public void Geo_validates_ranges(double lat, double lon) => Fail(new GeoContent(lat, lon));

    [Fact]
    public void Geo_uses_invariant_culture_regardless_of_current_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Encode(new GeoContent(48.8584, 2.2945, 35.5)).Should().Be("geo:48.8584,2.2945,35.5");
            Encode(new EpcPaymentContent("A", "DE89370400440532013000", Amount: 1234.5m)).Should().EndWith("EUR1234.50");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ---- Calendar ----

    [Fact]
    public void Event_golden_converts_to_utc_and_escapes_text()
    {
        var content = new CalendarEventContent(
            "Team sync; Q4, plan",
            new DateTimeOffset(2026, 11, 21, 14, 0, 0, TimeSpan.FromHours(1)),
            new DateTimeOffset(2026, 11, 21, 15, 30, 0, TimeSpan.FromHours(1)),
            false,
            "Room 1",
            "Agenda:\r\nItem \\1");

        Encode(content).Should().Be(string.Join("\r\n",
            "BEGIN:VEVENT",
            @"SUMMARY:Team sync\; Q4\, plan",
            "DTSTART:20261121T130000Z",
            "DTEND:20261121T143000Z",
            "LOCATION:Room 1",
            @"DESCRIPTION:Agenda:\nItem \\1",
            "END:VEVENT"));
    }

    [Fact]
    public void All_day_event_uses_date_values_with_exclusive_end() =>
        Encode(new CalendarEventContent("Holiday", new DateTimeOffset(2026, 12, 24, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 26, 0, 0, 0, TimeSpan.Zero), AllDay: true))
            .Should().Be("BEGIN:VEVENT\r\nSUMMARY:Holiday\r\nDTSTART;VALUE=DATE:20261224\r\nDTEND;VALUE=DATE:20261227\r\nEND:VEVENT");

    [Fact]
    public void Single_all_day_event_keeps_its_local_date()
    {
        var day = new DateTimeOffset(2026, 12, 25, 0, 0, 0, TimeSpan.FromHours(5));
        Encode(new CalendarEventContent("Xmas", day, day, AllDay: true))
            .Should().Contain("DTSTART;VALUE=DATE:20261225").And.Contain("DTEND;VALUE=DATE:20261226");
    }

    [Fact]
    public void Event_requires_title() =>
        Fail(new CalendarEventContent(" ", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)).Message.Should().Contain("title");

    [Fact]
    public void Event_cannot_end_before_it_starts() =>
        Fail(new CalendarEventContent("X", DateTimeOffset.UnixEpoch.AddHours(2), DateTimeOffset.UnixEpoch)).Message.Should().Contain("end");

    [Fact]
    public void Event_with_zero_duration_is_allowed() =>
        Encode(new CalendarEventContent("Now", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)).Should().Contain("DTEND:19700101T000000Z");

    // ---- Messengers ----

    [Theory]
    [InlineData("+1 555 123 4567", "Hello there!", "https://wa.me/15551234567?text=Hello%20there%21")]
    [InlineData("447700900123", null, "https://wa.me/447700900123")]
    [InlineData("", "Share this", "https://wa.me/?text=Share%20this")]
    public void WhatsApp_golden(string number, string? message, string expected) =>
        Encode(new WhatsAppContent(number, message)).Should().Be(expected);

    [Theory]
    [InlineData("123", "hi")]
    [InlineData("", null)]
    [InlineData("1234567890123456", null)]
    public void WhatsApp_validation(string number, string? message) => Fail(new WhatsAppContent(number, message));

    [Theory]
    [InlineData("durov", "https://t.me/durov")]
    [InlineData("@durov", "https://t.me/durov")]
    [InlineData("https://t.me/telegram", "https://t.me/telegram")]
    [InlineData("t.me/Some_Bot/", "https://t.me/Some_Bot")]
    public void Telegram_golden(string username, string expected) => Encode(new TelegramContent(username)).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("bad name")]
    [InlineData("joinchat")]
    public void Telegram_validation(string username) => Fail(new TelegramContent(username));

    // ---- Crypto ----

    [Fact]
    public void Bitcoin_bip21_golden() =>
        Encode(ContentSamples.All["bitcoin"])
            .Should().Be("bitcoin:bc1qar0srrr7xfkvy5l643lydnw9re59gtzzwf5mdq?amount=0.0015&label=Coffee%20shop&message=Order%20%2342");

    [Theory]
    [InlineData(CryptoCurrency.Bitcoin, "1BoatSLRHtKNngkdXEeobR76b53LETtpyT", "1.50", "bitcoin:1BoatSLRHtKNngkdXEeobR76b53LETtpyT?amount=1.5")]
    [InlineData(CryptoCurrency.Litecoin, "LVg2kJoFNg45Nbpy53h7Fe1wKyeXVRhMH9", "0.00000001", "litecoin:LVg2kJoFNg45Nbpy53h7Fe1wKyeXVRhMH9?amount=0.00000001")]
    [InlineData(CryptoCurrency.BitcoinCash, "bitcoincash:qpm2qsznhks23z7629mms6s4cwef74vcwvy22gdx6a", "100", "bitcoincash:qpm2qsznhks23z7629mms6s4cwef74vcwvy22gdx6a?amount=100")]
    [InlineData(CryptoCurrency.Ethereum, "0x89205A3A3b2A69De6Dbf7f01ED13B2108B2c43e7", "1.5", "ethereum:0x89205A3A3b2A69De6Dbf7f01ED13B2108B2c43e7?value=1500000000000000000")]
    [InlineData(CryptoCurrency.Ethereum, "0x89205A3A3b2A69De6Dbf7f01ED13B2108B2c43e7", "0.000000000000000001", "ethereum:0x89205A3A3b2A69De6Dbf7f01ED13B2108B2c43e7?value=1")]
    [InlineData(CryptoCurrency.Ethereum, "vitalik.eth", "", "ethereum:vitalik.eth")]
    public void Crypto_golden(CryptoCurrency currency, string address, string amount, string expected)
    {
        decimal? value = amount.Length == 0 ? null : decimal.Parse(amount, CultureInfo.InvariantCulture);
        Encode(new CryptoPaymentContent(currency, address, value)).Should().Be(expected);
    }

    [Fact]
    public void Ethereum_wei_never_uses_exponent_notation() =>
        Encode(new CryptoPaymentContent(CryptoCurrency.Ethereum, "0x89205A3A3b2A69De6Dbf7f01ED13B2108B2c43e7", 12345.678m))
            .Should().EndWith("?value=12345678000000000000000");

    [Theory]
    [InlineData(CryptoCurrency.Bitcoin, "", null)]
    [InlineData(CryptoCurrency.Bitcoin, "hello world", null)]
    [InlineData(CryptoCurrency.Bitcoin, "0x89205A3A3b2A69De6Dbf7f01ED13B2108B2c43e7", null)]
    [InlineData(CryptoCurrency.Ethereum, "0x1234", null)]
    [InlineData(CryptoCurrency.Bitcoin, "1BoatSLRHtKNngkdXEeobR76b53LETtpyT", "0")]
    [InlineData(CryptoCurrency.Bitcoin, "1BoatSLRHtKNngkdXEeobR76b53LETtpyT", "-1")]
    [InlineData(CryptoCurrency.Bitcoin, "1BoatSLRHtKNngkdXEeobR76b53LETtpyT", "0.000000001")]
    [InlineData(CryptoCurrency.Ethereum, "0x89205A3A3b2A69De6Dbf7f01ED13B2108B2c43e7", "0.0000000000000000001")]
    [InlineData(CryptoCurrency.Ethereum, "0x89205A3A3b2A69De6Dbf7f01ED13B2108B2c43e7", "100000000000")]
    public void Crypto_validation(CryptoCurrency currency, string address, string? amount)
    {
        decimal? value = amount is null ? null : decimal.Parse(amount, CultureInfo.InvariantCulture);
        Fail(new CryptoPaymentContent(currency, address, value));
    }

    // ---- EPC (SEPA) ----

    [Fact]
    public void Epc_golden_with_remittance_text() =>
        Encode(new EpcPaymentContent("Red Cross", "DE89 3704 0044 0532 0130 00", "cobadeffxxx", 12.5m, "char", null, "Donation 2026"))
            .Should().Be("BCD\n002\n1\nSCT\nCOBADEFFXXX\nRed Cross\nDE89370400440532013000\nEUR12.50\nCHAR\n\nDonation 2026");

    [Fact]
    public void Epc_golden_with_structured_reference() =>
        Encode(new EpcPaymentContent("Acme BV", "NL91ABNA0417164300", "ABNANL2A", 100m, null, "RF18 5390 0754 7034"))
            .Should().Be("BCD\n002\n1\nSCT\nABNANL2A\nAcme BV\nNL91ABNA0417164300\nEUR100.00\n\nRF18539007547034");

    [Fact]
    public void Epc_minimal_has_no_trailing_separators() =>
        Encode(new EpcPaymentContent("Jane Doe", "DE89370400440532013000"))
            .Should().Be("BCD\n002\n1\nSCT\n\nJane Doe\nDE89370400440532013000");

    [Fact]
    public void Epc_payload_stays_within_331_bytes_at_maximum_field_lengths()
    {
        var payload = Encode(new EpcPaymentContent(new string('N', 70), "DE89370400440532013000", "COBADEFFXXX", 999_999_999.99m, "GDDS", null, new string('R', 140)));
        Encoding.UTF8.GetByteCount(payload).Should().BeLessThanOrEqualTo(331);
    }

    [Theory]
    [InlineData("", "DE89370400440532013000", null, "1", null, null)]
    [InlineData("Name", "DE89370400440532013001", null, "1", null, null)]
    [InlineData("Name", "XX00", null, "1", null, null)]
    [InlineData("Name", "DE89370400440532013000", "BAD", "1", null, null)]
    [InlineData("Name", "DE89370400440532013000", null, "0", null, null)]
    [InlineData("Name", "DE89370400440532013000", null, "1000000000", null, null)]
    [InlineData("Name", "DE89370400440532013000", null, "1.234", null, null)]
    [InlineData("Name", "DE89370400440532013000", null, "1", "RF18539007547034", "And text")]
    [InlineData("Name", "DE89370400440532013000", null, "1", "RF1853900754703412345678901234567890", null)]
    public void Epc_validation(string name, string iban, string? bic, string amount, string? reference, string? remittance) =>
        Fail(new EpcPaymentContent(name, iban, bic, decimal.Parse(amount, CultureInfo.InvariantCulture), null, reference, remittance));

    [Fact]
    public void Epc_rejects_long_name_and_remittance()
    {
        Fail(new EpcPaymentContent(new string('N', 71), "DE89370400440532013000"));
        Fail(new EpcPaymentContent("Name", "DE89370400440532013000", RemittanceText: new string('R', 141)));
        Fail(new EpcPaymentContent("Name", "DE89370400440532013000", Purpose: "TOOLONG"));
    }

    [Fact]
    public void Epc_rejects_payload_over_331_bytes_with_multibyte_text() =>
        Fail(new EpcPaymentContent(new string('€', 70), "DE89370400440532013000", "COBADEFFXXX", 1m, null, null, new string('€', 140)))
            .Message.Should().Contain("too long");

    // ---- UPI ----

    [Fact]
    public void Upi_golden() =>
        Encode(new UpiPaymentContent("merchant@okbank", "Chai Point", 120.5m, "Order 42"))
            .Should().Be("upi://pay?pa=merchant@okbank&pn=Chai%20Point&am=120.50&cu=INR&tn=Order%2042");

    [Fact]
    public void Upi_without_amount_or_note() =>
        Encode(new UpiPaymentContent("a.b@upi", "Shop")).Should().Be("upi://pay?pa=a.b@upi&pn=Shop&cu=INR");

    [Theory]
    [InlineData("", "Shop", null)]
    [InlineData("not-a-vpa", "Shop", null)]
    [InlineData("a@upi", "", null)]
    [InlineData("a@upi", "Shop", "0")]
    [InlineData("a@upi", "Shop", "1.005")]
    public void Upi_validation(string vpa, string name, string? amount) =>
        Fail(new UpiPaymentContent(vpa, name, amount is null ? null : decimal.Parse(amount, CultureInfo.InvariantCulture)));

    [Fact]
    public void Upi_rejects_invalid_currency() => Fail(new UpiPaymentContent("a@upi", "Shop", Currency: "RUPEES"));

    // ---- Secure message ----

    [Fact]
    public void Secure_message_is_kept_verbatim() => Encode(new SecureMessageContent("QQSEC1:abc_-123")).Should().Be("QQSEC1:abc_-123");

    [Theory]
    [InlineData("hello")]
    [InlineData("QQSEC1:")]
    [InlineData("qqsec1:abc")]
    public void Secure_message_requires_envelope(string envelope) => Fail(new SecureMessageContent(envelope));

    // ---- App stores ----

    [Theory]
    [InlineData(AppStoreKind.AppleAppStore, "284882215", "https://apps.apple.com/app/id284882215")]
    [InlineData(AppStoreKind.AppleAppStore, "id284882215", "https://apps.apple.com/app/id284882215")]
    [InlineData(AppStoreKind.AppleAppStore, "https://apps.apple.com/us/app/facebook/id284882215?mt=8", "https://apps.apple.com/app/id284882215")]
    [InlineData(AppStoreKind.GooglePlay, "com.whatsapp", "https://play.google.com/store/apps/details?id=com.whatsapp")]
    [InlineData(AppStoreKind.GooglePlay, "https://play.google.com/store/apps/details?id=com.whatsapp&hl=en", "https://play.google.com/store/apps/details?id=com.whatsapp")]
    [InlineData(AppStoreKind.MicrosoftStore, "9nblggh4nns1", "https://apps.microsoft.com/detail/9NBLGGH4NNS1")]
    [InlineData(AppStoreKind.MicrosoftStore, "https://apps.microsoft.com/detail/9nblggh4nns1?hl=en-us", "https://apps.microsoft.com/detail/9NBLGGH4NNS1")]
    public void App_store_golden(AppStoreKind store, string id, string expected) => Encode(new AppStoreContent(store, id)).Should().Be(expected);

    [Theory]
    [InlineData(AppStoreKind.AppleAppStore, "")]
    [InlineData(AppStoreKind.AppleAppStore, "facebook")]
    [InlineData(AppStoreKind.GooglePlay, "whatsapp")]
    [InlineData(AppStoreKind.GooglePlay, "com.whats app")]
    [InlineData(AppStoreKind.MicrosoftStore, "x")]
    public void App_store_validation(AppStoreKind store, string id) => Fail(new AppStoreContent(store, id));

    // ---- Social ----

    [Theory]
    [InlineData(SocialNetwork.Instagram, "natgeo", "https://instagram.com/natgeo")]
    [InlineData(SocialNetwork.Instagram, "@natgeo", "https://instagram.com/natgeo")]
    [InlineData(SocialNetwork.Instagram, "https://www.instagram.com/natgeo/?hl=en", "https://instagram.com/natgeo")]
    [InlineData(SocialNetwork.Facebook, "zuck", "https://facebook.com/zuck")]
    [InlineData(SocialNetwork.Facebook, "https://www.facebook.com/profile.php?id=100004", "https://facebook.com/profile.php?id=100004")]
    [InlineData(SocialNetwork.X, "nasa", "https://x.com/nasa")]
    [InlineData(SocialNetwork.X, "https://twitter.com/nasa", "https://x.com/nasa")]
    [InlineData(SocialNetwork.LinkedIn, "williamhgates", "https://linkedin.com/in/williamhgates")]
    [InlineData(SocialNetwork.LinkedIn, "in/williamhgates", "https://linkedin.com/in/williamhgates")]
    [InlineData(SocialNetwork.LinkedIn, "https://www.linkedin.com/company/microsoft/", "https://linkedin.com/company/microsoft")]
    [InlineData(SocialNetwork.TikTok, "@khaby.lame", "https://tiktok.com/@khaby.lame")]
    [InlineData(SocialNetwork.YouTube, "@MrBeast", "https://youtube.com/@MrBeast")]
    [InlineData(SocialNetwork.YouTube, "channel/UCX6OQ3DkcsbYNE6H8uQQuVA", "https://youtube.com/channel/UCX6OQ3DkcsbYNE6H8uQQuVA")]
    [InlineData(SocialNetwork.GitHub, "torvalds", "https://github.com/torvalds")]
    [InlineData(SocialNetwork.GitHub, "github.com/torvalds", "https://github.com/torvalds")]
    [InlineData(SocialNetwork.Snapchat, "djkhaled305", "https://snapchat.com/add/djkhaled305")]
    [InlineData(SocialNetwork.Pinterest, "pinterest", "https://pinterest.com/pinterest")]
    [InlineData(SocialNetwork.Threads, "@zuck", "https://threads.net/@zuck")]
    public void Social_profile_golden(SocialNetwork network, string handle, string expected) =>
        Encode(new SocialProfileContent(network, handle)).Should().Be(expected);

    [Theory]
    [InlineData(SocialNetwork.Instagram, "")]
    [InlineData(SocialNetwork.Instagram, "@")]
    [InlineData(SocialNetwork.Instagram, "bad handle!")]
    [InlineData(SocialNetwork.GitHub, "a/b")]
    public void Social_profile_validation(SocialNetwork network, string handle) => Fail(new SocialProfileContent(network, handle));

    [Fact]
    public void Social_profile_rejects_unknown_network() => Fail(new SocialProfileContent((SocialNetwork)99, "x"));

    // ---- Product ----

    [Theory]
    [InlineData(" 4006381333931 ", "4006381333931")]
    [InlineData("4006-3813-3393-1", "4006381333931")]
    [InlineData("96385074", "96385074")]
    [InlineData("036000291452", "036000291452")]
    [InlineData("01234565", "01234565")]
    public void Product_golden(string code, string expected) => Encode(new ProductContent(code)).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("4006381333932")]
    [InlineData("ABC123")]
    [InlineData("12345")]
    [InlineData("12345678")]
    public void Product_validation(string code) => Fail(new ProductContent(code));
}
