using QuickQr.Core.Security;

namespace QuickQr.Core.Tests.Security;

public class UrlSafetyAnalyzerTests
{
    private readonly UrlSafetyAnalyzer _analyzer = new();

    private static IEnumerable<string> Codes(UrlSafetyReport report) => report.Signals.Select(s => s.Code);

    [Theory]
    [InlineData("https://github.com")]
    [InlineData("https://www.paypal.com/signin")]
    [InlineData("https://paypal.de/")]
    [InlineData("https://www.amazon.co.uk/dp/B000")]
    [InlineData("https://accounts.google.com/signin")]
    [InlineData("https://login.microsoftonline.com/common")]
    [InlineData("https://appleid.apple.com/")]
    [InlineData("https://www.instagram.com/natgeo")]
    [InlineData("https://wa.me/15551234567")]
    [InlineData("https://pineapple.com")]
    [InlineData("https://amazonia.org")]
    [InlineData("https://onlinebanking.mybank.com/login")]
    [InlineData("https://example.com:443/")]
    [InlineData("https://en.wikipedia.org/wiki/QR_code")]
    public void Ordinary_links_are_safe(string url)
    {
        var report = _analyzer.Analyze(url);
        report.Signals.Should().BeEmpty();
        report.Level.Should().Be(RiskLevel.Safe);
        report.IsSafe.Should().BeTrue();
        report.Url.Should().Be(url);
    }

    [Fact]
    public void Display_host_is_reported() => _analyzer.Analyze("https://github.com/dotnet").DisplayHost.Should().Be("github.com");

    [Fact]
    public void Http_is_insecure()
    {
        var report = _analyzer.Analyze("http://example.com");
        Codes(report).Should().Equal(UrlSafetyAnalyzer.InsecureScheme);
        report.Level.Should().Be(RiskLevel.Medium);
    }

    [Fact]
    public void Punycode_homograph_is_high_risk()
    {
        var report = _analyzer.Analyze("https://xn--pple-43d.com");
        Codes(report).Should().Contain([UrlSafetyAnalyzer.PunycodeHost, UrlSafetyAnalyzer.MixedScriptHost, UrlSafetyAnalyzer.BrandImpersonation]);
        report.Level.Should().Be(RiskLevel.High);
        report.DisplayHost.Should().Be("аpple.com");
    }

    [Fact]
    public void Whole_script_cyrillic_lookalike_is_high_risk()
    {
        var report = _analyzer.Analyze("https://xn--80ak6aa92e.com/");
        report.DisplayHost.Should().Be("аррӏе.com");
        Codes(report).Should().Contain(UrlSafetyAnalyzer.MixedScriptHost);
        report.Level.Should().Be(RiskLevel.High);
    }

    [Fact]
    public void Unicode_input_host_is_analysed_like_punycode() =>
        Codes(_analyzer.Analyze("https://аpple.com/")).Should().Contain(UrlSafetyAnalyzer.MixedScriptHost);

    [Fact]
    public void Legitimate_latin_idn_is_only_flagged_for_review()
    {
        var report = _analyzer.Analyze("https://xn--mnchen-3ya.de/");
        Codes(report).Should().Equal(UrlSafetyAnalyzer.PunycodeHost);
        report.Level.Should().Be(RiskLevel.Medium);
        report.DisplayHost.Should().Be("münchen.de");
    }

    [Theory]
    [InlineData("https://paypal.com.secure-login.xyz")]
    [InlineData("https://paypal-login.com/verify")]
    [InlineData("https://paypa1.com")]
    [InlineData("https://mypaypal.net")]
    [InlineData("https://microsoft-support.help/")]
    [InlineData("https://secure.netflixaccount.info")]
    [InlineData("https://g00gle.com")]
    [InlineData("https://paypal.zip")]
    [InlineData("https://www.whatsapp.web-login.tk")]
    [InlineData("https://apple.com.icloud-verify.co.uk")]
    public void Brand_impersonation_is_high_risk(string url)
    {
        var report = _analyzer.Analyze(url);
        report.Signals.Should().Contain(s => s.Code == UrlSafetyAnalyzer.BrandImpersonation && s.Severity == RiskLevel.High);
        report.Level.Should().Be(RiskLevel.High);
    }

    [Fact]
    public void Brand_on_lookalike_domain_reports_the_real_domain()
    {
        var report = _analyzer.Analyze("https://paypal.com.secure-login.xyz/");
        report.Signals.Single(s => s.Code == UrlSafetyAnalyzer.BrandImpersonation).Description.Should().Contain("secure-login.xyz");
        Codes(report).Should().Contain(UrlSafetyAnalyzer.SuspiciousTld);
    }

