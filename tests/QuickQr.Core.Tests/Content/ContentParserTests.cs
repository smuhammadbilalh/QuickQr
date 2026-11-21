using QuickQr.Core.Content;

namespace QuickQr.Core.Tests.Content;

public class ContentParserTests
{
    private readonly ContentParser _parser = new();

    private T Parse<T>(string payload) where T : QrContent
    {
        var content = _parser.Parse(payload);
        return content.Should().BeOfType<T>().Subject;
    }

    // ---- Classification ----

    [Theory]
    [InlineData("QQSEC1:AQIDBAUG", ContentType.SecureMessage)]
    [InlineData("WIFI:T:WPA;S:Home;P:password1;;", ContentType.Wifi)]
    [InlineData("BEGIN:VCARD\nVERSION:3.0\nFN:A\nEND:VCARD", ContentType.Contact)]
    [InlineData("MECARD:N:Doe,John;;", ContentType.MeCard)]
    [InlineData("BEGIN:VEVENT\nSUMMARY:X\nDTSTART:20260101T100000Z\nEND:VEVENT", ContentType.CalendarEvent)]
    [InlineData("mailto:a@example.com", ContentType.Email)]
    [InlineData("MATMSG:TO:a@example.com;SUB:Hi;BODY:There;;", ContentType.Email)]
    [InlineData("SMSTO:12345:STOP", ContentType.Sms)]
    [InlineData("sms:+15551234567", ContentType.Sms)]
    [InlineData("tel:+15551234567", ContentType.Phone)]
    [InlineData("geo:1.5,2.5", ContentType.Geo)]
    [InlineData("bitcoin:1BoatSLRHtKNngkdXEeobR76b53LETtpyT", ContentType.CryptoPayment)]
    [InlineData("ethereum:0x89205A3A3b2A69De6Dbf7f01ED13B2108B2c43e7", ContentType.CryptoPayment)]
    [InlineData("BCD\n002\n1\nSCT\n\nJane\nDE89370400440532013000", ContentType.EpcPayment)]
    [InlineData("upi://pay?pa=a@upi&pn=Shop", ContentType.UpiPayment)]
    [InlineData("https://wa.me/15551234567", ContentType.WhatsApp)]
    [InlineData("https://t.me/durov", ContentType.Telegram)]
    [InlineData("https://apps.apple.com/app/id284882215", ContentType.AppStore)]
    [InlineData("https://github.com/torvalds", ContentType.SocialProfile)]
    [InlineData("https://example.com", ContentType.Url)]
    [InlineData("www.example.com", ContentType.Url)]
    [InlineData("4006381333931", ContentType.Product)]
    [InlineData("jane@example.com", ContentType.Email)]
    [InlineData("+44 20 7946 0958", ContentType.Phone)]
    [InlineData("48.8584, 2.2945", ContentType.Geo)]
    [InlineData("Hello world", ContentType.Text)]
    public void Classifies_payloads(string payload, ContentType expected) => _parser.Parse(payload).Type.Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Just a sentence with https://example.com inside")]
    [InlineData("otpauth://totp/Example:alice@google.com?secret=JBSWY3DPEHPK3PXP&issuer=Example")]
    [InlineData("4006381333932")]
    [InlineData("12345678")]
    [InlineData("1234567")]
    [InlineData("12,34")]
    [InlineData("geo:91,0")]
    [InlineData("WIFI:T:WPA;P:no-ssid;;")]
    [InlineData("BEGIN:VCALENDAR\nVERSION:2.0\nEND:VCALENDAR")]
    [InlineData("BEGIN:VEVENT\nSUMMARY:No start\nEND:VEVENT")]
    [InlineData("tel:")]
    [InlineData("upi://pay?pn=NoPayee")]
    [InlineData("qqsec1:lowercase-prefix-is-not-an-envelope")]
    [InlineData("ftp://example.com/file")]
    [InlineData("http://")]
    public void Unrecognised_payloads_become_text_verbatim(string payload)
    {
        var content = Parse<TextContent>(payload);
        content.Text.Should().Be(payload);
    }

