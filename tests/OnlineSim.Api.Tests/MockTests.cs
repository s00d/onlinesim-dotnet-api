using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace OnlineSim.Api.Tests;

/// <summary>MockOnlineSim through the real client: every endpoint, every control helper.</summary>
public class MockTests
{
    private static readonly WaitCodeOptions NoSleep = new WaitCodeOptions { Interval = TimeSpan.Zero };

    private static async Task<string> Code(Func<Task> action) => (await Assert.ThrowsAnyAsync<ApiException>(action)).Code;

    /// <summary>A raw GET through the mock transport: the HTTP status and the JSON body as the backend would send it.</summary>
    private static async Task<(int Status, JsonNode Body)> Raw(MockOnlineSim mock, string pathAndQuery, string? key = "mock-key")
    {
        var headers = new Dictionary<string, string>();
        if (key != null) headers["Authorization"] = "Bearer " + key;
        var res = await mock.Transport.SendAsync(new TransportRequest("GET", new Uri("https://x.test/api/" + pathAndQuery), headers, null), CancellationToken.None);
        return (res.Status, JsonNode.Parse(res.Body)!);
    }

    // ----- auth and transport -----

    [Fact]
    public async Task AWrongKeyIs456AMissingKeyHttp200BothMapToApiException()
    {
        var mock = new MockOnlineSim();
        foreach (var key in new[] { "nope", "" })
        {
            var e = await Assert.ThrowsAsync<ApiException>(() => mock.Client(new OnlineSimClientOptions { ApiKey = key }).User.BalanceAsync());
            Assert.Equal("ERROR_WRONG_KEY", e.Code);
        }
        var missing = await Raw(mock, "getBalance.php", key: null);
        Assert.Equal((200, """{"response":"ERROR_WRONG_KEY"}"""), (missing.Status, missing.Body.ToJsonString()));
        Assert.Equal(456, (await Raw(mock, "getBalance.php", key: "nope")).Status);
    }

    [Fact]
    public async Task TariffsAndFreeNumbersNeedNoKeyButASentWrongKeyIsStill456()
    {
        var mock = new MockOnlineSim();
        var client = mock.Client(new OnlineSimClientOptions { ApiKey = "" });
        Assert.Equal(44, (await client.Numbers.TariffsOneAsync(44)).Code);
        Assert.Equal(7, (await client.Rent.TariffsOneAsync(7)).Code);
        Assert.Equal(44, (await client.Free.ListAsync()).Messages.Country);
        Assert.Equal("ERROR_WRONG_KEY", await Code(() => mock.Client(new OnlineSimClientOptions { ApiKey = "nope" }).Free.ListAsync()));
        Assert.Equal(456, (await Raw(mock, "getFreeList", key: "nope")).Status);
    }

    [Fact]
    public async Task WorksAsTheTransportOfAClientBuiltByHandWithAnyBaseUrl()
    {
        var mock = new MockOnlineSim(new MockOptions { ApiKey = "k", Balance = 5 });
        var client = new OnlineSimClient(new OnlineSimClientOptions { ApiKey = "k", Transport = mock.Transport });
        var b = await client.User.BalanceAsync();
        Assert.Equal((5m, 0m, (decimal?)0m), (b.Balance, b.Zbalance, b.Income));
        var r = Assert.Single(mock.Requests);
        Assert.Equal(("GET", "getBalance.php", (string?)null), (r.Method, r.Path, r.Body));
        Assert.Equal(Fake.Map("income", "1", "lang", "en"), r.Query);
    }

    [Fact]
    public async Task AnUnknownEndpointIsANonJson404()
    {
        var mock = new MockOnlineSim();
        var res = await mock.Transport.SendAsync(
            new TransportRequest("GET", new Uri("https://x.test/api/nope.php"), new Dictionary<string, string>(), null), CancellationToken.None);
        Assert.Equal(404, res.Status);
    }

    // ----- numbers -----

