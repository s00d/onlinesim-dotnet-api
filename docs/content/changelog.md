---
title: Changelog
description: Release notes of OnlineSim.Api.
navigation:
  order: 7
---

The full history is in [CHANGELOG.md](https://github.com/s00d/onlinesim-dotnet-api/blob/main/CHANGELOG.md).

## 1.0.0

First release of the .NET SDK, on the same contract as the other OnlineSim SDKs (JS 2.1.0, PHP, Python, Go, Rust, Java, Ruby).

- `OnlineSimClient` with `Numbers`, `Rent`, `User` and `Free` sections; async methods with `CancellationToken`; `netstandard2.0` and `net8.0` (.NET Framework 4.6.2+, .NET 6+, Unity/Mono).
- The key is sent as a Bearer header, never in the URL; injectable `ITransport` (your own `HttpClient`).
- Typed exceptions: `ApiException`, `NoNumberException`, `TransportException`, `WaitTimeoutException`.
- `Numbers.WaitCodeAsync`: interval and timeout as `TimeSpan`, `KeepOpen`, `Since`, retries network errors, injectable delay and clock.
- `Webhook.Parse` for inbound webhooks; webhook URL set / clear / logs.
- `MockOnlineSim`: an offline fake of the API for your tests — see [Testing](/testing).
