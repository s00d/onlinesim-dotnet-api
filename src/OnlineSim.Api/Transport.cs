using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OnlineSim.Api;

/// <summary>
/// Sends one HTTP request. The default is <see cref="HttpClientTransport"/>; tests use a fake or
/// <see cref="MockOnlineSim"/>. A transport only moves bytes: status codes, JSON and errors are the client's job.
/// </summary>
public interface ITransport
{
    /// <summary>Send <paramref name="request"/> and return the status and body. Throw on network failure.</summary>
    Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken);
}

/// <summary>One HTTP request built by <see cref="OnlineSimClient"/>.</summary>
public sealed class TransportRequest
{
    /// <summary>Creates the request.</summary>
    public TransportRequest(string method, Uri url, IReadOnlyDictionary<string, string> headers, string? body)
    {
        Method = method;
        Url = url;
        Headers = headers;
        Body = body;
    }

    /// <summary><c>GET</c> or <c>POST</c>.</summary>
    public string Method { get; }

    /// <summary>Absolute URL including the query string.</summary>
    public Uri Url { get; }

    /// <summary>Request headers (<c>Accept</c>, <c>Authorization</c>, <c>Content-Type</c> for a body).</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>JSON body of a POST, <c>null</c> for GET.</summary>
    public string? Body { get; }
}

/// <summary>HTTP status and body text of a response.</summary>
public sealed class TransportResponse
{
    /// <summary>Creates the response.</summary>
    public TransportResponse(int status, string body)
    {
        Status = status;
        Body = body;
    }

    /// <summary>HTTP status code.</summary>
    public int Status { get; }

    /// <summary>Response body as text.</summary>
    public string Body { get; }
}

/// <summary>
/// The default transport, on <see cref="HttpClient"/>. Pass your own <see cref="HttpClient"/> for a proxy,
/// custom TLS or <c>IHttpClientFactory</c>; the client applies its own per-request timeout on top.
/// </summary>
public sealed class HttpClientTransport : ITransport
{
    // One shared HttpClient for every client built without a transport: no socket exhaustion.
    private static readonly Lazy<HttpClient> SharedClient = new Lazy<HttpClient>(CreateSharedClient);

    private readonly HttpClient _http;

    /// <summary>Creates the transport on <paramref name="httpClient"/>, or on a shared default client when <c>null</c>.</summary>
    public HttpClientTransport(HttpClient? httpClient = null)
    {
        _http = httpClient ?? SharedClient.Value;
    }

    // The client applies its own timeout per request. On .NET 8 pooled connections are recycled so DNS changes are seen.
    private static HttpClient CreateSharedClient()
    {
#if NET5_0_OR_GREATER
        var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        return new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
#else
        return new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
#endif
    }

    /// <inheritdoc />
    public async Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);
        string? contentType = null;
        foreach (var header in request.Headers)
        {
            if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) contentType = header.Value;
            else message.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        if (request.Body != null)
        {
            message.Content = new StringContent(request.Body, Encoding.UTF8);
            message.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType ?? "application/json");
        }

        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
#if NET5_0_OR_GREATER
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
        return new TransportResponse((int)response.StatusCode, body);
    }
}