    [Fact]
    public async Task PriceNumberNullForUnknownServiceUndefinedCountryForUnknownCountry()
    {
        var client = new MockOnlineSim().Client();
        Assert.Equal(1.2m, await client.Numbers.PriceAsync("telegram", 7));
        Assert.Null(await client.Numbers.PriceAsync("nope", 7));
        Assert.Equal("UNDEFINED_COUNTRY", await Code(() => client.Numbers.PriceAsync("google", 999)));
    }

    [Fact]
    public async Task GetFreezesThePriceNumbersAreDeterministicGetWithNumberReturnsTheOrder()
    {
        var mock = new MockOnlineSim();
        var client = mock.Client();
        Assert.Equal(1, await client.Numbers.GetAsync("google", 44));
        var order = await client.Numbers.GetWithNumberAsync("vkcom");
        Assert.Equal((2L, "+7900000002", 7, 1200L, "vkcom"), (order.Tzid, order.Number, order.Country, order.Time, order.Service));
        Assert.Equal(("vkcom", (int?)0), (order.Title, order.GuardIntervalRemainingSeconds));
        Assert.Equal(new[] { false, true }, mock.Requests.Select(r => r.Query.ContainsKey("number")));
        var b = await client.User.BalanceAsync();
        Assert.Equal((100m, 0.7m), (b.Balance, b.Zbalance));
        Assert.Equal(
            new[] { (1L, "+44900000001", "TZ_NUM_WAIT"), (2L, "+7900000002", "TZ_NUM_WAIT") },
            mock.Operations().Select(o => (o.Tzid, o.Number, o.Status)));
    }

    [Fact]
    public async Task GetErrorsUnknownCountryServiceLowBalanceReject()
    {
        var mock = new MockOnlineSim(new MockOptions { Balance = 0.25m });
        var client = mock.Client();
        Assert.Equal("UNDEFINED_COUNTRY", await Code(() => client.Numbers.GetAsync("google", 999)));
        Assert.Equal("ERROR_NO_SERVICE", await Code(() => client.Numbers.GetAsync("nope", 7)));
        Assert.Equal("WARNING_LOW_BALANCE", await Code(() => client.Numbers.GetAsync("google", 7)));
        await Assert.ThrowsAsync<NoNumberException>(() => client.Numbers.GetAsync("google", 44, new[] { "4490", "7" }));
        Assert.Equal("4490,7", mock.Requests.Last().Query["reject"]);
        // a non-matching reject passes; the rejected attempt did not consume a number
        Assert.Equal("+44900000001", (await client.Numbers.GetWithNumberAsync("google", 44, new[] { "4480" })).Number);
        // 0.25 - 0.2 frozen = 0.05 available
        Assert.Equal("WARNING_LOW_BALANCE", await Code(() => client.Numbers.GetAsync("google", 44)));
    }

    [Fact]
    public async Task StateEmptyListOneAndTheStatusFlow()
    {
        var mock = new MockOnlineSim();
        var client = mock.Client();
        Assert.Empty(await client.Numbers.StateAsync());
        var a = await client.Numbers.GetAsync("google", 44);
        var b = await client.Numbers.GetAsync("vkcom", 7);
        var one = await client.Numbers.StateOneAsync(a);
        Assert.Equal((a, "TZ_NUM_WAIT", "+44900000001", (decimal?)0.2m, (int?)0), (one.Tzid, one.Response, one.Number, one.Sum, one.GuardIntervalRemainingSeconds));
        Assert.Null(one.Msg);

        mock.AddSms(a, "Your Google code is G-123 456. Do not share it.");
        var answered = await client.Numbers.StateOneAsync(a);
        Assert.Equal(("TZ_NUM_ANSWER", "123456"), (answered.Response, answered.Msg));
        Assert.Equal("Your Google code is G-123 456. Do not share it.", (await client.Numbers.StateOneAsync(a, messageToCode: false)).Msg);
        Assert.Equal(new[] { b, a }, (await client.Numbers.StateAsync(orderBy: SortOrder.Desc)).Select(o => o.Tzid));

        await client.Numbers.CloseAsync(a);
        var closed = await client.Numbers.StateOneAsync(a);
        Assert.Equal(("TZ_OVER_OK", (long?)0), (closed.Response, closed.Time));
        Assert.Equal("ERROR_NO_OPERATIONS", await Code(() => client.Numbers.StateOneAsync(999)));
    }

