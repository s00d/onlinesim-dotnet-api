using System.Text.Json;
using System.Threading.Tasks;

namespace OnlineSim.Api.Tests;

public class FreeTests
{
    [Fact]
    public async Task ListHasNoPhpSuffixAndOnlyTheSetParameters()
    {
        var (client, fake) = Fake.Client(Reply.Json(Fake.Fixture("getFreeList")));
        var list = await client.Free.ListAsync(country: 44, number: "7459608320", count: 20);
        Assert.Equal("/api/getFreeList", fake.Last.Url.AbsolutePath);
        Assert.Equal(Fake.Map("country", "44", "number", "7459608320", "count", "20"), Fake.Query(fake.Last));
        Assert.Equal("447459608320", list.Numbers["7459608320"].FullNumber);
        Assert.Equal("195895", list.Messages.Data[0].Code);
        Assert.Equal(("7459608320", 44, 426, (string?)null), (list.Messages.Number, list.Messages.Country, list.Messages.Total, list.Messages.FirstPageUrl));
        Assert.Equal(new[] { 44, 380 }, System.Linq.Enumerable.Select(list.Countries, c => c.Country));
    }

    [Fact]
    public async Task ListAcceptsNullCountryTextAndCountryOriginal()
    {
        var (client, _) = Fake.Client(Reply.Json("""{"response":1,"countries":[{"country":999,"country_text":null,"country_original":null}],"numbers":[],"messages":{"data":[]}}"""));
        var c = (await client.Free.ListAsync()).Countries[0];
        Assert.Equal((999, (string?)null, (string?)null), (c.Country, c.CountryText, c.CountryOriginal));
    }

    [Fact]
    public async Task ListNormalisesEmptyNumbers()
    {
        var (client, fake) = Fake.Client(Reply.Json("""{"response":1,"countries":[],"numbers":[],"messages":{"data":[]}}"""));
        var list = await client.Free.ListAsync();
        Assert.Empty(list.Numbers);
        Assert.Empty(Fake.Query(fake.Last));
    }
}

public class WebhookTests
{
    [Fact]
    public void ParsesAReceivingSmsJsonString()
    {
        var hook = Webhook.Parse("""
            {"webhook_type":"receiving_sms","operation_id":1520,"user_id":2,"country_code":7,"number":"+79001234567",
             "sender":"VK","message":"VK: 195895 is your code","code":"195895","time_start":"2026-11-19 10:00:00","time_left":1100}
            """);
        Assert.Equal(Webhook.ReceivingSms, hook.WebhookType);
        Assert.Equal((1520L, 2L, 7), (hook.OperationId, hook.UserId, hook.CountryCode));
        Assert.Equal(("+79001234567", "VK", "VK: 195895 is your code", "195895"), (hook.Number, hook.Sender, hook.Message, hook.Code));
        Assert.Equal(("2026-11-19 10:00:00", 1100L), (hook.TimeStart, hook.TimeLeft));
    }

    [Fact]
    public void IsLenientNumericStringsMissingUnknownFieldsAndTypes()
    {
        using var doc = JsonDocument.Parse("""{"webhook_type":"rent_sms","operation_id":"430","code":2991,"time_left":"soon","extra":true}""");
        var hook = Webhook.Parse(doc.RootElement);
        Assert.Equal(Webhook.RentSms, hook.WebhookType);
        Assert.Equal(430L, hook.OperationId);
        Assert.Equal("2991", hook.Code);
        Assert.Null(hook.UserId);
        Assert.Null(hook.CountryCode);
        Assert.Null(hook.Number);
        Assert.Null(hook.Sender);
        Assert.Null(hook.Message);
        Assert.Null(hook.TimeStart);
        Assert.Null(hook.TimeLeft);
        Assert.Null(Webhook.Parse("""{"webhook_type":"other"}""").WebhookType);
    }

    [Fact]
    public void ThrowsOnlineSimExceptionOnlyForInputThatIsNotAJsonObject()
    {
        Assert.Throws<OnlineSimException>(() => Webhook.Parse("not json"));
        Assert.Contains("not a JSON object", Assert.Throws<OnlineSimException>(() => Webhook.Parse("[1,2]")).Message, System.StringComparison.Ordinal);
        Assert.Throws<OnlineSimException>(() => Webhook.Parse("null"));
    }
}
