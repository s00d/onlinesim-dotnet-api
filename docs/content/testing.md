---
title: Testing
description: Test your integration offline with MockOnlineSim — no network, key or money.
navigation:
  order: 6
---

`MockOnlineSim` is an in-process fake of the OnlineSim API that ships with the package. It is an `ITransport` that answers like the real backend — same shapes, same error codes, HTTP 456 for a wrong key — and keeps a balance, operations and rents. Your code talks to a normal `OnlineSimClient`, so nothing in it changes for tests. It is deterministic: no randomness, no clock, numbers and ids come from a counter. It is thread-safe.

## Setup

```csharp
using OnlineSim.Api;

var mock = new MockOnlineSim(new MockOptions { Balance = 10 });
var client = mock.Client(); // an OnlineSimClient wired to the mock
```

| `MockOptions` | Default | Description |
|---|---|---|
| `ApiKey` | `"mock-key"` | Key the mock accepts. `mock.Client()` uses it; `mock.Client(new OnlineSimClientOptions { ApiKey = "x" })` tests a wrong key. |
| `Balance` | `100` | Starting balance, USD (`decimal`). |
| `Prices` | `{7: {vkcom: 0.5, telegram: 1.2, google: 0.3}, 44: {google: 0.2, vkcom: 0.36}}` | Number prices, `Dictionary<int, Dictionary<string, decimal>>` (country → service → USD); replaces the defaults. Also feeds `Numbers.TariffsAsync()`. |
| `RentTariffs` | `{7: {Days: {1: 1, 3: 2.5, 7: 5}, Extend: 1}}` | Rent prices, `Dictionary<int, MockRentTariff>`; `Extend = 0` makes rents of that country non-extendable. |
| `GuardInterval` | `TimeSpan.Zero` | Above zero: an operation without SMS cannot be closed (`NO_COMPLETE_TZID`). |

```csharp
using System.Collections.Generic;
using OnlineSim.Api;

var mock = new MockOnlineSim(new MockOptions
{
    Prices = new Dictionary<int, Dictionary<string, decimal>> { [44] = new Dictionary<string, decimal> { ["google"] = 0.25m } },
    GuardInterval = TimeSpan.FromSeconds(120),
});
```

