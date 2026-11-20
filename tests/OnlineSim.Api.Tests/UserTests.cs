using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace OnlineSim.Api.Tests;

public class UserTests
{
    [Fact]
    public async Task BalanceSendsIncomeAndParsesTheDecimalString()
    {
        var (client, fake) = Fake.Client(Reply.Json(Fake.Fixture("getBalance")));
        var b = await client.User.BalanceAsync();
        Assert.Equal((1442.33m, 1023.41m, (decimal?)9.78m), (b.Balance, b.Zbalance, b.Income));
        Assert.Equal("/api/getBalance.php", fake.Last.Url.AbsolutePath);
        Assert.Equal(Fake.Map("income", "1"), Fake.Query(fake.Last));
    }

    [Fact]
    public async Task BalanceWithoutIncome()
    {
        var (client, _) = Fake.Client(Reply.Json("""{"response":"1","balance":"0.000","zbalance":0}"""));
        var b = await client.User.BalanceAsync();
        Assert.Equal((0m, 0m, (decimal?)null), (b.Balance, b.Zbalance, b.Income));
    }

    [Fact]
    public async Task Profile()
    {
        var (client, fake) = Fake.Client(Reply.Json(Fake.Fixture("getProfile")));
        var p = await client.User.ProfileAsync();
        Assert.Equal("/api/getProfile.php", fake.Last.Url.AbsolutePath);
        Assert.Equal((1L, "https://example.com/hook", "user@example.com", 1), (p.Id, p.WebhookUrl, p.Email, p.NumberCountry));
        Assert.Equal("333", p.NumberReject!.Value[0].GetString());
        Assert.Equal(JsonValueKind.True, p.ApiAccess!.Value.ValueKind);
        Assert.Null(p.ResetCount);
    }

    [Fact]
    public async Task ProfileDecodesNumberAsIntOrNullAndCountryAsStringOrNull()
    {
        var (client, _) = Fake.Client(
            Reply.Json("""{"response":"1","profile":{"id":1,"number":79001234567,"country":"RU"}}"""),
            Reply.Json("""{"response":"1","profile":{"id":1,"number":null,"country":null}}"""),
            Reply.Json("""{"response":"1","profile":{"id":1,"number":"79001234567","country":7}}"""));
        var p = await client.User.ProfileAsync();
        Assert.Equal(((long?)79001234567, "RU"), (p.Number, p.Country));
        p = await client.User.ProfileAsync();
        Assert.Equal(((long?)null, (string?)null), (p.Number, p.Country));
        p = await client.User.ProfileAsync(); // lenient: a numeric string, a number
        Assert.Equal(((long?)79001234567, "7"), (p.Number, p.Country));
    }

    [Fact]
    public async Task PaymentHistoryKeepsAPaylistThatArrivesAsAJsonArrayNullableOrderFields()
    {
        var body = JsonNode.Parse(Fake.Fixture("getPaymentHistory"))!.AsObject();
        body["paylist"] = JsonNode.Parse("""[{"id":0,"name":"Card"},{"id":1,"name":"Crypto"}]""");
        body["orders"]!["data"] = JsonNode.Parse("""[{"id":1,"sum":"10.00","currency":null,"form_text":null,"stat":0,"created_at":"2026-06-14 09:12:12"}]""");
        var (client, _) = Fake.Client(Reply.Json(body.ToJsonString()));
        var h = await client.User.PaymentHistoryAsync();
        Assert.Equal(new[] { "0", "1" }, h.Paylist.Keys.OrderBy(k => k));
        Assert.Equal("Crypto", h.Paylist["1"].GetProperty("name").GetString());
        Assert.Equal(((string?)null, (string?)null), (h.Orders.Data[0].Currency, h.Orders.Data[0].FormText));
    }

    [Fact]
    public async Task PaymentHistoryNormalisesTheEmptyPaylist()
    {
        var (client, fake) = Fake.Client(Reply.Json(Fake.Fixture("getPaymentHistory")));
        var h = await client.User.PaymentHistoryAsync(2);
        Assert.Equal("/api/getPaymentHistory.php", fake.Last.Url.AbsolutePath);
        Assert.Equal(Fake.Map("page", "2"), Fake.Query(fake.Last));
        Assert.Empty(h.Paylist);
        var order = h.Orders.Data[0];
        Assert.Equal((9462L, "89.00", "USD", 2), (order.Id, order.Sum, order.Currency, order.Stat));
        Assert.Equal("cashout", order.Extra!["merchant_name"].GetString());
        Assert.Equal((106, 11), (h.Orders.Total, h.Orders.LastPage));
        Assert.Equal(1m, h.Currency!["USD"]);
    }

    [Fact]
    public async Task SetWebhookUrlPostsJson()
    {
        var (client, fake) = Fake.Client(Reply.Json("""{"response":"1","user":{"id":2}}"""));
        await client.User.SetWebhookUrlAsync("https://example.com/hook");
        var call = fake.Last;
        Assert.Equal("POST", call.Method);
        Assert.Equal("/api/profile.php", call.Url.AbsolutePath);
        Assert.Equal("application/json", call.Headers["Content-Type"]);
        Assert.Equal("Bearer test-key", call.Headers["Authorization"]);
        Assert.Equal("""{"profile":{"webhook_url":"https://example.com/hook"}}""", call.Body);
    }

    [Fact]
    public async Task ClearWebhookUrlPostsNull()
    {
        var (client, fake) = Fake.Client(Reply.Json("""{"response":"1","user":{"id":2}}"""));
        await client.User.ClearWebhookUrlAsync();
        Assert.Equal("POST", fake.Last.Method);
        Assert.Equal("""{"profile":{"webhook_url":null}}""", fake.Last.Body);
    }

    [Fact]
    public async Task WebhookLogsReturnsThePaginator()
    {
        var (client, fake) = Fake.Client(Reply.Json("""
            {"response":"1","data":{"current_page":1,"data":[{"id":7,"user_id":2,"webhook_url":"https://example.com/hook",
            "type":"receiving_sms","params":{"webhook_type":"receiving_sms","operation_id":1520,"code":"195895"},
            "status":"success","error":null,"created_at":"2026-11-19 10:00:01","updated_at":"2026-11-19 10:00:01"}],
            "from":1,"to":1,"last_page":1,"per_page":10,"total":1}}
            """));
        var page = await client.User.WebhookLogsAsync();
        Assert.Equal("/api/webhook-logs", fake.Last.Url.AbsolutePath);
        Assert.Equal(Fake.Map("page", "1"), Fake.Query(fake.Last));
        Assert.Equal((1, 1, 10, 1, (int?)1), (page.CurrentPage, page.Total, page.PerPage, page.LastPage, page.From));
        var log = page.Data[0];
        Assert.Equal((7L, 2L, "receiving_sms", "success", (string?)null), (log.Id, log.UserId, log.Type, log.Status, log.Error));
        Assert.Equal((Webhook.ReceivingSms, (long?)1520, "195895", (string?)null), (log.Params.WebhookType, log.Params.OperationId, log.Params.Code, log.Params.Number));
    }
}
