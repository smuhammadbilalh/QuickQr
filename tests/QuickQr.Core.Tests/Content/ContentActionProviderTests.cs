using System.Text;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Content;
using QuickQr.Core.Scanning;

namespace QuickQr.Core.Tests.Content;

public class ContentActionProviderTests
{
    private readonly ContentActionProvider _provider = new();

    public static TheoryData<string> Samples => ContentSamples.Names;

    private IReadOnlyList<ContentAction> Actions(QrContent content) => _provider.GetActions(content);

    private static ContentAction Single(IReadOnlyList<ContentAction> actions, ContentActionKind kind) =>
        actions.Should().ContainSingle(a => a.Kind == kind).Subject;

    [Theory]
    [MemberData(nameof(Samples))]
    public void Only_the_first_action_is_primary_and_all_are_labelled(string sample)
    {
        var actions = Actions(ContentSamples.All[sample]);

        actions.Should().NotBeEmpty();
        actions[0].IsPrimary.Should().BeTrue();
        actions.Skip(1).Should().OnlyContain(a => !a.IsPrimary);
        actions.Should().OnlyContain(a => !string.IsNullOrWhiteSpace(a.Label) && a.IconGlyph.Length == 1 && a.Target != null);
    }

    [Fact]
    public void Url_actions()
    {
        var actions = Actions(new UrlContent("example.com/a"));
        actions.Select(a => a.Kind).Should().Equal(ContentActionKind.OpenUrl, ContentActionKind.Copy, ContentActionKind.Share);
        actions.Should().OnlyContain(a => a.Target == "https://example.com/a");
        actions[0].IconGlyph.Should().Be(IconGlyphs.OpenInNew);
    }

    [Fact]
    public void Wifi_actions_connect_first_and_expose_password()
    {
        var actions = Actions(new WifiContent("Home", "password1"));
        actions.Select(a => a.Kind).Should().Equal(ContentActionKind.ConnectWifi, ContentActionKind.CopyWifiPassword, ContentActionKind.Copy, ContentActionKind.Share);
        actions[0].Target.Should().Be("WIFI:T:WPA;S:Home;P:password1;;");
        actions[1].Target.Should().Be("password1");
    }

    [Fact]
    public void Wifi_connect_works_for_scanned_networks_the_encoder_would_reject() =>
        Single(Actions(new WifiContent("Legacy", "short")), ContentActionKind.ConnectWifi).Target.Should().Be("WIFI:T:WPA;S:Legacy;P:short;;");

    [Fact]
    public void Open_wifi_has_no_password_action() =>
        Actions(new WifiContent("Guest", null, WifiSecurity.None)).Should().NotContain(a => a.Kind == ContentActionKind.CopyWifiPassword);

    [Fact]
    public void Contact_actions()
    {
        var actions = Actions(ContentSamples.FullContact);
        actions.Select(a => a.Kind).Should().StartWith([ContentActionKind.AddContact, ContentActionKind.Call, ContentActionKind.SendEmail]);
        actions[0].Target.Should().StartWith("BEGIN:VCARD\r\nVERSION:3.0\r\n").And.EndWith("END:VCARD");
        actions[1].Target.Should().Be("+15551234567");
        actions[2].Target.Should().Be("mailto:john@acme.com");
        Single(actions, ContentActionKind.Copy).Target.Should().StartWith("John Doe\nAcme, Inc.\nCEO\n+1 555 123 4567");
    }

    [Fact]
    public void Contact_without_phone_or_email_only_offers_add_copy_share() =>
        Actions(new ContactContent { Organization = "Acme" }).Select(a => a.Kind).Should()
            .Equal(ContentActionKind.AddContact, ContentActionKind.Copy, ContentActionKind.Share);

    [Fact]
    public void MeCard_is_converted_to_vcard_for_adding()
    {
        var actions = Actions(new MeCardContent("John Doe", "+1 555 123", "john@example.com"));
        actions[0].Kind.Should().Be(ContentActionKind.AddContact);
        actions[0].Target.Should().Contain("VERSION:3.0").And.Contain("N:Doe;John;;;").And.Contain("FN:John Doe")
            .And.Contain("TEL;TYPE=CELL:+1 555 123").And.Contain("EMAIL;TYPE=INTERNET:john@example.com");
        Single(actions, ContentActionKind.Call).Target.Should().Be("+1555123");
        Single(actions, ContentActionKind.SendEmail).Target.Should().Be("mailto:john@example.com");
    }

    [Fact]
    public void Email_actions()
    {
        var actions = Actions(new EmailContent("a@x.com", "Hi"));
        actions[0].Should().Be(new ContentAction(ContentActionKind.SendEmail, "Send email", IconGlyphs.Email, "mailto:a@x.com?subject=Hi", true));
        Single(actions, ContentActionKind.Copy).Target.Should().Be("a@x.com");
    }

    [Fact]
    public void Sms_actions()
    {
        var actions = Actions(new SmsContent("+1 555 1234", "Hello"));
        actions[0].Kind.Should().Be(ContentActionKind.SendSms);
        actions[0].Target.Should().Be("+15551234");
        actions.Where(a => a.Kind == ContentActionKind.Copy).Select(a => a.Target).Should().Equal("Hello", "+15551234");
    }

    [Fact]
    public void Phone_actions() =>
        Actions(new PhoneContent("+44 20 7946 0958")).Select(a => (a.Kind, a.Target)).Should().Equal(
            (ContentActionKind.Call, "+442079460958"),
            (ContentActionKind.SendSms, "+442079460958"),
            (ContentActionKind.Copy, "+442079460958"));

