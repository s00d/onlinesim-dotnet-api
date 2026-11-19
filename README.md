# OnlineSim.Api

[![NuGet](https://img.shields.io/nuget/v/OnlineSim.Api.svg)](https://www.nuget.org/packages/OnlineSim.Api)
[![CI](https://github.com/s00d/onlinesim-dotnet-api/actions/workflows/ci.yml/badge.svg)](https://github.com/s00d/onlinesim-dotnet-api/actions/workflows/ci.yml)

Typed async C# / .NET client for the [OnlineSim](https://onlinesim.io) API: SMS numbers, rent, account and free numbers.
`netstandard2.0` and `net8.0`: .NET Framework 4.6.2+, .NET 6/8/9+, Unity/Mono.

```bash
dotnet add package OnlineSim.Api
```

```csharp
using OnlineSim.Api;

var client = new OnlineSimClient(Environment.GetEnvironmentVariable("ONLINESIM_KEY"));

var balance = await client.User.BalanceAsync();
Console.WriteLine($"available: {balance.Balance - balance.Zbalance} USD");

var order = await client.Numbers.GetWithNumberAsync("google", 44);
Console.WriteLine($"enter {order.Number} in the app");
Console.WriteLine($"code: {await client.Numbers.WaitCodeAsync(order.Tzid)}");
```

## Testing

`MockOnlineSim` is a built-in offline fake of the API: test your integration without network, key or money.
More in the [testing guide](https://s00d.github.io/onlinesim-dotnet-api/testing).

```csharp
using OnlineSim.Api;

var mock = new MockOnlineSim(new MockOptions { Balance = 10 });
var client = mock.Client();

var tzid = await client.Numbers.GetAsync("google", 44);
mock.AddSmsAfter(tzid, "Your code: 123-456", 2); // arrives on the third poll
var code = await client.Numbers.WaitCodeAsync(tzid, new WaitCodeOptions { Interval = TimeSpan.Zero });

if (code != "123456" || mock.Balance() != 9.8m) throw new Exception($"unexpected: {code}, {mock.Balance()}");
Console.WriteLine($"code {code}, balance {mock.Balance()}");
```

Documentation: **https://s00d.github.io/onlinesim-dotnet-api/** · Changes: [CHANGELOG.md](CHANGELOG.md) · License: Apache-2.0