    [Fact]
    public async Task TextWithoutCodeGivesFullTextNextSwitchesMsgListListsAll()
    {
        var mock = new MockOnlineSim();
        var client = mock.Client();
        var tzid = await client.Numbers.GetAsync("telegram");
        mock.AddSms(tzid, "Welcome!", service: "telegram");
        Assert.Equal("Welcome!", (await client.Numbers.StateOneAsync(tzid)).Msg);
        await client.Numbers.NextAsync(tzid);
        var waiting = await client.Numbers.StateOneAsync(tzid);
        Assert.Equal("TZ_NUM_WAIT", waiting.Response);
        Assert.Null(waiting.Msg);
        mock.AddSms(tzid, "code 98765");
        Assert.Equal("98765", (await client.Numbers.StateOneAsync(tzid)).Msg);
        var all = (await client.Numbers.StateOneAsync(tzid, msgList: true)).MsgList!;
        Assert.Equal(new[] { ("telegram", "Welcome!"), ("telegram", "98765") }, all.Select(s => (s.Service, s.Msg!)));
        Assert.Single(mock.Requests, r => r.Path == "setOperationRevise.php");
    }

    [Fact]
    public async Task NextAnswersTheStringTzidErrorWrongTzidForAnUnknownOneErrorNoTzidFor0()
    {
        var mock = new MockOnlineSim();
        var client = mock.Client();
        var tzid = await client.Numbers.GetAsync("google", 44);
        var raw = await Raw(mock, "setOperationRevise.php?tzid=" + tzid);
        Assert.Equal("""{"tzid":"1","response":"1"}""", raw.Body.ToJsonString());
        Assert.Equal("ERROR_WRONG_TZID", await Code(() => client.Numbers.NextAsync(424242)));
        Assert.Equal("ERROR_NO_TZID", await Code(() => client.Numbers.NextAsync(0)));
        Assert.Equal("ERROR_NO_TZID", await Code(() => client.Numbers.CloseAsync(0)));
    }

    [Fact]
    public async Task CloseAfterSmsSpendsWithoutOneRefundsBanAfterSmsAnswers0()
    {
        var mock = new MockOnlineSim(new MockOptions { Balance = 10 });
        var client = mock.Client();
        var paid = await client.Numbers.GetAsync("telegram");
        var empty = await client.Numbers.GetAsync("telegram");
        mock.AddSms(paid, "1111");
        Assert.Equal("0", await Code(() => client.Numbers.BanAsync(paid)));
        await client.Numbers.CloseAsync(paid);
        await client.Numbers.CloseAsync(empty);
        var b = await client.User.BalanceAsync();
        Assert.Equal((8.8m, 0m), (b.Balance, b.Zbalance));
        Assert.Equal(8.8m, mock.Balance());
        Assert.Equal("ERROR_WRONG_TZID", await Code(() => client.Numbers.CloseAsync(paid)));
        Assert.Equal("ERROR_WRONG_TZID", await Code(() => client.Numbers.CloseAsync(999)));
        var banned = await client.Numbers.GetAsync("google");
        await client.Numbers.BanAsync(banned);
        Assert.Equal("TZ_OVER_OK", mock.Operations().Single(o => o.Tzid == banned).Status);
    }

    [Fact]
    public async Task GuardIntervalAnOperationWithoutSmsCannotBeClosed()
    {
        var mock = new MockOnlineSim(new MockOptions { GuardInterval = TimeSpan.FromSeconds(120) });
        var client = mock.Client();
        var tzid = await client.Numbers.GetAsync("google");
        Assert.Equal(120, (await client.Numbers.StateOneAsync(tzid)).GuardIntervalRemainingSeconds);
        Assert.Equal("NO_COMPLETE_TZID", await Code(() => client.Numbers.CloseAsync(tzid)));
        mock.AddSms(tzid, "5555");
        Assert.Equal(0, (await client.Numbers.StateOneAsync(tzid)).GuardIntervalRemainingSeconds);
        await client.Numbers.CloseAsync(tzid);
    }

