using System.Net;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using QuickQr.Core.Platform;
using QuickQr.Core.Updates;
using QuickQr.Infrastructure.Tests.Fakes;
using QuickQr.Infrastructure.Updates;

namespace QuickQr.Infrastructure.Tests.Updates;

public sealed class GitHubUpdateServiceTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero));
    private readonly InMemoryKeyValueStore _store = new();
    private readonly FakeNetworkStatus _network = new();
    private readonly FakeAppInfoProvider _appInfo = new(Path.GetTempPath(), version: "3.0.0");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reports_a_newer_release_with_details()
    {
        var handler = FakeHttpMessageHandler.Json(Release("v3.1.0"));

        var result = await Create(handler).CheckAsync(cancellationToken: Ct);

        result.Status.Should().Be(UpdateStatus.UpdateAvailable);
        result.CurrentVersion.Should().Be("3.0.0");
        result.LatestVersion.Should().Be("3.1.0");
        result.ReleaseNotes.Should().Be("Bug fixes");
        result.DownloadUrl.Should().Be(new Uri("https://github.com/owner/repo/releases/tag/v3.1.0"));
        result.PublishedAt.Should().Be(new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero));
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task Sends_the_documented_request()
    {
        var handler = FakeHttpMessageHandler.Json(Release("3.0.0"));

        await Create(handler, repository: "owner/repo").CheckAsync(cancellationToken: Ct);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.Uri.Should().Be(new Uri("https://api.github.com/repos/owner/repo/releases/latest"));
        request.Headers.Should().Contain("User-Agent: QuickQr/3.0.0").And.Contain("Accept: application/vnd.github+json");
    }

    [Theory]
    [InlineData("v3.0.0", UpdateStatus.UpToDate)]
    [InlineData("3.0", UpdateStatus.UpToDate)]
    [InlineData("3.0.0-rc.1", UpdateStatus.UpToDate)]
    [InlineData("v2.9.9", UpdateStatus.Ahead)]
    [InlineData("v3.0.1", UpdateStatus.UpdateAvailable)]
    [InlineData("V10.0", UpdateStatus.UpdateAvailable)]
    public async Task Compares_semantic_versions(string tag, UpdateStatus expected)
    {
        var result = await Create(FakeHttpMessageHandler.Json(Release(tag))).CheckAsync(cancellationToken: Ct);

        result.Status.Should().Be(expected);
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3", 0)]
    [InlineData("v1.2", "1.2.0", 0)]
    [InlineData("1.10.0", "1.9.9", 1)]
    [InlineData("1.2.3-beta", "1.2.3", 0)]
    [InlineData("1.2.3+build.5", "1.2.4", -1)]
    [InlineData("2", "1.99.99", 1)]
    public void Version_comparison(string left, string right, int expected) =>
        Math.Sign(GitHubUpdateService.CompareVersions(left, right)).Should().Be(expected);

    [Theory]
    [InlineData("""{"tag_name":"v9.0.0","prerelease":true}""")]
    [InlineData("""{"tag_name":"v9.0.0","draft":true}""")]
    public async Task Never_offers_drafts_or_prereleases(string json)
    {
        var result = await Create(FakeHttpMessageHandler.Json(json)).CheckAsync(cancellationToken: Ct);

        result.Status.Should().Be(UpdateStatus.UpToDate);
        result.LatestVersion.Should().BeNull();
    }

    [Fact]
    public async Task Automatic_checks_are_throttled_but_manual_checks_are_not()
    {
        var handler = FakeHttpMessageHandler.Json(Release("v3.1.0"));
        var service = Create(handler);

        (await service.CheckAsync(cancellationToken: Ct)).Status.Should().Be(UpdateStatus.UpdateAvailable);
        _store.Get("updates.lastCheckUtc").Should().Be("2026-11-21T09:00:00.0000000+00:00");

        _clock.Advance(TimeSpan.FromHours(23));
        (await service.CheckAsync(cancellationToken: Ct)).Status.Should().Be(UpdateStatus.Throttled);
        (await service.CheckAsync(force: true, Ct)).Status.Should().Be(UpdateStatus.UpdateAvailable);

        _clock.Advance(TimeSpan.FromHours(24));
        (await service.CheckAsync(cancellationToken: Ct)).Status.Should().Be(UpdateStatus.UpdateAvailable);
        handler.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task Corrupt_throttle_value_is_ignored()
    {
        _store.Set("updates.lastCheckUtc", "not a date");

        var result = await Create(FakeHttpMessageHandler.Json(Release("v3.0.0"))).CheckAsync(cancellationToken: Ct);

        result.Status.Should().Be(UpdateStatus.UpToDate);
    }

    [Fact]
    public async Task Offline_skips_the_request()
    {
        _network.IsOnline = false;
        var handler = FakeHttpMessageHandler.Json(Release("v3.1.0"));

        var result = await Create(handler).CheckAsync(force: true, Ct);

        result.Status.Should().Be(UpdateStatus.Offline);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Server_errors_are_retried_once_then_reported()
    {
        var handler = FakeHttpMessageHandler.Status(HttpStatusCode.InternalServerError);

        var result = await Create(handler).CheckAsync(cancellationToken: Ct);

        result.Status.Should().Be(UpdateStatus.Failed);
        result.Error.Should().Contain("500");
        handler.Requests.Should().HaveCount(2);
        _store.Get("updates.lastCheckUtc").Should().BeNull("failed checks must not start the throttle window");
    }

    [Fact]
    public async Task A_transient_failure_followed_by_success_succeeds()
    {
        var calls = 0;
        var handler = new FakeHttpMessageHandler(_ => ++calls == 1
            ? throw new HttpRequestException("connection reset")
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Release("v3.2.0")) }));

        var result = await Create(handler).CheckAsync(cancellationToken: Ct);

        result.Status.Should().Be(UpdateStatus.UpdateAvailable);
        calls.Should().Be(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "No published release")]
    [InlineData(HttpStatusCode.Forbidden, "rate limit")]
    public async Task Client_errors_are_not_retried(HttpStatusCode status, string message)
    {
        var handler = FakeHttpMessageHandler.Status(status);

        var result = await Create(handler).CheckAsync(cancellationToken: Ct);

        result.Status.Should().Be(UpdateStatus.Failed);
        result.Error.Should().Contain(message);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Network_exceptions_and_bad_json_never_escape()
    {
        (await Create(FakeHttpMessageHandler.Throws(new HttpRequestException("DNS failure"))).CheckAsync(cancellationToken: Ct))
            .Should().Match<UpdateCheckResult>(r => r.Status == UpdateStatus.Failed && r.Error!.Contains("DNS failure"));
        (await Create(FakeHttpMessageHandler.Json("{not json")).CheckAsync(cancellationToken: Ct))
            .Status.Should().Be(UpdateStatus.Failed);
        (await Create(FakeHttpMessageHandler.Json("""{"tag_name":"latest"}""")).CheckAsync(cancellationToken: Ct))
            .Error.Should().Contain("Unrecognised");
    }

    [Fact]
    public async Task Caller_cancellation_is_reported_as_failure()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await Create(FakeHttpMessageHandler.Json(Release("v3.1.0"))).CheckAsync(true, cts.Token);

        result.Status.Should().Be(UpdateStatus.Failed);
        result.Error.Should().Contain("cancelled");
    }

    private GitHubUpdateService Create(FakeHttpMessageHandler handler, string repository = "owner/repo") =>
        new(new HttpClient(handler),
            Options.Create(new QuickQrInfrastructureOptions { GitHubRepository = repository, UpdateCheckInterval = TimeSpan.FromHours(24) }),
            _appInfo,
            _network,
            _store,
            _clock)
        {
            RetryDelay = TimeSpan.Zero
        };

    private static string Release(string tag) =>
        $$"""
        {
          "tag_name": "{{tag}}",
          "name": "QuickQr {{tag}}",
          "body": "  Bug fixes  ",
          "html_url": "https://github.com/owner/repo/releases/tag/{{tag}}",
          "draft": false,
          "prerelease": false,
          "published_at": "2026-11-20T08:00:00Z"
        }
        """;
}