    [Theory]
    [InlineData("BEGIN:VCARD")]
    [InlineData("BEGIN:VEVENT\nDTSTART:garbage\nEND:VEVENT")]
    [InlineData("BEGIN:VEVENT\nDTSTART;TZID=\"Nowhere/Zone\":20260101T100000\nEND:VEVENT")]
    [InlineData("BEGIN:VEVENT\nDTSTART:20260101T100000Z\nDURATION:P99999999W\nEND:VEVENT")]
    [InlineData("geo:")]
    [InlineData("geo:abc,def?q=")]
    [InlineData("bitcoin:")]
    [InlineData("ethereum:@1/transfer")]
    [InlineData("BCD\n002\n1\nSCT\n\n\n")]
    [InlineData("mailto:")]
    [InlineData("mailto:%ZZ?subject=%E0%A4%A")]
    [InlineData("sms:")]
    [InlineData("SMSTO:")]
    [InlineData("MECARD:")]
    [InlineData("MATMSG:")]
    [InlineData("WIFI:")]
    [InlineData("WIFI:S:\\")]
    [InlineData("upi://pay")]
    [InlineData("whatsapp://send")]
    [InlineData("tg://resolve")]
    [InlineData("market://details")]
    [InlineData("https://wa.me/abc")]
    [InlineData("https://[::1")]
    [InlineData("\0\uFFFF\uD800")]
    [InlineData("BEGIN:VCARD\nN;ENCODING=QUOTED-PRINTABLE:=\nEND:VCARD")]
    public void Never_throws_on_malformed_payloads(string payload)
    {
        var act = () => _parser.Parse(payload);
        act.Should().NotThrow().Which.Should().NotBeNull();
    }

    [Fact]
    public void Null_payload_becomes_empty_text() => _parser.Parse(null!).Should().Be(new TextContent(string.Empty));

    [Fact]
    public void Leading_bom_and_whitespace_are_ignored_for_classification() =>
        _parser.Parse("\uFEFF  https://example.com  ").Should().Be(new UrlContent("https://example.com"));

    // ---- Wi-Fi ----