    [Fact]
    public void Geo_actions_use_invariant_lat_lon() =>
        Actions(new GeoContent(48.8584, -2.2945)).Select(a => (a.Kind, a.Target)).Should().Equal(
            (ContentActionKind.OpenMap, "48.8584,-2.2945"),
            (ContentActionKind.Navigate, "48.8584,-2.2945"),
            (ContentActionKind.Copy, "48.8584,-2.2945"));

    [Fact]
    public void Calendar_action_produces_a_complete_ics_document()
    {
        var content = new CalendarEventContent(
            "A very long event title that will certainly need folding because it is longer than seventy five octets",
            new DateTimeOffset(2026, 11, 21, 13, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 21, 14, 0, 0, TimeSpan.Zero),
            Description: "Ünïcödé text: ü ö ä ß € — repeated to force folding of multi-byte characters ü ö ä ß €");

        var actions = Actions(content);
        var ics = actions[0].Target!;

        actions[0].Kind.Should().Be(ContentActionKind.AddToCalendar);
        ics.Should().StartWith("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//QuickQr//EN\r\nBEGIN:VEVENT\r\nUID:")
            .And.Contain("\r\nDTSTAMP:").And.Contain("\r\nDTSTART:20261121T130000Z\r\n").And.EndWith("END:VEVENT\r\nEND:VCALENDAR\r\n");

        var lines = ics.Split("\r\n");
        lines.Should().OnlyContain(l => Encoding.UTF8.GetByteCount(l) <= 75);
        lines.Should().Contain(l => l.StartsWith(' '), "long lines are folded");
        Single(actions, ContentActionKind.Copy).Target.Should().StartWith("BEGIN:VEVENT");
    }

    [Fact]
    public void Calendar_uid_is_stable_for_the_same_event()
    {
        static string Uid(string ics) => ics.Split("\r\n").Single(l => l.StartsWith("UID:", StringComparison.Ordinal));
        var content = ContentSamples.All["event"];
        Uid(Actions(content)[0].Target!).Should().Be(Uid(Actions(content)[0].Target!)).And.EndWith("@quickqr");
    }

    [Theory]
    [InlineData("whatsapp", ContentActionKind.OpenWhatsApp, "https://wa.me/15551234567?text=Hello%20there%21")]
    [InlineData("telegram", ContentActionKind.OpenTelegram, "https://t.me/durov")]
    [InlineData("bitcoin", ContentActionKind.OpenPaymentApp, "bitcoin:bc1qar0srrr7xfkvy5l643lydnw9re59gtzzwf5mdq?amount=0.0015&label=Coffee%20shop&message=Order%20%2342")]
    [InlineData("upi", ContentActionKind.OpenPaymentApp, "upi://pay?pa=merchant@okbank&pn=Chai%20Point&am=120.50&cu=INR&tn=Order%2042")]
    [InlineData("secure", ContentActionKind.Decrypt, "QQSEC1:AQIDBAUGBwgJ")]
    [InlineData("app-apple", ContentActionKind.OpenStore, "https://apps.apple.com/app/id284882215")]
    [InlineData("app-google", ContentActionKind.OpenStore, "https://play.google.com/store/apps/details?id=com.whatsapp")]
    [InlineData("social-github", ContentActionKind.OpenUrl, "https://github.com/torvalds")]
    [InlineData("product-ean13", ContentActionKind.SearchProduct, "https://www.google.com/search?q=4006381333931")]
    [InlineData("text", ContentActionKind.Copy, "Just some text")]
    public void Primary_action_and_target(string sample, ContentActionKind kind, string target)
    {
        var primary = Actions(ContentSamples.All[sample])[0];
        primary.Kind.Should().Be(kind);
        primary.Target.Should().Be(target);
    }

    [Fact]
    public void Crypto_copy_action_copies_the_address() =>
        Single(Actions(ContentSamples.All["bitcoin-cash"]), ContentActionKind.Copy).Target.Should().Be("qpm2qsznhks23z7629mms6s4cwef74vcwvy22gdx6a");

    [Fact]
    public void Epc_actions_copy_iban_then_all_details()
    {
        var actions = Actions(ContentSamples.All["epc-remittance"]);
        actions.Select(a => a.Kind).Should().Equal(ContentActionKind.Copy, ContentActionKind.Copy);
        actions[0].Target.Should().Be("DE89370400440532013000");
        actions[1].Target.Should().Be("Beneficiary: Red Cross\nIBAN: DE89 3704 0044 0532 0130 00\nBIC: COBADEFFXXX\nAmount: EUR 12.50\nRemittance: Donation 2026");
    }

    [Fact]
    public void Text_actions_include_web_search()
    {
        var actions = Actions(new TextContent("best pizza & pasta"));
        actions.Select(a => a.Kind).Should().Equal(ContentActionKind.Copy, ContentActionKind.Share, ContentActionKind.SearchWeb);
        actions[2].Target.Should().Be("https://www.google.com/search?q=best%20pizza%20%26%20pasta");
    }

    [Fact]
    public void Social_and_store_links_are_exposed_as_link_targets()
    {
        var social = new ScanOutcome("p", BarcodeFormat.QrCode, ContentSamples.All["social-x"], null!, Actions(ContentSamples.All["social-x"]), null, null);
        social.LinkTarget.Should().Be("https://x.com/nasa");

        var store = new ScanOutcome("p", BarcodeFormat.QrCode, ContentSamples.All["app-microsoft"], null!, Actions(ContentSamples.All["app-microsoft"]), null, null);
        store.LinkTarget.Should().Be("https://apps.microsoft.com/detail/9NBLGGH4NNS1");
    }

    [Fact]
    public void Null_content_throws() => FluentActions.Invoking(() => _provider.GetActions(null!)).Should().Throw<ArgumentNullException>();
}
