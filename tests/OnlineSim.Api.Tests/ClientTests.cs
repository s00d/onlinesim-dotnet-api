using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace OnlineSim.Api.Tests;

public class ClientTests
{
    private static readonly Reply Price = Reply.Json("""{"response":"1","price":1}""");

    [Fact]
    public async Task SendsKeyAsBearerHeaderNeverInQueryPlusLang()
    {
        var (client, fake) = Fake.Client(Reply.Json("""{"response":"1","price":0.36}"""));
        await client.Numbers.PriceAsync("vkcom", 44);
        var call = fake.Last;
        Assert.Equal("Bearer test-key", call.Headers["Authorization"]);
        Assert.Equal("application/json", call.Headers["Accept"]);
        Assert.Equal("GET", call.Method);
        Assert.Null(call.Body);
        var q = Fake.AllQuery(call);
        Assert.False(q.ContainsKey("apikey"));
        Assert.Equal("en", q["lang"]);
        Assert.False(q.ContainsKey("dev_id"));
    }

    [Fact]
    public async Task AppliesLangDevIdAndBaseUrlWithoutTrailingSlash()
    {
        var (client, fake) = Fake.Client(o =>
        {
            o.BaseUrl = "https://api.test/api";
            o.Lang = "ru";
            o.DevId = "42";
        }, Price);
        await client.Numbers.PriceAsync("vkcom");
        var url = fake.Last.Url;
        Assert.Equal("https://api.test/api/getPrice.php", url.GetLeftPart(UriPartial.Path));
        Assert.Equal(Fake.Map("service", "vkcom", "country", "7", "lang", "ru", "dev_id", "42"), Fake.AllQuery(fake.Last));
    }