`mock.Client(options?)` takes the usual [client options](/#client-options); `Transport` is ignored. To build the client yourself, set `mock.Transport` as the transport; any `BaseUrl` works:

```csharp
using OnlineSim.Api;

var mock = new MockOnlineSim();
var client = new OnlineSimClient(new OnlineSimClientOptions { ApiKey = "mock-key", Transport = mock.Transport });
```

## A full example

Order a number, let an SMS arrive while `WaitCodeAsync` polls, check the code and the money. `AddSmsAfter` delivers the SMS after a number of polls instead of after some time, so `WaitCodeAsync` runs with a zero interval and the test takes milliseconds. xUnit here; any test framework works the same.

```csharp
using System;
using System.Threading.Tasks;
using OnlineSim.Api;
using Xunit;

public class OnlineSimTests
{
    [Fact]
    public async Task ReceivesTheCode()
    {
        var mock = new MockOnlineSim(new MockOptions { Balance = 10 });
        var client = mock.Client();

        var order = await client.Numbers.GetWithNumberAsync("google", 44);
        Assert.Equal("+44900000001", order.Number);

        mock.AddSmsAfter(order.Tzid, "Your Google code: 123-456", 2); // the third poll sees it
        var code = await client.Numbers.WaitCodeAsync(order.Tzid, new WaitCodeOptions { Interval = TimeSpan.Zero });

        Assert.Equal("123456", code);
        Assert.Equal("TZ_OVER_OK", mock.Operations()[0].Status); // WaitCodeAsync closed it
        Assert.Equal(9.8m, mock.Balance()); // the price is spent once an SMS arrived
    }
}
```

The `Msg` of an operation is the code by default: the first run of 4–8 digits, with spaces and dashes between digits removed (`123-456` → `123456`); a text without such a run comes back whole. `messageToCode: false` (or `FullMessage` in `WaitCodeOptions`) gives the full text.

## Errors

`FailNext(code)` makes the next request answer `{"response": code}` — the client turns it into `ApiException` (or `NoNumberException`) exactly as for the real server. Pass `endpoint` to fail only that endpoint. `FailNextTransport()` makes the next request fail with a non-JSON HTTP 502 (`TransportException`).

```csharp
using System.Threading.Tasks;
using OnlineSim.Api;
using Xunit;

public class ErrorTests
{
    [Fact]
    public async Task HandlesAMissingNumberAndANetworkHiccup()
    {
        var mock = new MockOnlineSim();
        var client = mock.Client();

        mock.FailNext("NO_NUMBER", endpoint: "getNum.php");
        await Assert.ThrowsAsync<NoNumberException>(() => client.Numbers.GetAsync("google"));

        mock.FailNextTransport(503);
        await Assert.ThrowsAsync<TransportException>(() => client.User.BalanceAsync());

        Assert.Equal(100m, (await client.User.BalanceAsync()).Balance); // one-shot
    }
}
```

The state itself produces the real error codes too: `WARNING_LOW_BALANCE` when the balance is short, `NO_NUMBER` when `reject` matches the next number, `UNDEFINED_COUNTRY` / `ERROR_NO_SERVICE` for unknown prices, `ERROR_WRONG_TZID` for an unknown or closed operation, `TRY_AGAIN_LATER` when extending a non-extendable rent, `ERROR_WRONG_KEY` (HTTP 456) for a wrong key.

## Inspecting requests

`mock.Requests` lists every request received: `Method`, `Path` (relative to `/api/`), `Query` (decoded), `Body` (the JSON of a POST).

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using OnlineSim.Api;
using Xunit;

public class RequestTests
{
    [Fact]
    public async Task SendsRejectAsACommaList()
    {
        var mock = new MockOnlineSim();
        await mock.Client().Numbers.GetAsync("google", 44, reject: new[] { "4470", "4471" });

        var request = Assert.Single(mock.Requests);
        Assert.Equal(("GET", "getNum.php"), (request.Method, request.Path));
        Assert.Equal(new Dictionary<string, string> { ["service"] = "google", ["country"] = "44", ["reject"] = "4470,4471", ["lang"] = "en" }, request.Query);
    }
}
```

## Control API

| Member | Description |
|---|---|
| `Client(options?)` | A normal `OnlineSimClient` wired to the mock. |
| `Transport` | The fake `ITransport` (the mock itself). |
| `AddSms(tzid, text, service?)` | An SMS arrives now at a number operation or a rent (detected by `tzid`). Throws `InvalidOperationException` for an unknown or closed one. |
| `AddSmsAfter(tzid, text, polls, service?)` | The SMS arrives after `polls` state requests for `tzid` (`Numbers.StateAsync`/`StateOneAsync`, `Rent.StateAsync`/`StateOneAsync`). |
| `FailNext(code, endpoint?)` | The next request (to `endpoint` only, e.g. `"getNum.php"`) answers `{"response": code}`. |
| `FailNextTransport(status = 502, endpoint?)` | The next request fails with a non-JSON body and HTTP `status`. |
| `SetBalance(usd)` / `Balance()` | Set / read the total balance, USD (it includes the part frozen by open operations, as `User.BalanceAsync()` reports it). |
| `Operations()` / `Rents()` | Snapshots of the current number operations / open rents, for assertions. |
| `Requests` | Log of the requests received (a copy). |
| `Reset()` | Back to the constructor's state. |

## What the mock does

- **Numbers**: `GetAsync` freezes the price; `CloseAsync` after an SMS spends it, without one refunds it; `BanAsync` after an SMS answers `"0"`, like the server. Numbers are `+<country>9000000NN`, ids count from 1. `NextAsync` switches to the following SMS. The operation's `Time` stays at 1200 s — there is no clock.
- **Rent**: `GetAsync` charges at once and keeps the `extension` flag; `ExtendAsync` charges again; `CloseAsync` removes the rent. SMS appear in `Messages`, newest first.
- **User**: `BalanceAsync` (frozen part included), a fixed profile, `SetWebhookUrlAsync` validates `http(s)` URLs (`VALIDATION_ERROR` otherwise), `WebhookLogsAsync` lists the deliveries the mock *recorded* when an SMS arrived while a webhook URL was set — it makes no HTTP call. `PaymentHistoryAsync` is empty.
- **Free**: `Free.ListAsync` returns two fixed countries with one number and a few SMS each.
- Not imitated: the server's 5 s lock after `setOperationOk` (`INTERVAL_CONCURRENT_REQUESTS_ERROR`) and anything else that needs real time.