    [Fact]
    public void Bank_keyword_in_deceptive_subdomain_is_medium_risk()
    {
        var report = _analyzer.Analyze("https://bankofamerica.com.verify-account.top/");
        report.Signals.Should().Contain(s => s.Code == UrlSafetyAnalyzer.BrandImpersonation && s.Severity == RiskLevel.Medium);
        report.Level.Should().Be(RiskLevel.Medium);
    }

    [Fact]
    public void Credentials_hide_the_real_host()
    {
        var report = _analyzer.Analyze("https://user@evil.com");
        Codes(report).Should().Contain(UrlSafetyAnalyzer.CredentialsInUrl);
        report.Level.Should().Be(RiskLevel.High);
        report.DisplayHost.Should().Be("evil.com");
    }

    [Fact]
    public void Brand_in_userinfo_trick_is_caught()
    {
        var report = _analyzer.Analyze("https://www.paypal.com:secure@phish.example.net/login");
        Codes(report).Should().Contain(UrlSafetyAnalyzer.CredentialsInUrl);
        report.DisplayHost.Should().Be("phish.example.net");
    }

    [Theory]
    [InlineData("https://bit.ly/x")]
    [InlineData("https://tinyurl.com/abc")]
    [InlineData("https://t.co/xyz")]
    [InlineData("https://qrco.de/bcd")]
    [InlineData("https://www.rb.gy/1")]
    public void Shorteners_hide_the_destination(string url)
    {
        var report = _analyzer.Analyze(url);
        Codes(report).Should().Equal(UrlSafetyAnalyzer.UrlShortener);
        report.Level.Should().Be(RiskLevel.Low);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(document.cookie)")]
    [InlineData("java\tscript:alert(1)")]
    [InlineData(" vbscript:msgbox(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("file:///etc/passwd")]
    [InlineData("intent://scan/#Intent;scheme=zxing;package=com.evil;end")]
    public void Dangerous_schemes_are_high_risk(string url)
    {
        var report = _analyzer.Analyze(url);
        Codes(report).Should().Equal(UrlSafetyAnalyzer.DangerousScheme);
        report.Level.Should().Be(RiskLevel.High);
        report.DisplayHost.Should().BeNull();
    }

    [Fact]
    public void Ip_literal_with_port_and_apk_download()
    {
        var report = _analyzer.Analyze("http://192.168.0.1:8080/a.apk");
        Codes(report).Should().BeEquivalentTo(
        [
            UrlSafetyAnalyzer.InsecureScheme, UrlSafetyAnalyzer.IpLiteralHost, UrlSafetyAnalyzer.NonStandardPort, UrlSafetyAnalyzer.FileDownload,
        ]);
        report.Level.Should().Be(RiskLevel.Medium);
        report.Signals.Single(s => s.Code == UrlSafetyAnalyzer.NonStandardPort).Severity.Should().Be(RiskLevel.Low);
    }

    [Theory]
    [InlineData("http://3232235521/")]
    [InlineData("http://0xC0A80001/")]
    [InlineData("http://0300.0250.0.1/")]
    [InlineData("http://192.168.1/")]
    public void Integer_hex_and_octal_ip_hosts_are_flagged(string url) =>
        Codes(_analyzer.Analyze(url)).Should().Contain([UrlSafetyAnalyzer.IpLiteralHost, UrlSafetyAnalyzer.ObfuscatedEncoding]);

    [Fact]
    public void Ipv6_literal_is_flagged()
    {
        var report = _analyzer.Analyze("https://[2001:db8::1]/");
        Codes(report).Should().Equal(UrlSafetyAnalyzer.IpLiteralHost);
        report.DisplayHost.Should().Be("[2001:db8::1]");
    }

    [Theory]
    [InlineData("https://example.zip/", RiskLevel.Medium)]
    [InlineData("https://example.tk/", RiskLevel.Medium)]
    [InlineData("https://example.xyz/", RiskLevel.Low)]
    [InlineData("https://cheap-deals.top/", RiskLevel.Low)]
    [InlineData("https://claim.support/", RiskLevel.Low)]
    public void Suspicious_tlds(string url, RiskLevel severity) =>
        _analyzer.Analyze(url).Signals.Should().ContainSingle(s => s.Code == UrlSafetyAnalyzer.SuspiciousTld && s.Severity == severity);

    [Fact]
    public void Excessive_subdomains_are_flagged() =>
        Codes(_analyzer.Analyze("https://a.b.c.d.example.com/")).Should().Equal(UrlSafetyAnalyzer.ExcessiveSubdomains);

    [Fact]
    public void Very_long_urls_are_flagged() =>
        Codes(_analyzer.Analyze("https://example.com/" + new string('a', 200))).Should().Equal(UrlSafetyAnalyzer.VeryLongUrl);

    [Theory]
    [InlineData("https://example.com/%61%64%6D%69%6E")]
    [InlineData("https://example.com/redirect?to=%252F%252Fevil.com")]
    [InlineData("https://%65xample.com/")]
    public void Obfuscated_encoding_is_flagged(string url) =>
        Codes(_analyzer.Analyze(url)).Should().Contain(UrlSafetyAnalyzer.ObfuscatedEncoding);

    [Fact]
    public void Percent_encoded_host_is_decoded_like_a_browser_would()
    {
        var report = _analyzer.Analyze("https://%70aypal.com.evil.xyz/login");
        report.DisplayHost.Should().Be("paypal.com.evil.xyz");
        Codes(report).Should().Contain([UrlSafetyAnalyzer.ObfuscatedEncoding, UrlSafetyAnalyzer.BrandImpersonation]);
        report.Level.Should().Be(RiskLevel.High);
    }

    [Fact]
    public void Normal_percent_encoding_is_not_obfuscation() =>
        _analyzer.Analyze("https://example.com/search?q=caf%C3%A9%20au%20lait").IsSafe.Should().BeTrue();

    [Theory]
    [InlineData("https://example.com/setup.exe", "exe")]
    [InlineData("https://example.com/app.APK?v=2", "apk")]
    [InlineData("https://example.com/dl/install.msi", "msi")]
    [InlineData("https://example.com/tool.ps1", "ps1")]
    [InlineData("https://example.com/x.dmg", "dmg")]
    public void Executable_downloads_are_flagged(string url, string extension)
    {
        var signal = _analyzer.Analyze(url).Signals.Should().ContainSingle(s => s.Code == UrlSafetyAnalyzer.FileDownload).Subject;
        signal.Severity.Should().Be(RiskLevel.Medium);
        signal.Description.Should().Contain("." + extension);
    }

    [Fact]
    public void Documents_are_not_downloads() => _analyzer.Analyze("https://example.com/menu.pdf").IsSafe.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("https://")]
    [InlineData("http://exa mple.com")]
    public void Invalid_urls_are_high_risk(string url)
    {
        var report = _analyzer.Analyze(url);
        Codes(report).Should().Equal(UrlSafetyAnalyzer.InvalidUrl);
        report.Level.Should().Be(RiskLevel.High);
    }

    [Fact]
    public void Scheme_less_host_is_analysed_as_https()
    {
        var report = _analyzer.Analyze("www.example.com/path");
        report.IsSafe.Should().BeTrue();
        report.DisplayHost.Should().Be("www.example.com");
    }

    [Fact]
    public void Other_app_schemes_are_low_risk()
    {
        var report = _analyzer.Analyze("market://details?id=com.example");
        Codes(report).Should().Equal(UrlSafetyAnalyzer.UnusualScheme);
        report.Level.Should().Be(RiskLevel.Low);
    }

    [Fact]
    public void Level_is_the_maximum_signal_severity()
    {
        var report = _analyzer.Analyze("http://paypal.com.login.example.zip:8443/update.apk");
        report.Level.Should().Be(report.Signals.Max(s => s.Severity));
        report.Level.Should().Be(RiskLevel.High);
        Codes(report).Should().Contain([UrlSafetyAnalyzer.InsecureScheme, UrlSafetyAnalyzer.ExcessiveSubdomains, UrlSafetyAnalyzer.SuspiciousTld,
            UrlSafetyAnalyzer.BrandImpersonation, UrlSafetyAnalyzer.NonStandardPort, UrlSafetyAnalyzer.FileDownload]);
    }

    [Fact]
    public void Signals_have_descriptions_and_unique_codes()
    {
        var report = _analyzer.Analyze("http://user@xn--pple-43d.com.bit.ly:81/" + new string('x', 200) + "/%41.exe");
        report.Signals.Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s.Description));
        Codes(report).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Null_url_throws() => FluentActions.Invoking(() => _analyzer.Analyze(null!)).Should().Throw<ArgumentNullException>();

    [Fact]
    public void Analysis_is_fast()
    {
        var urls = new[] { "https://github.com", "http://192.168.0.1:8080/a.apk", "https://xn--pple-43d.com", "https://paypal.com.secure-login.xyz" };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 2_000; i++) _analyzer.Analyze(urls[i % urls.Length]);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }
}
