using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuickQr.Core.Platform;
using QuickQr.Core.Security;
using QuickQr.Infrastructure.Updates;

namespace QuickQr.Infrastructure.Security;

/// <summary>
/// URL reputation via the Google Safe Browsing Lookup API v4 (<c>threatMatches:find</c>), with a 30-minute in-memory cache.
/// </summary>
/// <remarks>
/// The Lookup API is licensed for non-commercial use only. A commercial build must use Google Web Risk
/// (through a server-side proxy that holds the key) by registering another <see cref="IUrlReputationService"/>
/// before <c>AddQuickQrData()</c>; this adapter is registered with TryAdd semantics.
/// </remarks>
public sealed class SafeBrowsingReputationService(
    [FromKeyedServices(QuickQrHttp.ClientKey)] HttpClient httpClient,
    IOptions<QuickQrInfrastructureOptions> options,
    IAppInfoProvider appInfo,
    TimeProvider clock,
    ILogger<SafeBrowsingReputationService>? logger = null) : IUrlReputationService
{
    internal const string ProviderName = "Google Safe Browsing";
    internal const string Endpoint = "https://safebrowsing.googleapis.com/v4/threatMatches:find";
    internal const int MaxCacheEntries = 512;
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    internal static readonly string[] ThreatTypes = ["MALWARE", "SOCIAL_ENGINEERING", "UNWANTED_SOFTWARE", "POTENTIALLY_HARMFUL_APPLICATION"];

    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;
    private readonly ConcurrentDictionary<string, (ReputationResult Result, DateTimeOffset ExpiresAt)> _cache = new(StringComparer.Ordinal);

    /// <summary>Delay before the single retry of a transient failure.</summary>
    internal TimeSpan RetryDelay { get; init; } = HttpRetry.DefaultDelay;

    public async Task<ReputationResult> CheckAsync(string url, CancellationToken cancellationToken = default)
    {
        var apiKey = options.Value.SafeBrowsingApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) return Result(ReputationVerdict.NotConfigured);

        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return Result(ReputationVerdict.Unknown);

        var key = uri.AbsoluteUri;
        var now = clock.GetUtcNow();
        if (_cache.TryGetValue(key, out var cached) && cached.ExpiresAt > now) return cached.Result;

        try
        {
            var body = new ThreatMatchesRequest(
                new ClientInfo("quickqr", appInfo.Version),
                new ThreatInfo(ThreatTypes, ["ANY_PLATFORM"], ["URL"], [new ThreatEntry(key)]));
            var endpoint = new Uri($"{Endpoint}?key={Uri.EscapeDataString(apiKey.Trim())}");

            using var response = await HttpRetry.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(body, SafeBrowsingJsonContext.Default.ThreatMatchesRequest) },
                RetryDelay,
                clock,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Safe Browsing returned HTTP {Status}.", (int)response.StatusCode);
                return Result(ReputationVerdict.Unknown);
            }

            var payload = await response.Content.ReadFromJsonAsync(SafeBrowsingJsonContext.Default.ThreatMatchesResponse, cancellationToken).ConfigureAwait(false);
            var threats = (payload?.Matches ?? [])
                .Select(m => m.ThreatType)
                .OfType<string>()
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var result = threats.Count > 0
                ? new ReputationResult(ReputationVerdict.Malicious, threats, ProviderName)
                : Result(ReputationVerdict.Clean);
            Remember(key, result, now);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Network errors, timeouts and malformed JSON: the verdict is simply unknown (and not cached).
            _logger.LogWarning(ex, "Safe Browsing lookup failed.");
            return Result(ReputationVerdict.Unknown);
        }
    }

    private void Remember(string key, ReputationResult result, DateTimeOffset now)
    {
        if (_cache.Count >= MaxCacheEntries)
        {
            foreach (var (k, v) in _cache)
                if (v.ExpiresAt <= now) _cache.TryRemove(k, out _);
            if (_cache.Count >= MaxCacheEntries) _cache.Clear();
        }

        _cache[key] = (result, now + CacheDuration);
    }

    private static ReputationResult Result(ReputationVerdict verdict) => new(verdict, [], ProviderName);
}

internal sealed record ThreatMatchesRequest(ClientInfo Client, ThreatInfo ThreatInfo);

internal sealed record ClientInfo(string ClientId, string ClientVersion);

internal sealed record ThreatInfo(
    IReadOnlyList<string> ThreatTypes,
    IReadOnlyList<string> PlatformTypes,
    IReadOnlyList<string> ThreatEntryTypes,
    IReadOnlyList<ThreatEntry> ThreatEntries);

internal sealed record ThreatEntry(string Url);

internal sealed record ThreatMatchesResponse(IReadOnlyList<ThreatMatch>? Matches);

internal sealed record ThreatMatch(string? ThreatType, string? PlatformType, string? ThreatEntryType);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ThreatMatchesRequest))]
[JsonSerializable(typeof(ThreatMatchesResponse))]
internal sealed partial class SafeBrowsingJsonContext : JsonSerializerContext;
