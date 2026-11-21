using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuickQr.Core.Platform;
using QuickQr.Core.Updates;

namespace QuickQr.Infrastructure.Updates;

/// <summary>
/// Checks <c>GET /repos/{owner}/{repo}/releases/latest</c> on the GitHub REST API. Automatic checks are throttled
/// to <see cref="QuickQrInfrastructureOptions.UpdateCheckInterval"/>; every failure is reported as
/// <see cref="UpdateStatus.Failed"/> (no exceptions escape).
/// </summary>
public sealed class GitHubUpdateService(
    [FromKeyedServices(QuickQrHttp.ClientKey)] HttpClient httpClient,
    IOptions<QuickQrInfrastructureOptions> options,
    IAppInfoProvider appInfo,
    INetworkStatus network,
    IKeyValueStore store,
    TimeProvider clock,
    ILogger<GitHubUpdateService>? logger = null) : IUpdateService
{
    internal const string LastCheckKey = "updates.lastCheckUtc";
    internal const string ApiBaseUrl = "https://api.github.com";

    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;

    /// <summary>Delay before the single retry of a transient failure.</summary>
    internal TimeSpan RetryDelay { get; init; } = HttpRetry.DefaultDelay;

    public async Task<UpdateCheckResult> CheckAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        var current = appInfo.Version;
        try
        {
            var settings = options.Value;
            var now = clock.GetUtcNow();

            if (!force && IsThrottled(now, settings.UpdateCheckInterval))
                return new UpdateCheckResult(UpdateStatus.Throttled, current);

            if (!network.IsOnline)
                return new UpdateCheckResult(UpdateStatus.Offline, current, Error: "No internet connection.");

            if (string.IsNullOrWhiteSpace(settings.GitHubRepository) || settings.GitHubRepository.Count(static c => c == '/') != 1)
                return Failed(current, "The update repository is not configured.");

            var uri = new Uri($"{ApiBaseUrl}/repos/{settings.GitHubRepository.Trim()}/releases/latest");
            using var response = await HttpRetry.SendAsync(httpClient, () => CreateRequest(uri, current), RetryDelay, clock, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return Failed(current, "No published release was found.");
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                return Failed(current, "GitHub rate limit reached. Try again later.");
            if (!response.IsSuccessStatusCode)
                return Failed(current, $"GitHub returned HTTP {((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)}.");

            var release = await response.Content.ReadFromJsonAsync(UpdatesJsonContext.Default.GitHubRelease, cancellationToken).ConfigureAwait(false);
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
                return Failed(current, "The release information could not be read.");

            RecordCheck(now);

            // /releases/latest never returns drafts or pre-releases, but guard anyway: they are never offered.
            if (release.Draft || release.Prerelease)
                return new UpdateCheckResult(UpdateStatus.UpToDate, current);

            if (!TryParseVersion(release.TagName, out var latestParts))
                return Failed(current, $"Unrecognised release version '{release.TagName}'.");

            var latest = NormalizeVersion(release.TagName);
            var comparison = TryParseVersion(current, out var currentParts) ? Compare(latestParts, currentParts) : 1;
            var status = comparison switch
            {
                > 0 => UpdateStatus.UpdateAvailable,
                0 => UpdateStatus.UpToDate,
                _ => UpdateStatus.Ahead
            };

            var downloadUrl = Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps ? url : null;
            return new UpdateCheckResult(
                status,
                current,
                latest,
                string.IsNullOrWhiteSpace(release.Body) ? null : release.Body.Trim(),
                downloadUrl,
                release.PublishedAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failed(current, "The update check was cancelled.");
        }
        catch (Exception ex)
        {
            // Includes HttpRequestException, HttpClient timeouts (TaskCanceledException) and JsonException.
            _logger.LogWarning(ex, "Update check failed.");
            return Failed(current, ex is OperationCanceledException ? "The update check timed out." : $"The update check failed: {ex.Message}");
        }
    }

    private static HttpRequestMessage CreateRequest(Uri uri, string version)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        if (!request.Headers.UserAgent.TryParseAdd($"QuickQr/{version}"))
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("QuickQr", "0"));
        return request;
    }

    /// <summary>Compares two version strings; see <see cref="TryParseVersion"/> for the accepted syntax.</summary>
    internal static int CompareVersions(string left, string right)
    {
        TryParseVersion(left, out var l);
        TryParseVersion(right, out var r);
        return Compare(l, r);
    }

    /// <summary>
    /// Parses "v1.2.3", "1.2", "3.0.0-beta.1+build": optional leading v, pre-release/build suffix ignored,
    /// missing components are 0.
    /// </summary>
    internal static bool TryParseVersion(string? text, out int[] parts)
    {
        parts = [];
        var core = NormalizeVersion(text);
        if (core.Length == 0) return false;

        var segments = core.Split('.');
        var result = new int[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            if (!int.TryParse(segments[i], NumberStyles.None, CultureInfo.InvariantCulture, out result[i]))
                return false;
        }

        parts = result;
        return true;
    }

    private static string NormalizeVersion(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
        var cut = value.IndexOfAny(['-', '+']);
        return (cut >= 0 ? value[..cut] : value).Trim();
    }

    private static int Compare(int[] left, int[] right)
    {
        var length = Math.Max(left.Length, right.Length);
        for (var i = 0; i < length; i++)
        {
            var l = i < left.Length ? left[i] : 0;
            var r = i < right.Length ? right[i] : 0;
            if (l != r) return l.CompareTo(r);
        }

        return 0;
    }

    private bool IsThrottled(DateTimeOffset now, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero) return false;
        var stored = store.Get(LastCheckKey);
        if (!DateTimeOffset.TryParse(stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last)) return false;
        var elapsed = now - last;
        return elapsed >= TimeSpan.Zero && elapsed < interval;
    }

    private void RecordCheck(DateTimeOffset now) =>
        store.Set(LastCheckKey, now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

    private static UpdateCheckResult Failed(string current, string message) =>
        new(UpdateStatus.Failed, current, Error: message);
}

internal sealed record GitHubRelease
{
    [JsonPropertyName("tag_name")] public string? TagName { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("body")] public string? Body { get; init; }
    [JsonPropertyName("html_url")] public string? HtmlUrl { get; init; }
    [JsonPropertyName("draft")] public bool Draft { get; init; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; init; }
    [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; init; }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class UpdatesJsonContext : JsonSerializerContext;
