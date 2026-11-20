---
title: OnlineSim.Api
description: Typed async C# / .NET client for the OnlineSim API — SMS numbers, rent, account and free numbers — with a built-in offline mock for tests.
---

## Install

One package for .NET Framework 4.6.2+, .NET 6/8/9+ and Unity/Mono (targets `netstandard2.0` and `net8.0`). The only dependency is `System.Text.Json`, and only on `netstandard2.0` (it is in-box on .NET 8+).

::code-group
```bash [dotnet CLI]
dotnet add package OnlineSim.Api
```

```xml [PackageReference]
<PackageReference Include="OnlineSim.Api" Version="1.0.0" />
```

```powershell [Package Manager]
Install-Package OnlineSim.Api
```
::

## Quick start

Check the balance, order a number, wait for the SMS code.

```csharp
using OnlineSim.Api;

var client = new OnlineSimClient(Environment.GetEnvironmentVariable("ONLINESIM_KEY"));

var balance = await client.User.BalanceAsync();
Console.WriteLine($"available: {balance.Balance - Math.Max(balance.Zbalance, 0)} USD");

var order = await client.Numbers.GetWithNumberAsync("google", 44);
Console.WriteLine($"enter {order.Number} in the app");

// Polls every 5 s for up to 10 min, then closes the operation.
var code = await client.Numbers.WaitCodeAsync(order.Tzid);
Console.WriteLine($"code: {code}");
```

Every method is async (`Task<T>`) and takes an optional `CancellationToken` as its last parameter. The library awaits with `ConfigureAwait(false)`, so it is safe in UI and .NET Framework hosts with a synchronization context. `OnlineSimClient` is thread-safe: create one and reuse it.

## Client options

```csharp
using OnlineSim.Api;

var client = new OnlineSimClient(new OnlineSimClientOptions
{
    ApiKey = "your-api-key",
    Lang = "en",
    Timeout = TimeSpan.FromSeconds(15),
});
```

| Option | Type | Default | Description |
|---|---|---|---|
| `ApiKey` | `string?` | — | Sent as `Authorization: Bearer <ApiKey>`, never in the URL. Not needed for tariffs and free numbers. |
| `BaseUrl` | `string?` | `https://onlinesim.host/api/` | API base; trailing slash optional. |
| `Lang` | `string` | `en` | Language of server texts (`lang` query parameter). |
| `DevId` | `string?` | — | Partner developer id (`dev_id`), sent only when set. |
| `Timeout` | `TimeSpan` | 30 s | Per-request timeout; `Timeout.InfiniteTimeSpan` disables it. |
| `Transport` | `ITransport?` | `HttpClientTransport` | Transport: your own `HttpClient` (proxy, custom TLS, `IHttpClientFactory`), or a fake in tests. |

`new OnlineSimClient(apiKey)` is a shortcut for the defaults with a key. To use your own `HttpClient`:

```csharp
using System.Net;
using System.Net.Http;
using OnlineSim.Api;

var http = new HttpClient(new HttpClientHandler { Proxy = new WebProxy("http://127.0.0.1:8080") });
var client = new OnlineSimClient(new OnlineSimClientOptions
{
    ApiKey = "your-api-key",
    Transport = new HttpClientTransport(http),
});
```

Sections: [`client.Numbers`](/numbers), [`client.Rent`](/rent), [`client.User`](/user), [`client.Free`](/free). Webhook bodies are parsed by the static [`Webhook.Parse`](/webhooks).

Returned models keep the server's field names in PascalCase (`guard_interval_remaining_seconds` → `GuardIntervalRemainingSeconds`). Two things are normalised: money that the server sends as a decimal string (`balance`, rent `sum` in `Rent.StateAsync`) becomes a `decimal`, and maps that arrive as a PHP list become dictionaries keyed by index (an empty `[]` becomes an empty dictionary; `PaymentHistory.Paylist` sent as `[a, b]` becomes `{"0": a, "1": b}`). Times reported by the server stay plain numbers in the server's unit (seconds or minutes, documented per field); durations you pass in are `TimeSpan`.

## Error handling

Every exception derives from `OnlineSimException`.

| Class | When | Properties |
|---|---|---|
| `ApiException` | The API answered with an error code (also a wrong key, HTTP 456, and the rate limit, HTTP 429 `TRY_AGAIN_LATER`). | `Code`, `Message` |
| `NoNumberException` | `NO_NUMBER`: no number right now. Derives from `ApiException`. | `Code`, `Message` |
| `TransportException` | Network error, request timeout, or a body that is not JSON. | `Status` (`null` without a response), `Body` (first 500 chars) |
| `WaitTimeoutException` | `WaitCodeAsync` got no code within its `Timeout`. | — |

Cancelling your own `CancellationToken` throws the usual `OperationCanceledException`, not an SDK exception.

```csharp
using OnlineSim.Api;

try
{
    await client.Numbers.GetAsync("google", 44);
}
catch (NoNumberException)
{
    // try another country or later
}
catch (ApiException e)
{
    Console.Error.WriteLine($"{e.Code}: {e.Message}"); // e.g. WARNING_LOW_BALANCE
}
catch (TransportException e)
{
    Console.Error.WriteLine($"network: {e.Status} {e.Body}");
}
```

`Message` is the server's text when it sends one, otherwise a short English description of the code (`ErrorMessages.All`), otherwise the code itself. Profile validation failures become `ApiException` with code `VALIDATION_ERROR`.