    [Fact]
    public async Task WaitCodeWithAddSmsAfterAndAZeroIntervalNoClockNoRealWaiting()
    {
        var mock = new MockOnlineSim();
        var client = mock.Client();
        var tzid = await client.Numbers.GetAsync("google", 44);
        mock.AddSmsAfter(tzid, "Your code: 4321", 2);
        Assert.Equal("4321", await client.Numbers.WaitCodeAsync(tzid, new WaitCodeOptions { Interval = TimeSpan.Zero }));
        Assert.Equal(new[] { "getNum.php", "getState.php", "getState.php", "getState.php", "setOperationOk.php" }, mock.Requests.Select(r => r.Path));
        Assert.Equal("TZ_OVER_OK", mock.Operations()[0].Status);
    }

    [Fact]
    public async Task WaitCodeKeepOpenAndSinceWaitForTheNextSmsPollsListingAllCountToo()
    {
        var mock = new MockOnlineSim();
        var client = mock.Client();
        var tzid = await client.Numbers.GetAsync("google", 44);
        mock.AddSmsAfter(tzid, "1111", 0);
        var first = await client.Numbers.WaitCodeAsync(tzid, new WaitCodeOptions { Interval = TimeSpan.Zero, KeepOpen = true });
        mock.AddSmsAfter(tzid, "2222", 1);
        await client.Numbers.StateAsync(); // one poll of every operation
        Assert.Equal("2222", await client.Numbers.WaitCodeAsync(tzid, new WaitCodeOptions { Interval = TimeSpan.Zero, Since = first }));
    }

    [Fact]
    public async Task TariffsFromThePricesServiceSlugKeysCount100NoCountryForAnUnknownOne()
    {
        var client = new MockOnlineSim().Client();
        var all = await client.Numbers.TariffsAsync();
        Assert.Equal(new[] { 7, 44 }, all.Keys.OrderBy(k => k));
        var vk = all[44].Services["service_vkcom"];
        Assert.Equal((100, false, 44, 0.36m, 2L, "vkcom", "vkcom"), (vk.Count, vk.Popular, vk.Code, vk.Price, vk.Id, vk.Service, vk.Slug));
        Assert.Equal("NO_COUNTRY", await Code(() => client.Numbers.TariffsOneAsync(1)));
        var empty = new MockOnlineSim(new MockOptions { Prices = new Dictionary<int, Dictionary<string, decimal>> { [1] = new Dictionary<string, decimal>() } }).Client();
        Assert.Empty((await empty.Numbers.TariffsOneAsync(1)).Services); // `[]` on the wire
    }

    // ----- rent -----

