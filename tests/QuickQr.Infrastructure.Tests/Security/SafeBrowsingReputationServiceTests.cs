using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using QuickQr.Core.Security;
using QuickQr.Infrastructure.Security;
using QuickQr.Infrastructure.Tests.Fakes;

namespace QuickQr.Infrastructure.Tests.Security;

public sealed class SafeBrowsingReputationServiceTests
{
    private const string Url = "https://phish.example/login";
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_an_api_key_nothing_is_sent()
    {
        var handler = FakeHttpMessageHandler.Json("{}");

        var result = await Create(handler, apiKey: null).CheckAsync(Url, Ct);

        result.Verdict.Should().Be(ReputationVerdict.NotConfigured);
        result.Provider.Should().Be("Google Safe Browsing");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Posts_a_lookup_request_and_reports_matches_as_malicious()
    {
        var handler = FakeHttpMessageHandler.Json(
            """
            {"matches":[
              {"threatType":"SOCIAL_ENGINEERING","platformType":"ANY_PLATFORM","threat":{"url":"https://phish.example/login"},"cacheDuration":"300s","threatEntryType":"URL"},
              {"threatType":"MALWARE","platformType":"ANY_PLATFORM","threat":{"url":"https://phish.example/login"},"cacheDuration":"300s","threatEntryType":"URL"},
              {"threatType":"MALWARE","platformType":"WINDOWS","threat":{"url":"https://phish.example/login"},"cacheDuration":"300s","threatEntryType":"URL"}
            ]}
            """);

        var result = await Create(handler).CheckAsync(Url, Ct);

        result.Verdict.Should().Be(ReputationVerdict.Malicious);
        result.Threats.Should().Equal("SOCIAL_ENGINEERING", "MALWARE");

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.GetLeftPart(UriPartial.Path).Should().Be("https://safebrowsing.googleapis.com/v4/threatMatches:find");
        request.Uri.Query.Should().Be("?key=test%2Bkey");

        using var body = JsonDocument.Parse(request.Body!);
        var threatInfo = body.RootElement.GetProperty("threatInfo");
        body.RootElement.GetProperty("client").GetProperty("clientId").GetString().Should().Be("quickqr");
        threatInfo.GetProperty("threatTypes").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("MALWARE", "SOCIAL_ENGINEERING", "UNWANTED_SOFTWARE", "POTENTIALLY_HARMFUL_APPLICATION");
        threatInfo.GetProperty("platformTypes")[0].GetString().Should().Be("ANY_PLATFORM");
        threatInfo.GetProperty("threatEntryTypes")[0].GetString().Should().Be("URL");
        threatInfo.GetProperty("threatEntries")[0].GetProperty("url").GetString().Should().Be(Url);
    }

    [Fact]
    public async Task Empty_response_is_clean()
    {
        var result = await Create(FakeHttpMessageHandler.Json("{}")).CheckAsync("https://example.com", Ct);

        result.Verdict.Should().Be(ReputationVerdict.Clean);
        result.Threats.Should().BeEmpty();
    }

    [Fact]
    public async Task Verdicts_are_cached_for_thirty_minutes()
    {
        var handler = FakeHttpMessageHandler.Json("{}");
        var service = Create(handler);

        await service.CheckAsync(Url, Ct);
        _clock.Advance(TimeSpan.FromMinutes(29));
        await service.CheckAsync(Url, Ct);
        handler.Requests.Should().HaveCount(1);

        _clock.Advance(TimeSpan.FromMinutes(2));
        await service.CheckAsync(Url, Ct);
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Network_failures_are_unknown_and_not_cached()
    {
        var handler = FakeHttpMessageHandler.Throws(new HttpRequestException("no route"));
        var service = Create(handler);

        (await service.CheckAsync(Url, Ct)).Verdict.Should().Be(ReputationVerdict.Unknown);
        (await service.CheckAsync(Url, Ct)).Verdict.Should().Be(ReputationVerdict.Unknown);
        handler.Requests.Should().HaveCount(4, "each lookup is retried once");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, 1)]
    [InlineData(HttpStatusCode.TooManyRequests, 2)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 2)]
    public async Task Http_errors_are_unknown(HttpStatusCode status, int expectedAttempts)
    {
        var handler = FakeHttpMessageHandler.Status(status);

        (await Create(handler).CheckAsync(Url, Ct)).Verdict.Should().Be(ReputationVerdict.Unknown);
        handler.Requests.Should().HaveCount(expectedAttempts);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("")]
    public async Task Non_web_urls_are_unknown_without_a_lookup(string url)
    {
        var handler = FakeHttpMessageHandler.Json("{}");

        (await Create(handler).CheckAsync(url, Ct)).Verdict.Should().Be(ReputationVerdict.Unknown);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Malformed_json_is_unknown()
    {
        (await Create(FakeHttpMessageHandler.Json("<html>")).CheckAsync(Url, Ct)).Verdict.Should().Be(ReputationVerdict.Unknown);
    }

    private SafeBrowsingReputationService Create(FakeHttpMessageHandler handler, string? apiKey = "test+key") =>
        new(new HttpClient(handler),
            Options.Create(new QuickQrInfrastructureOptions { SafeBrowsingApiKey = apiKey }),
            new FakeAppInfoProvider(Path.GetTempPath()),
            _clock)
        {
            RetryDelay = TimeSpan.Zero
        };
}
