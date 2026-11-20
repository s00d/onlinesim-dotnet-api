using System.Linq;
using System.Threading.Tasks;

namespace OnlineSim.Api.Tests;

public class NumbersTests
{
    [Fact]
    public async Task Price()
    {
        var (client, fake) = Fake.Client(Reply.Json("""{"response":"1","price":0.36}"""));
        Assert.Equal(0.36m, await client.Numbers.PriceAsync("vkcom", 44));
        Assert.Equal("/api/getPrice.php", fake.Last.Url.AbsolutePath);
        Assert.Equal(Fake.Map("service", "vkcom", "country", "44"), Fake.Query(fake.Last));
    }

    [Fact]
    public async Task PriceIsNullWhenTheServerHasNone()
    {
        var (client, _) = Fake.Client(Reply.Json("""{"response":"1","price":null}"""));
        Assert.Null(await client.Numbers.PriceAsync("vkcom"));
    }

    [Fact]
    public async Task GetSendsNoNumberFlagAndRejectAsCommaList()
    {
        var (client, fake) = Fake.Client(Reply.Json("""{"response":1,"tzid":1520}"""));
        Assert.Equal(1520, await client.Numbers.GetAsync("google", 44, new[] { "4470", "4471" }));
        Assert.Equal("/api/getNum.php", fake.Last.Url.AbsolutePath);
        Assert.Equal(Fake.Map("service", "google", "country", "44", "reject", "4470,4471"), Fake.Query(fake.Last));
    }

    [Fact]
    public async Task GetDefaultsToCountry7AndOmitsEmptyReject()
    {
        var (client, fake) = Fake.Client(Reply.Json("""{"response":1,"tzid":1}"""));
        await client.Numbers.GetAsync("google", reject: new string[0]);
        Assert.Equal(Fake.Map("service", "google", "country", "7"), Fake.Query(fake.Last));
    }

    [Fact]
    public async Task GetWithNumberSendsNumberFlagAndReturnsTheOrder()
    {
        var (client, fake) = Fake.Client(Reply.Json(
            """{"response":1,"tzid":1520,"number":"+447459608320","country":44,"time":1200,"service":"google"}"""));
        var order = await client.Numbers.GetWithNumberAsync("google", 44);
        Assert.Equal((1520L, "+447459608320", 44, 1200L, "google"), (order.Tzid, order.Number, order.Country, order.Time, order.Service));
        Assert.Equal(Fake.Map("service", "google", "country", "44", "number", "1"), Fake.Query(fake.Last));
    }

    [Fact]
    public async Task StateSendsBooleansAs10LowercaseOrderbyAndNumberFlag()
    {
        var (client, fake) = Fake.Client(Reply.Json(Fake.Fixture("getState")));
        var ops = await client.Numbers.StateAsync(orderBy: SortOrder.Desc, msgList: true);
        Assert.Equal("/api/getState.php", fake.Last.Url.AbsolutePath);
        Assert.Equal(Fake.Map("message_to_code", "1", "orderby", "desc", "msg_list", "1", "number", "1"), Fake.Query(fake.Last));
        Assert.Equal(new[] { 594L, 593L }, ops.Select(o => o.Tzid));
        var first = ops[0];
        Assert.Equal("195895", first.Msg);
        Assert.Null(first.MsgList);
        Assert.Equal((OperationStatus.NumAnswer, "+77781746533", "wechat", 77, 0.3m, 0), (first.Response, first.Number, first.Service, first.Country, first.Sum, first.GuardIntervalRemainingSeconds));
        Assert.Null(ops[1].Msg);
        Assert.Equal(120, ops[1].GuardIntervalRemainingSeconds);
    }

    [Fact]
    public async Task StateDefaults()
    {
        var (client, fake) = Fake.Client(Reply.Json("[]"));
        await client.Numbers.StateAsync();
        Assert.Equal(Fake.Map("message_to_code", "1", "orderby", "asc", "msg_list", "0", "number", "1"), Fake.Query(fake.Last));
    }

    [Fact]
    public async Task StateReturnsEmptyOnErrorNoOperations()
    {
        var (client, _) = Fake.Client(Reply.Json("""{"response":"ERROR_NO_OPERATIONS"}"""));
        Assert.Empty(await client.Numbers.StateAsync());
    }

    [Fact]
    public async Task StateOneReturnsTheOperation()
    {
        var (client, fake) = Fake.Client(Reply.Json("""[{"tzid":594,"response":"TZ_NUM_ANSWER","msg":"Your code is 195895"}]"""));
        var op = await client.Numbers.StateOneAsync(594, messageToCode: false);
        Assert.Equal((594L, "Your code is 195895"), (op.Tzid, op.Msg));
        Assert.Equal(Fake.Map("tzid", "594", "message_to_code", "0", "msg_list", "0", "number", "1"), Fake.Query(fake.Last));
    }

    [Fact]
    public async Task StateOneReadsTheMsgList()
    {
        var (client, _) = Fake.Client(Reply.Json("""[{"tzid":1,"response":"TZ_NUM_ANSWER","msg":[{"service":"vk","msg":"1234"},{"service":"vk","msg":"5678"}]}]"""));
        var op = await client.Numbers.StateOneAsync(1, msgList: true);
        Assert.Null(op.Msg);
        Assert.Equal(new[] { ("vk", "1234"), ("vk", "5678") }, op.MsgList!.Select(s => (s.Service, s.Msg!)));
    }