    [Fact]
    public async Task RentGetReservesTheFirstSmsConfirmsAndChargesExtendChargesAtOnceClose()
    {
        var mock = new MockOnlineSim(new MockOptions { Balance = 10 });
        var client = mock.Client();
        var rent = await client.Rent.GetAsync(7, 3);
        Assert.Equal((1L, "900000001", 0, (bool?)false, 2.5m, 0, 20L, (long?)1200), (rent.Tzid, rent.Number, rent.Status, rent.Checked, rent.Sum, rent.Days, rent.Time, rent.TimeSeconds));
        Assert.Equal(((int?)3, (int?)3, 1, (int?)0), (rent.Rent, rent.DaysAutoextend, rent.Extension, rent.GuardIntervalRemainingSeconds));
        Assert.Empty(rent.Messages);
        Assert.Equal(new Dictionary<int, decimal> { [1] = 1m, [3] = 2.5m, [7] = 5m }, rent.Extend);
        // reserved: the total balance stays, the price is frozen
        Assert.Equal(10m, mock.Balance());
        var b = await client.User.BalanceAsync();
        Assert.Equal((10m, 2.5m), (b.Balance, b.Zbalance));
        Assert.Equal("TRY_AGAIN_LATER", await Code(() => client.Rent.ExtendAsync(rent.Tzid))); // not confirmed yet

        mock.AddSms(rent.Tzid, "Bank: 777 888", service: "bank");
        mock.AddSms(rent.Tzid, "no code here");
        var state = await client.Rent.StateOneAsync(rent.Tzid);
        Assert.Equal((1, (bool?)true, 3, 3 * 1440L, (long?)(3 * 86400)), (state.Status, state.Checked, state.Days, state.Time, state.TimeSeconds));
        Assert.Equal(new[] { ("other", ""), ("bank", "777888") }, state.Messages.Select(m => (m.Service, m.Code))); // newest first
        Assert.Equal(7.5m, mock.Balance());
        b = await client.User.BalanceAsync();
        Assert.Equal((7.5m, 0m), (b.Balance, b.Zbalance));

        var extended = await client.Rent.ExtendAsync(rent.Tzid, 1);
        Assert.Equal((4, (int?)4, (int?)3, 3.5m), (extended.Days, extended.Rent, extended.DaysAutoextend, extended.Sum));
        Assert.Equal(6.5m, mock.Balance());
        Assert.Equal("TRY_AGAIN_LATER", await Code(() => client.Rent.ExtendAsync(rent.Tzid, 2))); // unknown period
        Assert.Equal("TRY_AGAIN_LATER", await Code(() => client.Rent.ExtendAsync(999))); // unknown tzid

        var fixedRent = await client.Rent.GetAsync(7, 1, extension: false);
        mock.AddSms(fixedRent.Tzid, "1234");
        var fixedState = await client.Rent.StateOneAsync(fixedRent.Tzid);
        Assert.Equal(0, fixedState.Extension);
        Assert.Empty(fixedState.Extend);
        Assert.Equal("TRY_AGAIN_LATER", await Code(() => client.Rent.ExtendAsync(fixedRent.Tzid)));
        Assert.Equal(new[] { rent.Tzid, fixedRent.Tzid }, (await client.Rent.StateAsync()).Select(r => r.Tzid));

        await client.Rent.CloseAsync(fixedRent.Tzid); // confirmed: no refund
        Assert.Equal(5.5m, mock.Balance());
        Assert.Equal(new[] { rent.Tzid }, mock.Rents().Select(r => r.Tzid));
        Assert.Equal("ERROR_NO_OPERATIONS", await Code(() => client.Rent.StateOneAsync(fixedRent.Tzid)));
        Assert.Equal("ERROR_NO_OPERATIONS", await Code(() => client.Rent.CloseAsync(fixedRent.Tzid)));
    }

    [Fact]
    public async Task RentSumIsANumberInGetRentNumExtendRentStateAndADecimalStringInGetRentStateOnTheWire()
    {
        var mock = new MockOnlineSim();
        Assert.Equal("2.5", (await Raw(mock, "rent/getRentNum.php?days=3")).Body["item"]!["sum"]!.ToJsonString());
        Assert.Equal("\"2.50\"", (await Raw(mock, "rent/getRentState.php")).Body["list"]![0]!["sum"]!.ToJsonString());
    }

