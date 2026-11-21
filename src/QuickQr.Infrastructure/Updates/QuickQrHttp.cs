using System.Net;

namespace QuickQr.Infrastructure.Updates;

/// <summary>
/// The one <see cref="HttpClient"/> shared by QuickQr's online services (registered as a keyed singleton).
/// Uses the default platform handler so Android/iOS keep their native TLS stacks.
/// </summary>
public static class QuickQrHttp
{
    /// <summary>DI key of the shared client: <c>[FromKeyedServices(QuickQrHttp.ClientKey)] HttpClient</c>.</summary>
    public const string ClientKey = "QuickQr.Http";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public static HttpClient CreateClient() => new() { Timeout = Timeout };
}

/// <summary>
/// Minimal resilience: one retry after a short delay on transient failures
/// (<see cref="HttpRequestException"/>, client timeout, HTTP 408/429/5xx). Caller cancellation is always honoured.
/// </summary>
internal static class HttpRetry
{
    public const int MaxRetries = 1;
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(1);

    /// <param name="createRequest">Called once per attempt: a request message can only be sent once.</param>
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        Func<HttpRequestMessage> createRequest,
        TimeSpan retryDelay,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var canRetry = attempt < MaxRetries;
            using (var request = createRequest())
            {
                try
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
                    if (!canRetry || !IsTransient(response.StatusCode)) return response;
                    response.Dispose();
                }
                catch (HttpRequestException) when (canRetry && !cancellationToken.IsCancellationRequested)
                {
                    // Network blip: retry once.
                }
                catch (TaskCanceledException) when (canRetry && !cancellationToken.IsCancellationRequested)
                {
                    // HttpClient.Timeout elapsed (not the caller's token): retry once.
                }
            }

            if (retryDelay > TimeSpan.Zero)
                await Task.Delay(retryDelay, clock, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;
}
