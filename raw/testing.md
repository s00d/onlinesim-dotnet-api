# Testing

> Test your integration offline with MockOnlineSim — no network, key or money.

`MockOnlineSim` is an in-process fake of the OnlineSim API that ships with the package. It is an `ITransport` that answers like the real backend — same shapes, same error codes, HTTP 456 for a wrong key — and keeps a balance, operations and rents. Your code talks to a normal `OnlineSimClient`, so nothing in it changes for tests. It is deterministic: no randomness, no clock, numbers and ids come from a counter. It is thread-safe.

## Setup

```csharp
using OnlineSim.Api;

var mock = new MockOnlineSim(new MockOptions { Balance = 10 });
var client = mock.Client(); // an OnlineSimClient wired to the mock
```

<table>
<thead>
  <tr>
    <th>
      <code>
        MockOptions
      </code>
    </th>
    
    <th>
      Default
    </th>
    
    <th>
      Description
    </th>
  </tr>
</thead>

<tbody>
  <tr>
    <td>
      <code>
        ApiKey
      </code>
    </td>
    
    <td>
      <code>
        "mock-key"
      </code>
    </td>
    
    <td>
      Key the mock accepts. <code>
        mock.Client()
      </code>
      
       uses it; <code>
        mock.Client(new OnlineSimClientOptions { ApiKey = "x" })
      </code>
      
       tests a wrong key.
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        Balance
      </code>
    </td>
    
    <td>
      <code>
        100
      </code>
    </td>
    
    <td>
      Starting balance, USD (<code>
        decimal
      </code>
      
      ).
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        Prices
      </code>
    </td>
    
    <td>
      <code>
        {7: {vkcom: 0.5, telegram: 1.2, google: 0.3}, 44: {google: 0.2, vkcom: 0.36}}
      </code>
    </td>
    
    <td>
      Number prices, <code>
        Dictionary<int, Dictionary<string, decimal>>
      </code>
      
       (country → service → USD); replaces the defaults. Also feeds <code>
        Numbers.TariffsAsync()
      </code>
      
      .
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        RentTariffs
      </code>
    </td>
    
    <td>
      <code>
        {7: {Days: {1: 1, 3: 2.5, 7: 5}, Extend: 1}}
      </code>
    </td>
    
    <td>
      Rent prices, <code>
        Dictionary<int, MockRentTariff>
      </code>
      
      ; <code>
        Extend = 0
      </code>
      
       makes rents of that country non-extendable.
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        GuardInterval
      </code>
    </td>
    
    <td>
      <code>
        TimeSpan.Zero
      </code>
    </td>
    
    <td>
      Above zero: an operation without SMS, or an unconfirmed rent without any SMS, cannot be closed (<code>
        NO_COMPLETE_TZID
      </code>
      
      ).
    </td>
  </tr>
</tbody>
</table>

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

The state itself produces the real error codes too: `WARNING_LOW_BALANCE` when the balance is short, `NO_NUMBER` when `reject` matches the next number, `UNDEFINED_COUNTRY` / `ERROR_NO_SERVICE` for unknown prices, `ERROR_WRONG_TZID` for an unknown or closed operation (`NextAsync` too; `ERROR_NO_TZID` for tzid `0`), `UNDEFINED_COUNTRY` / `UNDEFINED_DAYS` for an unknown rent country or period, `TRY_AGAIN_LATER` for every refused `Rent.ExtendAsync`, `ERROR_NO_OPERATIONS` for an unknown rent in `Rent.CloseAsync`, `ERROR_WRONG_KEY` for a wrong key (HTTP 456) or no key (HTTP 200).

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

<table>
<thead>
  <tr>
    <th>
      Member
    </th>
    
    <th>
      Description
    </th>
  </tr>
</thead>