    [Fact]
    public async Task StateOneRaisesErrorNoOperations()
    {
        var (client, _) = Fake.Client(Reply.Json("""{"response":"ERROR_NO_OPERATIONS"}"""), Reply.Json("[]"));
        Assert.Equal("ERROR_NO_OPERATIONS", (await Assert.ThrowsAsync<ApiException>(() => client.Numbers.StateOneAsync(1))).Code);
        Assert.Equal("ERROR_NO_OPERATIONS", (await Assert.ThrowsAsync<ApiException>(() => client.Numbers.StateOneAsync(1))).Code);
    }

    [Fact]
    public async Task Next()
    {
        var (client, fake) = Fake.Client(Reply.Json("""{"response":"1","tzid":"1520"}"""));
        await client.Numbers.NextAsync(1520);
        Assert.Equal("/api/setOperationRevise.php", fake.Last.Url.AbsolutePath);
        Assert.Equal(Fake.Map("tzid", "1520"), Fake.Query(fake.Last));
    }

    [Fact]
    public async Task Close()
    {
        var (client, fake) = Fake.Client(Reply.Json("""{"response":1,"tzid":1520}"""));
        await client.Numbers.CloseAsync(1520);
        Assert.Equal("/api/setOperationOk.php", fake.Last.Url.AbsolutePath);
        Assert.Equal(Fake.Map("tzid", "1520"), Fake.Query(fake.Last));
    }

    [Fact]
    public async Task Ban()
    {
        var (client, fake) = Fake.Client(Reply.Json("""{"response":1,"tzid":1520}"""));
        await client.Numbers.BanAsync(1520);
        Assert.Equal(Fake.Map("tzid", "1520", "ban", "1"), Fake.Query(fake.Last));
    }

    [Fact]
    public async Task BanAfterAnSmsAnswers0WhichIsAnApiExceptionWithAReadableMessage()
    {
        var (client, _) = Fake.Client(Reply.Json("""{"response":"0"}"""));
        var e = await Assert.ThrowsAsync<ApiException>(() => client.Numbers.BanAsync(1520));
        Assert.Equal(("0", "Ban not applied: the operation already has an SMS"), (e.Code, e.Message));
    }

    [Fact]
    public async Task NextAcceptsTheStringTzidAnswerErrorWrongTzidForAForeignTzid()
    {
        var (client, _) = Fake.Client(Reply.Json("""{"tzid":"1520","response":"1"}"""), Reply.Json("""{"response":"ERROR_WRONG_TZID"}"""));
        await client.Numbers.NextAsync(1520);
        Assert.Equal("ERROR_WRONG_TZID", (await Assert.ThrowsAsync<ApiException>(() => client.Numbers.NextAsync(1))).Code);
    }

    [Fact]
    public async Task StateOneAcceptsAnOperationWithoutResponse()
    {
        var (client, _) = Fake.Client(Reply.Json("""[{"tzid":5,"country":7,"service":"vkcom","sum":0.5,"time":900,"form":"index"}]"""));
        var op = await client.Numbers.StateOneAsync(5);
        Assert.Equal((5L, (string?)null), (op.Tzid, op.Response));
    }

    [Fact]
    public async Task CloseSurfacesNoCompleteTzid()
    {
        var (client, _) = Fake.Client(Reply.Json("""{"response":"NO_COMPLETE_TZID"}"""));
        Assert.Equal("NO_COMPLETE_TZID", (await Assert.ThrowsAsync<ApiException>(() => client.Numbers.CloseAsync(1))).Code);
    }

    [Fact]
    public async Task TariffsRequestsCountryAllAndNormalisesEmptyServices()
    {
        var uk = Fake.Fixture("getNumbersStats_44");
        var (client, fake) = Fake.Client(Reply.Json($$$"""{"44":{{{uk}}},"7":{"name":"russia","position":7,"code":7,"other":false,"new":false,"enabled":true,"services":[]}}"""));
        var all = await client.Numbers.TariffsAsync();
        Assert.Equal("/api/getNumbersStats.php", fake.Last.Url.AbsolutePath);
        Assert.Equal(Fake.Map("country", "all"), Fake.Query(fake.Last));
        Assert.Equal("vkcom", all[44].Services["service_vkcom"].Slug);
        Assert.Empty(all[7].Services);
        Assert.Null(all[7].Other);
    }

    [Fact]
    public async Task TariffsOne()
    {
        var (client, fake) = Fake.Client(Reply.Json(Fake.Fixture("getNumbersStats_44")));
        var uk = await client.Numbers.TariffsOneAsync(44);
        Assert.Equal(Fake.Map("country", "44"), Fake.Query(fake.Last));
        Assert.Equal((44, "united_kingdom", "United Kingdom", 0.63m, true), (uk.Code, uk.Name, uk.LocaleName, uk.Other, uk.Enabled));
        var vk = uk.Services["service_vkcom"];
        Assert.Equal((363, false, 44, 0.36m, 1L, "VK", "vkcom"), (vk.Count, vk.Popular, vk.Code, vk.Price, vk.Id, vk.Service, vk.Slug));
    }

    [Fact]
    public async Task TariffsOneReadsUnknownStockAsNull()
    {
        var (client, _) = Fake.Client(Reply.Json("""{"code":1,"services":{"service_x":{"count":"?","price":"0.5","slug":"x"}}}"""));
        var x = (await client.Numbers.TariffsOneAsync(1)).Services["service_x"];
        Assert.Null(x.Count);
        Assert.Equal(0.5m, x.Price);
    }

    [Fact]
    public async Task TariffsOneRaisesNoCountry()
    {
        var (client, _) = Fake.Client(Reply.Json("""{"response":"NO_COUNTRY"}"""));
        Assert.Equal("NO_COUNTRY", (await Assert.ThrowsAsync<ApiException>(() => client.Numbers.TariffsOneAsync(999))).Code);
    }
}