    [Fact]
    public async Task AnUnconfirmedRentCloseReleasesTheReservationNotifySmsHasCode0AndConfirmsNothingGuardInterval()
    {
        var mock = new MockOnlineSim(new MockOptions { Balance = 10, GuardInterval = TimeSpan.FromSeconds(120) });
        var client = mock.Client();
        var rent = await client.Rent.GetAsync(7, 3);
        Assert.Equal(120, rent.GuardIntervalRemainingSeconds);
        Assert.Equal("NO_COMPLETE_TZID", await Code(() => client.Rent.CloseAsync(rent.Tzid)));
        mock.AddSms(rent.Tzid, "Messages from this sender are not accepted", service: "notify");
        var state = await client.Rent.StateOneAsync(rent.Tzid);
        Assert.Equal((0, (bool?)false, (int?)0), (state.Status, state.Checked, state.GuardIntervalRemainingSeconds));
        Assert.Equal(("notify", "0"), (state.Messages[0].Service, state.Messages[0].Code));
        Assert.Equal("0", (await Raw(mock, "rent/getRentState.php")).Body["list"]![0]!["messages"]![0]!["code"]!.ToJsonString()); // the integer 0
        await client.Rent.CloseAsync(rent.Tzid);
        Assert.Equal(10m, mock.Balance()); // released, nothing charged
        var b = await client.User.BalanceAsync();
        Assert.Equal((10m, 0m), (b.Balance, b.Zbalance));
    }

    [Fact]
    public async Task RentErrorsUnknownCountryUnknownDaysLowBalance()
    {
        var client = new MockOnlineSim(new MockOptions { Balance = 1.5m }).Client();
        Assert.Equal("UNDEFINED_COUNTRY", await Code(() => client.Rent.GetAsync(44)));
        Assert.Equal("UNDEFINED_DAYS", await Code(() => client.Rent.GetAsync(7, 2)));
        Assert.Equal("WARNING_LOW_BALANCE", await Code(() => client.Rent.GetAsync(7, 3)));
    }

    [Fact]
    public async Task AddSmsAfterWorksForRentsTariffsEmptyForUnknownCountry()
    {
        var mock = new MockOnlineSim();
        var client = mock.Client();
        var tzid = (await client.Rent.GetAsync()).Tzid;
        mock.AddSmsAfter(tzid, "6060", 1);
        Assert.Empty((await client.Rent.StateOneAsync(tzid)).Messages);
        var listed = (await client.Rent.StateAsync())[0];
        Assert.Equal((1, "6060", "other"), (listed.Status, listed.Messages[0].Code, listed.Messages[0].Service));
        var tariffs = await client.Rent.TariffsAsync();
        var ru = Assert.Single(tariffs).Value;
        Assert.Equal((7, true, "russia", false, 7, 1, (bool?)false), (ru.Code, ru.Enabled, ru.Name, ru.New, ru.Position, ru.Extend, ru.Confirm));
        Assert.Equal(new Dictionary<int, decimal> { [1] = 1m, [3] = 2.5m, [7] = 5m }, ru.Days);
        Assert.Equal(new Dictionary<int, int> { [1] = 100, [3] = 100, [7] = 100 }, ru.Count);
        Assert.Equal(ru.Count, ru.ExtCounts!);
        Assert.Equal("NO_COUNTRY", await Code(() => client.Rent.TariffsOneAsync(44)));
    }

    // ----- user and free -----

