using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace OnlineSim.Api.Tests;

/// <summary>
/// Opt-in integration test against a LOCAL backend. Skipped unless both are set:
/// <c>ONLINESIM_IT_BASE=https://on.test/api/</c> and <c>ONLINESIM_IT_KEY=&lt;apikey&gt;</c>.
/// With <c>ONLINESIM_IT_MOCK=1</c> it also runs the number lifecycle on the provider-free <c>test</c> driver
/// (<c>spec/verify/backend_mock.php setup</c> first, a key of a user without webhook_url, teardown after).
/// </summary>
public class IntegrationTests
{
    private static readonly string? Base = Environment.GetEnvironmentVariable("ONLINESIM_IT_BASE");
    private static readonly string? Key = Environment.GetEnvironmentVariable("ONLINESIM_IT_KEY");
    private static readonly bool Enabled = !string.IsNullOrEmpty(Base) && !string.IsNullOrEmpty(Key);
    private static readonly bool Mock = Environment.GetEnvironmentVariable("ONLINESIM_IT_MOCK") == "1";

    /// <summary>Test-only transport: local backends use a self-signed certificate. Not part of the SDK API.</summary>
    private static readonly Lazy<HttpClient> Insecure = new Lazy<HttpClient>(() =>
    {
        var host = new Uri(Base!).Host;
        if (!(host == "localhost" || host == "127.0.0.1" || host.EndsWith(".test", StringComparison.Ordinal) || host.EndsWith(".localhost", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("refusing non-local integration host " + host);
        }
        var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        return new HttpClient(handler);
    });

    private static OnlineSimClient Client(string? key = null) =>
        new OnlineSimClient(new OnlineSimClientOptions { ApiKey = key ?? Key, BaseUrl = Base, Transport = new HttpClientTransport(Insecure.Value) });

    private static void RequireReadOnly() => Assert.SkipUnless(Enabled, "set ONLINESIM_IT_BASE and ONLINESIM_IT_KEY to run");

    private static void RequireMock() => Assert.SkipUnless(Enabled && Mock, "set ONLINESIM_IT_BASE, ONLINESIM_IT_KEY and ONLINESIM_IT_MOCK=1 to run");

    [Fact]
    public async Task ReadOnlyCalls()
    {
        RequireReadOnly();
        var client = Client();

        var balance = await client.User.BalanceAsync();
        Assert.True(balance.Zbalance >= 0);
        Assert.True((await client.User.ProfileAsync()).Id > 0);

        Assert.Equal(44, (await client.Numbers.TariffsOneAsync(44)).Code);
        Assert.NotNull(await client.Numbers.StateAsync());
        Assert.Equal(7, (await client.Rent.TariffsOneAsync(7)).Code);
        foreach (var rent in await client.Rent.StateAsync()) Assert.NotNull(rent.Extend);

        Assert.NotNull((await client.User.PaymentHistoryAsync()).Orders.Data);
        Assert.NotNull((await client.User.WebhookLogsAsync()).Data);
        Assert.NotNull((await client.Free.ListAsync()).Countries);
    }

    [Fact]
    public async Task WrongKeyIsApiException()
    {
        RequireReadOnly();
        var e = await Assert.ThrowsAsync<ApiException>(() => Client("wrong-key").User.BalanceAsync());
        Assert.Equal("ERROR_WRONG_KEY", e.Code);
    }

    [Fact]
    public async Task NumberLifecycleOnTheTestDriver()
    {
        RequireMock();
        const int country = 44;
        const string service = "google";
        var client = Client();

        Assert.NotNull(await client.Numbers.PriceAsync(service, country));
        var tzid = await client.Numbers.GetAsync(service, country);
        var order = await client.Numbers.GetWithNumberAsync(service, country);
        Assert.Matches(@"^\+?44\d+$", order.Number);
        Assert.Equal(country, order.Country);

        var ids = (await client.Numbers.StateAsync()).Select(o => o.Tzid).ToList();
        Assert.Contains(tzid, ids);
        Assert.Contains(order.Tzid, ids);
        Assert.StartsWith("TZ_", (await client.Numbers.StateOneAsync(tzid)).Response, StringComparison.Ordinal);

        // The test driver answers NO_NUMBER to any reject list: proves `reject` is sent.
        await Assert.ThrowsAsync<NoNumberException>(() => client.Numbers.GetAsync(service, country, new[] { "4470" }));

        // No SMS yet: the guard interval keeps it open.
        Assert.Equal("NO_COMPLETE_TZID", (await Assert.ThrowsAsync<ApiException>(() => client.Numbers.CloseAsync(order.Tzid))).Code);

        // Inject an SMS through the test driver callback (local backends only, no auth).
        var json = $$"""{"id":{{tzid}},"phoneFrom":"VK","text":"VK: 195895 is your code"}""";
        using var response = await Insecure.Value.PostAsync(new Uri(new Uri(Base!), "sms_in_getaways/test"), new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(200, (int)response.StatusCode);

        var code = await client.Numbers.WaitCodeAsync(tzid, new WaitCodeOptions { Interval = TimeSpan.FromSeconds(1), Timeout = TimeSpan.FromSeconds(30) });
        Assert.Equal("195895", code);
    }

    [Fact]
    public async Task WebhookUrlSetValidateClear()
    {
        RequireMock();
        var client = Client();
        await client.User.SetWebhookUrlAsync("https://example.com/hook");
        Assert.Equal("https://example.com/hook", (await client.User.ProfileAsync()).WebhookUrl);
        Assert.Equal("VALIDATION_ERROR", (await Assert.ThrowsAsync<ApiException>(() => client.User.SetWebhookUrlAsync("not a url"))).Code);
        await client.User.ClearWebhookUrlAsync();
        Assert.Null((await client.User.ProfileAsync()).WebhookUrl);
    }
}