    [Fact]
    public async Task SendsNoAuthorizationHeaderWithoutKey()
    {
        var (client, fake) = Fake.Client(o => o.ApiKey = null, Price);
        await client.Numbers.PriceAsync("vkcom");
        Assert.False(fake.Last.Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task UsesOnlineSimHostByDefault()
    {
        var (client, fake) = Fake.Client(o => o.BaseUrl = null, Price);
        await client.Numbers.PriceAsync("vkcom");
        Assert.StartsWith("https://onlinesim.host/api/getPrice.php?", fake.Last.Url.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsNonPositiveTimeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OnlineSimClient(new OnlineSimClientOptions { Timeout = TimeSpan.Zero }));
    }

    [Fact]
    public async Task MapsNoNumberToNoNumberException()
    {
        var (client, _) = Fake.Client(Reply.Json("""{"response":"NO_NUMBER"}"""));
        var e = await Assert.ThrowsAsync<NoNumberException>(() => client.Numbers.GetAsync("google", 44));
        Assert.IsAssignableFrom<ApiException>(e);
        Assert.Equal("NO_NUMBER", e.Code);
        Assert.Equal("No numbers available for this service right now", e.Message);
    }

    [Fact]
    public async Task MapsNoNumberForForwardToNoNumberException()
    {
        var (client, _) = Fake.Client(Reply.Json("""{"response":"NO_NUMBER_FOR_FORWARD"}"""));
        await Assert.ThrowsAsync<NoNumberException>(() => client.Numbers.GetAsync("google"));
    }

    [Fact]
    public async Task MapsHttp456WrongKeyToApiExceptionNotTransport()
    {
        var (client, _) = Fake.Client(Reply.Json("""{"response":"ERROR_WRONG_KEY"}""", 456));
        var e = await Assert.ThrowsAsync<ApiException>(() => client.User.BalanceAsync());
        Assert.Equal("ERROR_WRONG_KEY", e.Code);
        Assert.Equal("Wrong API key", e.Message);
    }

    [Fact]
    public async Task PrefersServerMessageButNotWhenItEchoesTheCode()
    {
        var (client, _) = Fake.Client(
            Reply.Json("""{"response":"TRY_AGAIN_LATER","message":"Maintenance until 12:00"}"""),
            Reply.Json("""{"response":"UNDEFINED_COUNTRY","message":"UNDEFINED_COUNTRY"}"""),
            Reply.Json("""{"response":"SOMETHING_NEW"}"""));
        var a = await Assert.ThrowsAsync<ApiException>(() => client.Numbers.GetAsync("x"));
        Assert.Equal(("TRY_AGAIN_LATER", "Maintenance until 12:00"), (a.Code, a.Message));
        var b = await Assert.ThrowsAsync<ApiException>(() => client.Numbers.GetAsync("x"));
        Assert.Equal(("UNDEFINED_COUNTRY", "Unknown country"), (b.Code, b.Message));
        var c = await Assert.ThrowsAsync<ApiException>(() => client.Numbers.GetAsync("x"));
        Assert.Equal(("SOMETHING_NEW", "SOMETHING_NEW"), (c.Code, c.Message));
    }

    [Fact]
    public async Task MapsValidationObjectToValidationError()
    {
        var (client, _) = Fake.Client(Reply.Json(
            """{"response":{"webhook_url":["The webhook url must be a valid URL.","Too long."],"name":["Required."]}}"""));
        var e = await Assert.ThrowsAsync<ApiException>(() => client.User.SetWebhookUrlAsync("nope"));
        Assert.Equal("VALIDATION_ERROR", e.Code);
        Assert.Equal("The webhook url must be a valid URL.; Too long.; Required.", e.Message);
    }

    [Fact]
    public async Task MapsNonJsonBodyToTransportExceptionWithStatusAndBody()
    {
        var (client, _) = Fake.Client(Reply.Raw("<html>Bad gateway</html>", 502));
        var e = await Assert.ThrowsAsync<TransportException>(() => client.User.BalanceAsync());
        Assert.Equal(502, e.Status);
        Assert.Equal("<html>Bad gateway</html>", e.Body);
    }

    [Fact]
    public async Task CutsTheBodyExcerptAt500Chars()
    {
        var (client, _) = Fake.Client(Reply.Raw(new string('x', 600), 500));
        var e = await Assert.ThrowsAsync<TransportException>(() => client.User.BalanceAsync());
        Assert.Equal(500, e.Body.Length);
    }

    [Fact]
    public async Task MapsJsonErrorStatusWithoutResponseToTransportException()
    {
        var (client, _) = Fake.Client(Reply.Json("""{"error":"boom"}""", 500));
        var e = await Assert.ThrowsAsync<TransportException>(() => client.User.BalanceAsync());
        Assert.Equal(500, e.Status);
    }

    [Fact]
    public async Task MapsNetworkFailureToTransportExceptionWithoutStatus()
    {
        var (client, _) = Fake.Client(Reply.Throw(new HttpRequestException("connection refused")));
        var e = await Assert.ThrowsAsync<TransportException>(() => client.User.BalanceAsync());
        Assert.Null(e.Status);
        Assert.Equal("Network error: connection refused", e.Message);
        Assert.IsType<HttpRequestException>(e.InnerException);
    }

    [Fact]
    public async Task TimeoutRaisesTransportException()
    {
        // The transport never answers on its own: only the client's timeout can end the request.
        var client = new OnlineSimClient(new OnlineSimClientOptions
        {
            ApiKey = "k",
            Timeout = TimeSpan.FromMilliseconds(1),
            Transport = new HangingTransport(),
        });
        var e = await Assert.ThrowsAsync<TransportException>(() => client.User.BalanceAsync());
        Assert.Null(e.Status);
        Assert.Equal("Request timed out after 1 ms", e.Message);
    }

    [Fact]
    public async Task CallerCancellationIsNotATimeout()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var client = new OnlineSimClient(new OnlineSimClientOptions { ApiKey = "k", Transport = new HangingTransport() });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.User.BalanceAsync(cts.Token));
    }

    [Fact]
    public async Task ReturnsBodiesWithoutResponseAsTheyAre()
    {
        var (client, _) = Fake.Client(Reply.Json("[]"));
        Assert.Empty(await client.Numbers.StateAsync());
    }

    [Fact]
    public async Task UnexpectedShapeIsTransportException()
    {
        var (client, _) = Fake.Client(Reply.Json("""{"response":1,"tzid":"not a number"}"""));
        await Assert.ThrowsAsync<TransportException>(() => client.Numbers.GetAsync("google"));
    }

    [Fact]
    public async Task HttpClientTransportSendsHeadersAndJsonBody()
    {
        var handler = new RecordingHandler();
        var client = new OnlineSimClient(new OnlineSimClientOptions
        {
            ApiKey = "k",
            BaseUrl = "https://api.test/api/",
            Transport = new HttpClientTransport(new HttpClient(handler)),
        });
        await client.User.SetWebhookUrlAsync("https://example.com/hook");
        var request = handler.Request!;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.test/api/profile.php?lang=en", request.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer k", request.Headers.Authorization!.ToString());
        Assert.Equal("application/json", request.Headers.Accept.ToString());
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("""{"profile":{"webhook_url":"https://example.com/hook"}}""", handler.Body);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        internal HttpRequestMessage? Request { get; private set; }

        internal string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("""{"response":"1"}""") };
        }
    }

    private sealed class HangingTransport : ITransport
    {
        public async Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }
}