    [Fact]
    public async Task ProfileWebhookSetValidateClearLogsRecordedWhenAnSmsArrives()
    {
        var mock = new MockOnlineSim(new MockOptions { ApiKey = "secret" });
        var client = mock.Client();
        var profile = await client.User.ProfileAsync();
        Assert.Equal((1L, "secret", (string?)null), (profile.Id, profile.Apikey, profile.WebhookUrl));
        var invalid = await Assert.ThrowsAsync<ApiException>(() => client.User.SetWebhookUrlAsync("not a url"));
        Assert.Equal(("VALIDATION_ERROR", "The webhook url must be a valid URL."), (invalid.Code, invalid.Message));
        await client.User.SetWebhookUrlAsync("ftp://x"); // any scheme://… URL
        await client.User.SetWebhookUrlAsync("https://example.com/hook");
        var post = mock.Requests.Last();
        Assert.Equal(("POST", "profile.php", """{"profile":{"webhook_url":"https://example.com/hook"}}"""), (post.Method, post.Path, post.Body));
        Assert.Equal("https://example.com/hook", (await client.User.ProfileAsync()).WebhookUrl);

        var tzid = await client.Numbers.GetAsync("google", 44);
        mock.AddSms(tzid, "code 1234");
        var rent = await client.Rent.GetAsync();
        mock.AddSms(rent.Tzid, "code 5678");
        mock.AddSms(rent.Tzid, "no code");
        var logs = await client.User.WebhookLogsAsync();
        Assert.Equal((3, 1, 10), (logs.Total, logs.CurrentPage, logs.PerPage));
        Assert.Equal(
            new[] { ("rent_sms", (long?)rent.Tzid, ""), ("rent_sms", (long?)rent.Tzid, "5678"), ("receiving_sms", (long?)tzid, "1234") },
            logs.Data.Select(l => (l.Type, l.Params.OperationId, l.Params.Code!)));
        // activation: +number, seconds left, order time; rent: digits without +, minutes left, SMS time
        var activation = logs.Data[2].Params;
        Assert.Equal(("+44900000001", (long?)1200, "2026-01-01 00:00:01"), (activation.Number, activation.TimeLeft, activation.TimeStart));
        var rentSms = logs.Data[1].Params;
        Assert.Equal(("7900000002", (long?)1440, "2026-01-01 00:00:03"), (rentSms.Number, rentSms.TimeLeft, rentSms.TimeStart));

        await client.User.ClearWebhookUrlAsync();
        Assert.Null((await client.User.ProfileAsync()).WebhookUrl);
    }

    [Fact]
    public async Task BalanceIncludesTheFrozenPartPaymentHistoryIsEmpty()
    {
        var mock = new MockOnlineSim(new MockOptions { Balance = 3 });
        var client = mock.Client();
        await client.Numbers.GetAsync("telegram");
        var b = await client.User.BalanceAsync();
        Assert.Equal((3m, 1.2m, (decimal?)0m), (b.Balance, b.Zbalance, b.Income));
        var history = await client.User.PaymentHistoryAsync();
        Assert.Empty(history.Paylist);
        Assert.Equal("Card", history.Forms!["1"]);
        Assert.Equal(new[] { 5m, 10m, 20m }, history.Buttons!["USD"]);
        Assert.Equal(new[] { "onlinesim.io" }, history.Domains!);
        Assert.Empty(history.Orders.Data);
        Assert.Equal((0, 1), (history.Orders.Total, history.Orders.CurrentPage));
    }

    [Fact]
    public async Task FreeListCountriesNumbersKeyedByTheShortNumberMessagesOfTheSelectedNumber()
    {
        var client = new MockOnlineSim().Client();
        var uk = await client.Free.ListAsync();
        Assert.Equal(new[] { 44, 7 }, uk.Countries.Select(c => c.Country));
        Assert.Equal(new[] { "7459608320" }, uk.Numbers.Keys);
        Assert.Equal(("7459608320", 44, 2, 10), (uk.Messages.Number, uk.Messages.Country, uk.Messages.Total, uk.Messages.PerPage));
        Assert.Equal(("195895", "2026-01-01T00:00:00.000000Z"), (uk.Messages.Data[0].Code, uk.Messages.Data[0].CreatedAt));
        Assert.Equal(10, (await client.Free.ListAsync(count: 0)).Messages.PerPage); // clamped to 10..500
        Assert.Equal(500, (await client.Free.ListAsync(count: 9999)).Messages.PerPage);
        Assert.Equal(44, (await client.Free.ListAsync(country: 999)).Messages.Country); // unknown → the first country
        var raw = (await Raw(new MockOnlineSim(), "getFreeList", key: null)).Body;
        Assert.Equal(("1", "null"), (raw["response"]!.ToJsonString(), raw["content"]?.ToJsonString() ?? "null"));
        Assert.True(raw.AsObject().ContainsKey("content"));
        Assert.Equal(System.Text.Json.JsonValueKind.String, raw["ignore"]!.GetValueKind());
        var ru = await client.Free.ListAsync(country: 7);
        Assert.Equal(("51423", "9001234567"), (ru.Messages.Data[0].Code, ru.Messages.Data[0].MyNumber));
    }