<tbody>
  <tr>
    <td>
      <code>
        Client(options?)
      </code>
    </td>
    
    <td>
      A normal <code>
        OnlineSimClient
      </code>
      
       wired to the mock.
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        Transport
      </code>
    </td>
    
    <td>
      The fake <code>
        ITransport
      </code>
      
       (the mock itself).
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        AddSms(tzid, text, service?)
      </code>
    </td>
    
    <td>
      An SMS arrives now at a number operation or a rent (detected by <code>
        tzid
      </code>
      
      ). Throws <code>
        InvalidOperationException
      </code>
      
       for an unknown or closed one.
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        AddSmsAfter(tzid, text, polls, service?)
      </code>
    </td>
    
    <td>
      The SMS arrives after <code>
        polls
      </code>
      
       state requests for <code>
        tzid
      </code>
      
       (<code>
        Numbers.StateAsync
      </code>
      
      /<code>
        StateOneAsync
      </code>
      
      , <code>
        Rent.StateAsync
      </code>
      
      /<code>
        StateOneAsync
      </code>
      
      ).
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        FailNext(code, endpoint?)
      </code>
    </td>
    
    <td>
      The next request (to <code>
        endpoint
      </code>
      
       only, e.g. <code>
        "getNum.php"
      </code>
      
      ) answers <code>
        {"response": code}
      </code>
      
      .
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        FailNextTransport(status = 502, endpoint?)
      </code>
    </td>
    
    <td>
      The next request fails with a non-JSON body and HTTP <code>
        status
      </code>
      
      .
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        SetBalance(usd)
      </code>
      
       / <code>
        Balance()
      </code>
    </td>
    
    <td>
      Set / read the total balance, USD (it includes the part frozen by open operations, as <code>
        User.BalanceAsync()
      </code>
      
       reports it).
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        Operations()
      </code>
      
       / <code>
        Rents()
      </code>
    </td>
    
    <td>
      Snapshots of the current number operations / open rents, for assertions.
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        Requests
      </code>
    </td>
    
    <td>
      Log of the requests received (a copy).
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        Reset()
      </code>
    </td>
    
    <td>
      Back to the constructor's state.
    </td>
  </tr>
</tbody>
</table>

## What the mock does

- **Numbers**: `GetAsync` freezes the price; `CloseAsync` after an SMS spends it, without one refunds it; `BanAsync` after an SMS answers `"0"`, like the server. Numbers are `+<country>9000000NN`, ids count from 1. `NextAsync` switches to the following SMS. The operation's `Time` stays at 1200 s — there is no clock.
- **Rent**: `GetAsync` reserves the price (`Status` `0`, `Checked` `false`) and keeps the `extension` flag; the first SMS that is not `notify` confirms the rent (`Status` `1`) and charges it — `AddSms(tzid, text, service: "notify")` adds a notice with `Code` `"0"` (the integer `0` on the wire) that confirms nothing. `ExtendAsync` works on a confirmed rent and charges at once. `CloseAsync` releases the reservation of an unconfirmed rent and gives no refund for a confirmed one. `sum` is a number in `GetAsync`/`ExtendAsync` and a decimal string on the wire of `StateAsync`, like the server. SMS appear in `Messages`, newest first.
- **User**: `BalanceAsync` (frozen part included), a fixed profile, `SetWebhookUrlAsync` accepts any `scheme://…` URL (`VALIDATION_ERROR` otherwise), `WebhookLogsAsync` lists the deliveries the mock *recorded* when an SMS arrived while a webhook URL was set — it makes no HTTP call; their `Params` follow the real payload (`Code` `""` without a code, rent `Number` in digits without `+`, rent `TimeLeft` in minutes). `PaymentHistoryAsync` has no orders.
- **Free**: `Free.ListAsync` returns two fixed countries (44 first) with one number and a few SMS each.
- Not imitated: anything that needs real time — the 5 s lock after `setOperationOk` (`INTERVAL_CONCURRENT_REQUESTS_ERROR`), the 10 s lock of `Rent.ExtendAsync`, the 120 s before an unconfirmed rent may be closed and its 20-minute wait for the first SMS. `GuardInterval` above zero stands in for "too early to close".
