using System.Net;
using System.Text;

namespace QuickQr.Infrastructure.Tests.Fakes;

/// <summary>Records requests (with their bodies) and answers them from a delegate.</summary>
public sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    private readonly List<RecordedRequest> _requests = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<RecordedRequest> Requests
    {
        get { lock (_gate) return [.. _requests]; }
    }

    public static FakeHttpMessageHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));

    public static FakeHttpMessageHandler Status(HttpStatusCode status) =>
        new(_ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(string.Empty) }));

    public static FakeHttpMessageHandler Throws(Exception exception) => new(_ => Task.FromException<HttpResponseMessage>(exception));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_gate) _requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.ToString(), body));
        cancellationToken.ThrowIfCancellationRequested();
        return await respond(request);
    }
}

public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Headers, string? Body);