    // ----- control API -----

    [Fact]
    public async Task FailNextAnswersAnErrorCodeOnceOptionallyOnlyForOneEndpoint()
    {
        var mock = new MockOnlineSim();
        var client = mock.Client();
        mock.FailNext("NO_NUMBER", endpoint: "getNum.php");
        Assert.Equal(100m, (await client.User.BalanceAsync()).Balance); // other endpoint: untouched
        await Assert.ThrowsAsync<NoNumberException>(() => client.Numbers.GetAsync("google"));
        Assert.Equal(1, await client.Numbers.GetAsync("google"));
        mock.FailNext("TRY_AGAIN_LATER");
        Assert.Equal("TRY_AGAIN_LATER", await Code(() => client.User.ProfileAsync()));
    }

    [Fact]
    public async Task FailNextTransportFailsOnceWithANonJsonBodyWaitCodeRetriesIt()
    {
        var mock = new MockOnlineSim();
        var client = mock.Client();
        mock.FailNextTransport();
        Assert.Equal(502, (await Assert.ThrowsAsync<TransportException>(() => client.User.BalanceAsync())).Status);

        var tzid = await client.Numbers.GetAsync("google");
        mock.AddSms(tzid, "3030");
        mock.FailNextTransport(503, endpoint: "getState.php");
        Assert.Equal("3030", await client.Numbers.WaitCodeAsync(tzid, NoSleep));
    }

    [Fact]
    public async Task AddSmsRejectsUnknownIdsSetBalanceRequestsLogReset()
    {
        var mock = new MockOnlineSim(new MockOptions { Balance = 1 });
        var client = mock.Client(new OnlineSimClientOptions { Lang = "ru" });
        Assert.Contains("no open operation or rent 5", Assert.Throws<InvalidOperationException>(() => mock.AddSms(5, "x")).Message, StringComparison.Ordinal);
        Assert.Contains("no operation or rent 5", Assert.Throws<InvalidOperationException>(() => mock.AddSmsAfter(5, "x", 1)).Message, StringComparison.Ordinal);
        mock.SetBalance(50);
        Assert.Equal(50m, mock.Balance());
        var tzid = await client.Numbers.GetAsync("telegram");
        await client.Numbers.CloseAsync(tzid);
        Assert.Throws<InvalidOperationException>(() => mock.AddSms(tzid, "late")); // closed
        var first = mock.Requests[0];
        Assert.Equal(("GET", "getNum.php", (string?)null), (first.Method, first.Path, first.Body));
        Assert.Equal(Fake.Map("service", "telegram", "country", "7", "lang", "ru"), first.Query);

        mock.FailNext("NO_NUMBER");
        mock.Reset();
        Assert.Equal(1m, mock.Balance());
        Assert.Empty(mock.Operations());
        Assert.Empty(mock.Rents());
        Assert.Empty(mock.Requests);
        Assert.Equal(1, await client.Numbers.GetAsync("google", 44)); // ids restart, failure queue cleared
    }

    [Fact]
    public async Task OperationsReturnsSnapshots()
    {
        var mock = new MockOnlineSim();
        var client = mock.Client();
        var tzid = await client.Numbers.GetAsync("google");
        var before = mock.Operations()[0];
        mock.AddSms(tzid, "1234");
        Assert.Equal("TZ_NUM_WAIT", before.Status);
        Assert.Empty(before.Messages);
        Assert.Equal("TZ_NUM_ANSWER", mock.Operations()[0].Status);
    }

    [Fact]
    public async Task ConcurrentRequestsKeepTheStateConsistent()
    {
        var mock = new MockOnlineSim(new MockOptions { Balance = 1000 });
        var client = mock.Client();
        var ids = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => client.Numbers.GetAsync("google", 44))));
        Assert.Equal(Enumerable.Range(1, 50).Select(i => (long)i), ids.OrderBy(i => i));
        Assert.Equal(10m, (await client.User.BalanceAsync()).Zbalance);
    }
}