    [Theory]
    [InlineData("WIFI:S:MyNet;T:WPA;P:secret123;;", "MyNet", "secret123", WifiSecurity.Wpa, false)]
    [InlineData("wifi:t:wpa2;s:Home;p:pass1234;h:true;;", "Home", "pass1234", WifiSecurity.Wpa, true)]
    [InlineData("WIFI:T:WPA2-EAP;S:Corp;P:pw123456;;", "Corp", "pw123456", WifiSecurity.Wpa, false)]
    [InlineData("WIFI:T:nopass;S:Guest;;", "Guest", null, WifiSecurity.None, false)]
    [InlineData("WIFI:T:;S:Free;;", "Free", null, WifiSecurity.None, false)]
    [InlineData("WIFI:T:SAE;S:Lab;P:labpass1;R:1;;", "Lab", "labpass1", WifiSecurity.Wpa3, false)]
    [InlineData("WIFI:T:WPA3;S:Lab;P:labpass1;H:1;;", "Lab", "labpass1", WifiSecurity.Wpa3, true)]
    [InlineData("WIFI:T:WEP;S:Old;P:abcde;;", "Old", "abcde", WifiSecurity.Wep, false)]
    [InlineData("WIFI:S:NoType;P:secret123;;", "NoType", "secret123", WifiSecurity.Wpa, false)]
    [InlineData("WIFI:T:WPA;S:\"Quoted Net\";P:\"password\";;", "Quoted Net", "password", WifiSecurity.Wpa, false)]
    [InlineData(@"WIFI:T:WPA;S:Esc\;aped\,\:\\;P:p\:w\\d12345;;", @"Esc;aped,:\", @"p:w\d12345", WifiSecurity.Wpa, false)]
    [InlineData("WIFI:T:WPA;S:Spaces ok ;P: lead and trail ;;", "Spaces ok ", " lead and trail ", WifiSecurity.Wpa, false)]
    [InlineData("WIFI:T:nopass;S:Open;P:leaked;;", "Open", null, WifiSecurity.None, false)]
    public void Parses_wifi_variants(string payload, string ssid, string? password, WifiSecurity security, bool hidden) =>
        Parse<WifiContent>(payload).Should().Be(new WifiContent(ssid, password, security, hidden));

    // ---- vCard ----

    [Fact]
    public void Parses_ios_vcard_with_groups_types_and_folding()
    {
        const string payload =
            "BEGIN:VCARD\r\nVERSION:3.0\r\nPRODID:-//Apple Inc.//iPhone OS 17.0//EN\r\nN:Appleseed;John;;;\r\nFN:John Appleseed\r\n" +
            "ORG:Apple Inc.;\r\nTITLE:Engineer\r\nTEL;type=CELL;type=VOICE;type=pref:+1 (408) 555-0100\r\nTEL;type=WORK:+1 408 555 0101\r\n" +
            "item1.EMAIL;type=INTERNET;type=pref:john@example.com\r\nitem1.X-ABLabel:_$!<Other>!$_\r\n" +
            "item2.URL;type=pref:https://www.apple.com\r\nADR;type=HOME;type=pref:;;1 Infinite Loop;Cupertino;CA;95014;United States\r\n" +
            "NOTE:This is a long note that has been folded across\r\n  two lines.\r\nEND:VCARD\r\n";

        var contact = Parse<ContactContent>(payload);

        contact.FirstName.Should().Be("John");
        contact.LastName.Should().Be("Appleseed");
        contact.FullName.Should().Be("John Appleseed");
        contact.Organization.Should().Be("Apple Inc.");
        contact.JobTitle.Should().Be("Engineer");
        contact.Phones.Should().Equal(new ContactPhone("+1 (408) 555-0100", PhoneKind.Mobile), new ContactPhone("+1 408 555 0101", PhoneKind.Work));
        contact.Emails.Should().Equal("john@example.com");
        contact.Website.Should().Be("https://www.apple.com");
        contact.Address.Should().Be(new PostalAddress("1 Infinite Loop", "Cupertino", "CA", "95014", "United States"));
        contact.Note.Should().Be("This is a long note that has been folded across two lines.");
    }

    [Fact]
    public void Parses_android_vcard_21_with_quoted_printable()
    {
        const string payload =
            "BEGIN:VCARD\nVERSION:2.1\nN;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:=4D=C3=BC=6C=6C=65=72;J=C3=BCrgen;;;\n" +
            "TEL;CELL:+49 170 1234567\nTEL;WORK;FAX:+49 30 123456\nTEL;HOME:+49 30 654321\nEMAIL;HOME:juergen@example.de\n" +
            "NOTE;ENCODING=QUOTED-PRINTABLE:Erste Zeile=0D=0A=\nzweite Zeile\nEND:VCARD";

        var contact = Parse<ContactContent>(payload);

        contact.FirstName.Should().Be("Jürgen");
        contact.LastName.Should().Be("Müller");
        contact.Phones.Should().Equal(
            new ContactPhone("+49 170 1234567", PhoneKind.Mobile),
            new ContactPhone("+49 30 123456", PhoneKind.Fax),
            new ContactPhone("+49 30 654321", PhoneKind.Home));
        contact.Emails.Should().Equal("juergen@example.de");
        contact.Note.Should().Be("Erste Zeile\r\nzweite Zeile");
    }

    [Fact]
    public void Parses_vcard_40_with_uri_values_and_quoted_types()
    {
        const string payload =
            "BEGIN:VCARD\r\nVERSION:4.0\r\nFN:Jane Doe\r\nTEL;VALUE=uri;TYPE=\"voice,home\":tel:+1-555-555-5555\r\nEMAIL:mailto:jane@example.com\r\nEND:VCARD";

        var contact = Parse<ContactContent>(payload);

        contact.FullName.Should().Be("Jane Doe");
        contact.Phones.Should().Equal(new ContactPhone("+1-555-555-5555", PhoneKind.Home));
        contact.Emails.Should().Equal("jane@example.com");
    }

    [Fact]
    public void Parses_lowercase_vcard_with_escaped_values()
    {
        var contact = Parse<ContactContent>("begin:vcard\nversion:3.0\nn:O\\;Brien;Pat;Q;;\norg:Smith\\, Jones & Co\nnote:a\\nb\\\\c\nend:vcard");

        contact.LastName.Should().Be("O;Brien");
        contact.FirstName.Should().Be("Pat Q");
        contact.Organization.Should().Be("Smith, Jones & Co");
        contact.Note.Should().Be("a\nb\\c");
    }

    // ---- MeCard ----

    [Fact]
    public void Parses_docomo_mecard()
    {
        var card = Parse<MeCardContent>(@"MECARD:N:Doe,John;TEL:+15551234567;EMAIL:john@example.com;URL:http\://example.com;ADR:1 Main St\, Town;NOTE:Hi;;");
        card.Should().Be(new MeCardContent("John Doe", "+15551234567", "john@example.com", "http://example.com", "1 Main St, Town", "Hi"));
    }

    [Fact]
    public void Parses_lowercase_mecard_and_keeps_first_value() =>
        Parse<MeCardContent>("mecard:n:Jane;tel:111;tel:222;;").Should().Be(new MeCardContent("Jane", "111"));

    // ---- Calendar ----

    [Fact]
    public void Parses_vcalendar_with_tzid()
    {
        const string payload =
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Test//EN\r\nBEGIN:VEVENT\r\nUID:1@test\r\nSUMMARY:Launch\r\n" +
            "DTSTART;TZID=Europe/Berlin:20260315T090000\r\nDTEND;TZID=Europe/Berlin:20260315T100000\r\nLOCATION:HQ\\, Berlin\r\nEND:VEVENT\r\nEND:VCALENDAR";

        var ev = Parse<CalendarEventContent>(payload);

        ev.Title.Should().Be("Launch");
        ev.AllDay.Should().BeFalse();
        ev.Start.UtcDateTime.Should().Be(new DateTime(2026, 3, 15, 8, 0, 0, DateTimeKind.Utc));
        ev.End.UtcDateTime.Should().Be(new DateTime(2026, 3, 15, 9, 0, 0, DateTimeKind.Utc));
        ev.Location.Should().Be("HQ, Berlin");
    }

    [Fact]
    public void Parses_bare_vevent_with_lf_and_duration()
    {
        var ev = Parse<CalendarEventContent>("begin:vevent\nsummary:Standup\ndtstart:20261121T090000Z\nduration:PT15M\nend:vevent");
        ev.Start.Should().Be(new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero));
        ev.End.Should().Be(new DateTimeOffset(2026, 11, 21, 9, 15, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Parses_all_day_event_with_exclusive_end()
    {
        var ev = Parse<CalendarEventContent>("BEGIN:VEVENT\r\nSUMMARY:Holiday\r\nDTSTART;VALUE=DATE:20261224\r\nDTEND;VALUE=DATE:20261226\r\nEND:VEVENT");
        ev.AllDay.Should().BeTrue();
        ev.Start.Should().Be(new DateTimeOffset(2026, 12, 24, 0, 0, 0, TimeSpan.Zero));
        ev.End.Should().Be(new DateTimeOffset(2026, 12, 25, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Parses_floating_time_as_local_time()
    {
        var ev = Parse<CalendarEventContent>("BEGIN:VEVENT\nSUMMARY:Lunch\nDTSTART:20260601T120000\nEND:VEVENT");
        var local = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Unspecified);
        ev.Start.Should().Be(new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)));
        ev.End.Should().Be(ev.Start);
    }

    [Fact]
    public void Parses_zxing_style_event_without_seconds() =>
        Parse<CalendarEventContent>("BEGIN:VEVENT\nSUMMARY:Quick\nDTSTART:20260101T1000Z\nDTEND:20260101T1030Z\nEND:VEVENT")
            .End.Should().Be(new DateTimeOffset(2026, 1, 1, 10, 30, 0, TimeSpan.Zero));

    // ---- Email ----

    [Theory]
    [InlineData("mailto:john@example.com?subject=Hi%20there&body=Line1%0D%0ALine2", "john@example.com", "Hi there", "Line1\nLine2", null, null)]
    [InlineData("MAILTO:john@example.com", "john@example.com", null, null, null, null)]
    [InlineData("mailto:a@x.com,b@y.com?cc=c@z.com&bcc=d@w.com", "a@x.com,b@y.com", null, null, "c@z.com", "d@w.com")]
    [InlineData("mailto:?to=a@x.com&subject=S", "a@x.com", "S", null, null, null)]
    [InlineData("mailto:john%2Btag@example.com?SUBJECT=1+1", "john+tag@example.com", "1+1", null, null, null)]
    [InlineData("MATMSG:TO:john@example.com;SUB:Hello;BODY:How are you?;;", "john@example.com", "Hello", "How are you?", null, null)]
    [InlineData("jane.doe@example.co.uk", "jane.doe@example.co.uk", null, null, null, null)]
    public void Parses_email_variants(string payload, string to, string? subject, string? body, string? cc, string? bcc) =>
        Parse<EmailContent>(payload).Should().Be(new EmailContent(to, subject, body, cc, bcc));

    // ---- SMS & phone ----

    [Theory]
    [InlineData("SMSTO:+15551234567:Hello there", "+15551234567", "Hello there")]
    [InlineData("smsto:12345:STOP: now", "12345", "STOP: now")]
    [InlineData("MMSTO:12345:Pic", "12345", "Pic")]
    [InlineData("sms:+15551234567?body=Hello%20there", "+15551234567", "Hello there")]
    [InlineData("sms:+15551234567&body=Hi", "+15551234567", "Hi")]
    [InlineData("sms:+1 (555) 123-4567;body=Yo", "+15551234567", "Yo")]
    [InlineData("SMS:+15551234567", "+15551234567", null)]
    [InlineData("sms:+15551234567:Legacy", "+15551234567", "Legacy")]
    [InlineData("sms:+111,+222?body=Group", "+111", "Group")]
    public void Parses_sms_variants(string payload, string number, string? message) =>
        Parse<SmsContent>(payload).Should().Be(new SmsContent(number, message));

    [Theory]
    [InlineData("tel:+1-555-123-4567", "+15551234567")]
    [InlineData("TEL:+44 20 7946 0958", "+442079460958")]
    [InlineData("tel:+15551234567;ext=12", "+15551234567")]
    [InlineData("tel:%2B15551234567", "+15551234567")]
    [InlineData("+1 (555) 123-4567", "+15551234567")]
    public void Parses_phone_variants(string payload, string number) => Parse<PhoneContent>(payload).Number.Should().Be(number);

    // ---- Geo ----

    [Theory]
    [InlineData("geo:37.7749,-122.4194", 37.7749, -122.4194, null, null)]
    [InlineData("GEO:37.7749,-122.4194,30;crs=wgs84;u=10", 37.7749, -122.4194, 30.0, null)]
    [InlineData("geo:0,0?q=1600+Amphitheatre+Parkway", 0.0, 0.0, null, "1600 Amphitheatre Parkway")]
    [InlineData("geo:0,0?q=48.8584,2.2945(Eiffel%20Tower)", 48.8584, 2.2945, null, "Eiffel Tower")]
    [InlineData("geo:52.52,13.405?z=15&q=Berlin", 52.52, 13.405, null, "Berlin")]
    [InlineData("-33.8688,151.2093", -33.8688, 151.2093, null, null)]
    public void Parses_geo_variants(string payload, double lat, double lon, double? alt, string? query) =>
        Parse<GeoContent>(payload).Should().Be(new GeoContent(lat, lon, alt, query));

    // ---- Crypto ----

    [Fact]
    public void Parses_bip21_example_from_the_spec() =>
        Parse<CryptoPaymentContent>("bitcoin:1BoatSLRHtKNngkdXEeobR76b53LETtpyT?amount=0.5&label=Luke-Jr&message=Donation%20for%20project%20xyz")
            .Should().Be(new CryptoPaymentContent(CryptoCurrency.Bitcoin, "1BoatSLRHtKNngkdXEeobR76b53LETtpyT", 0.5m, "Luke-Jr", "Donation for project xyz"));

    [Fact]
    public void Lowercases_uppercase_bech32_addresses() =>
        Parse<CryptoPaymentContent>("BITCOIN:BC1QAR0SRRR7XFKVY5L643LYDNW9RE59GTZZWF5MDQ?AMOUNT=0.001")
            .Should().Be(new CryptoPaymentContent(CryptoCurrency.Bitcoin, "bc1qar0srrr7xfkvy5l643lydnw9re59gtzzwf5mdq", 0.001m));

    [Theory]
    [InlineData("ethereum:0x89205A3A3b2A69De6Dbf7f01ED13B2108B2c43e7@1?value=2.014e18", "0x89205A3A3b2A69De6Dbf7f01ED13B2108B2c43e7", "2.014")]
    [InlineData("ethereum:pay-0xfb6916095ca1df60bb79Ce92ce3ea74c37c5d359?value=1e16", "0xfb6916095ca1df60bb79Ce92ce3ea74c37c5d359", "0.01")]
    [InlineData("ethereum:0x89205A3A3b2A69De6Dbf7f01ED13B2108B2c43e7", "0x89205A3A3b2A69De6Dbf7f01ED13B2108B2c43e7", null)]
    [InlineData("ethereum:0xdac17f958d2ee523a2206206994597c13d831ec7/transfer?address=0x8e23ee67d1332ad560396262c48ffbb01f93d052&uint256=1", "0x8e23ee67d1332ad560396262c48ffbb01f93d052", null)]
    public void Parses_eip681(string payload, string address, string? amount)
    {
        var content = Parse<CryptoPaymentContent>(payload);
        content.Currency.Should().Be(CryptoCurrency.Ethereum);
        content.Address.Should().Be(address);
        content.Amount.Should().Be(amount is null ? null : decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("litecoin:LVg2kJoFNg45Nbpy53h7Fe1wKyeXVRhMH9?amount=1.25", CryptoCurrency.Litecoin, "LVg2kJoFNg45Nbpy53h7Fe1wKyeXVRhMH9")]
    [InlineData("bitcoincash:qpm2qsznhks23z7629mms6s4cwef74vcwvy22gdx6a?amount=1.25", CryptoCurrency.BitcoinCash, "qpm2qsznhks23z7629mms6s4cwef74vcwvy22gdx6a")]
    [InlineData("bitcoin://1BoatSLRHtKNngkdXEeobR76b53LETtpyT?amount=1.25", CryptoCurrency.Bitcoin, "1BoatSLRHtKNngkdXEeobR76b53LETtpyT")]
    public void Parses_other_bip21_coins(string payload, CryptoCurrency currency, string address) =>
        Parse<CryptoPaymentContent>(payload).Should().Be(new CryptoPaymentContent(currency, address, 1.25m));

    // ---- EPC / UPI ----

    [Fact]
    public void Parses_girocode_version_001_with_crlf()
    {
        const string payload = "BCD\r\n001\r\n1\r\nSCT\r\nBFSWDE33BER\r\nWikimedia Foerdergesellschaft\r\nDE33100205000001194700\r\nEUR123.45\r\n\r\n\r\nSpende fuer Wikipedia";
        Parse<EpcPaymentContent>(payload).Should().Be(new EpcPaymentContent(
            "Wikimedia Foerdergesellschaft", "DE33100205000001194700", "BFSWDE33BER", 123.45m, null, null, "Spende fuer Wikipedia"));
    }

    [Fact]
    public void Parses_minimal_epc_without_bic_or_amount() =>
        Parse<EpcPaymentContent>("BCD\n002\n1\nSCT\n\nJane\nDE89 3704 0044 0532 0130 00\n")
            .Should().Be(new EpcPaymentContent("Jane", "DE89370400440532013000"));

    [Theory]
    [InlineData("upi://pay?pa=merchant@okaxis&pn=Chai%20Point&am=120.50&cu=INR&tn=Order+42", "merchant@okaxis", "Chai Point", "120.50", "Order 42", "INR")]
    [InlineData("UPI://PAY?pa=x@ybl&pn=Shop&mc=5411&tr=123", "x@ybl", "Shop", null, null, "INR")]
    [InlineData("upi://pay?pa=x%40ybl&pn=&cu=usd", "x@ybl", "", null, null, "USD")]
    public void Parses_upi_links(string payload, string vpa, string name, string? amount, string? note, string currency) =>
        Parse<UpiPaymentContent>(payload).Should().Be(new UpiPaymentContent(
            vpa, name, amount is null ? null : decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), note, currency));

    // ---- Messengers ----

    [Theory]
    [InlineData("https://wa.me/15551234567?text=I%27m%20interested", "15551234567", "I'm interested")]
    [InlineData("https://api.whatsapp.com/send?phone=15551234567&text=Hello+there", "15551234567", "Hello there")]
    [InlineData("https://api.whatsapp.com/send/?phone=%2B15551234567&type=phone_number", "15551234567", null)]
    [InlineData("wa.me/447700900123", "447700900123", null)]
    [InlineData("whatsapp://send?phone=15551234567&text=Hi", "15551234567", "Hi")]
    [InlineData("https://wa.me/?text=Share%20me", "", "Share me")]
    public void Parses_whatsapp_links(string payload, string number, string? message) =>
        Parse<WhatsAppContent>(payload).Should().Be(new WhatsAppContent(number, message));

    [Theory]
    [InlineData("https://t.me/durov", "durov")]
    [InlineData("t.me/telegram", "telegram")]
    [InlineData("https://telegram.me/Some_Bot", "Some_Bot")]
    [InlineData("tg://resolve?domain=durov", "durov")]
    public void Parses_telegram_links(string payload, string username) =>
        Parse<TelegramContent>(payload).Username.Should().Be(username);

    [Theory]
    [InlineData("https://wa.me/message/ABCDEF")]
    [InlineData("https://t.me/joinchat/AAAAAEkk")]
    [InlineData("https://t.me/+AbCdEfGh")]
    [InlineData("https://t.me/durov/123")]
    [InlineData("https://instagram.com/p/Cxyz123/")]
    [InlineData("https://x.com/nasa/status/123")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://github.com/torvalds/linux")]
    [InlineData("https://www.facebook.com/sharer/sharer.php?u=x")]
    [InlineData("https://apps.apple.com/us/charts")]
    [InlineData("https://play.google.com/store/games")]
    [InlineData("https://www.microsoft.com/en-us/windows")]
    public void Feature_pages_of_known_sites_stay_plain_urls(string payload) => Parse<UrlContent>(payload).Url.Should().Be(payload);

    // ---- App stores ----

    [Theory]
    [InlineData("https://apps.apple.com/us/app/instagram/id389801252", AppStoreKind.AppleAppStore, "389801252")]
    [InlineData("https://itunes.apple.com/app/id284882215?mt=8", AppStoreKind.AppleAppStore, "284882215")]
    [InlineData("itms-apps://itunes.apple.com/app/id284882215", AppStoreKind.AppleAppStore, "284882215")]
    [InlineData("https://play.google.com/store/apps/details?id=com.instagram.android&hl=en_US", AppStoreKind.GooglePlay, "com.instagram.android")]
    [InlineData("market://details?id=com.whatsapp", AppStoreKind.GooglePlay, "com.whatsapp")]
    [InlineData("https://apps.microsoft.com/detail/9nblggh4nns1?hl=en-us&gl=US", AppStoreKind.MicrosoftStore, "9NBLGGH4NNS1")]
    [InlineData("https://apps.microsoft.com/store/detail/whatsapp/9NKSQGP7F2NH", AppStoreKind.MicrosoftStore, "9NKSQGP7F2NH")]
    [InlineData("https://www.microsoft.com/store/apps/9wzdncrfj3tj", AppStoreKind.MicrosoftStore, "9WZDNCRFJ3TJ")]
    [InlineData("ms-windows-store://pdp/?productid=9WZDNCRFJ3TJ", AppStoreKind.MicrosoftStore, "9WZDNCRFJ3TJ")]
    public void Parses_app_store_links(string payload, AppStoreKind store, string id) =>
        Parse<AppStoreContent>(payload).Should().Be(new AppStoreContent(store, id));

    // ---- Social ----

    [Theory]
    [InlineData("https://www.instagram.com/natgeo/?hl=en", SocialNetwork.Instagram, "natgeo")]
    [InlineData("https://instagr.am/natgeo", SocialNetwork.Instagram, "natgeo")]
    [InlineData("https://twitter.com/NASA", SocialNetwork.X, "NASA")]
    [InlineData("https://x.com/nasa?s=21", SocialNetwork.X, "nasa")]
    [InlineData("https://www.linkedin.com/in/williamhgates/", SocialNetwork.LinkedIn, "williamhgates")]
    [InlineData("https://linkedin.com/company/microsoft", SocialNetwork.LinkedIn, "company/microsoft")]
    [InlineData("https://www.tiktok.com/@khaby.lame?lang=en", SocialNetwork.TikTok, "khaby.lame")]
    [InlineData("https://www.youtube.com/@MrBeast", SocialNetwork.YouTube, "MrBeast")]
    [InlineData("https://m.youtube.com/channel/UCX6OQ3DkcsbYNE6H8uQQuVA", SocialNetwork.YouTube, "channel/UCX6OQ3DkcsbYNE6H8uQQuVA")]
    [InlineData("https://github.com/torvalds", SocialNetwork.GitHub, "torvalds")]
    [InlineData("https://www.snapchat.com/add/djkhaled305", SocialNetwork.Snapchat, "djkhaled305")]
    [InlineData("https://www.pinterest.com/pinterest/", SocialNetwork.Pinterest, "pinterest")]
    [InlineData("https://www.threads.net/@zuck", SocialNetwork.Threads, "zuck")]
    [InlineData("https://m.facebook.com/zuck", SocialNetwork.Facebook, "zuck")]
    [InlineData("https://www.facebook.com/profile.php?id=100004", SocialNetwork.Facebook, "profile.php?id=100004")]
    public void Parses_social_profiles(string payload, SocialNetwork network, string handle) =>
        Parse<SocialProfileContent>(payload).Should().Be(new SocialProfileContent(network, handle));

    // ---- URLs & products ----

    [Theory]
    [InlineData("https://example.com/path?q=1", "https://example.com/path?q=1")]
    [InlineData("HTTP://EXAMPLE.COM", "HTTP://EXAMPLE.COM")]
    [InlineData("www.example.com", "https://www.example.com")]
    [InlineData("  https://example.com  \r\n", "https://example.com")]
    [InlineData(@"MEBKM:TITLE:Example;URL:http\://example.com/a;;", "http://example.com/a")]
    [InlineData("URLTO:https://example.com", "https://example.com")]
    [InlineData("https://xn--pple-43d.com/login", "https://xn--pple-43d.com/login")]
    public void Parses_urls(string payload, string url) => Parse<UrlContent>(payload).Url.Should().Be(url);

    [Fact]
    public void Legacy_url_wrappers_are_classified_by_their_target() =>
        Parse<TelegramContent>(@"MEBKM:TITLE:Chat;URL:https\://t.me/durov;;").Username.Should().Be("durov");

    [Theory]
    [InlineData("4006381333931")]
    [InlineData("96385074")]
    [InlineData("036000291452")]
    [InlineData("01234565")]
    [InlineData("9780306406157")]
    public void Parses_retail_codes_with_valid_check_digit(string code) => Parse<ProductContent>(code).Code.Should().Be(code);
}
