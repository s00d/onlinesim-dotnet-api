# Changelog

## 1.0.0

First release of the C# / .NET SDK for the OnlineSim API, on the same contract as the other OnlineSim SDKs (the JS SDK 2.1.0 is the reference). NuGet package `OnlineSim.Api`.

### Added

- `OnlineSimClient` (`ApiKey`, `BaseUrl` default `https://onlinesim.host/api/`, `Lang`, `DevId`, `Timeout`, `Transport`) with the sections `Numbers`, `Rent`, `User`, `Free`. Every method is async (`Task<T>`), takes a `CancellationToken`, and awaits with `ConfigureAwait(false)`.
- Targets `netstandard2.0` and `net8.0`: .NET Framework 4.6.2+, .NET 6/8/9+, Unity/Mono. The only dependency is `System.Text.Json`, for `netstandard2.0` only.
- The API key is sent as `Authorization: Bearer <key>`, never in the query string. Booleans as `1`/`0`, `reject` as a comma list, unset parameters omitted, `orderby` lowercase.
- Injectable `ITransport`; the default `HttpClientTransport` runs on a shared `HttpClient` or on yours (proxy, custom TLS, `IHttpClientFactory`).
- Exceptions: `OnlineSimException` → `ApiException` (`Code`, `Message`) → `NoNumberException`; `TransportException` (`Status`, `Body`); `WaitTimeoutException`. An HTTP 456 wrong-key answer is an `ApiException`; profile validation errors are `ApiException("VALIDATION_ERROR")`.
- `Numbers`: `PriceAsync`, `GetAsync`, `GetWithNumberAsync`, `StateAsync`, `StateOneAsync`, `NextAsync`, `CloseAsync`, `BanAsync`, `TariffsAsync`, `TariffsOneAsync`, `WaitCodeAsync` (`Interval`, `Timeout` as `TimeSpan`, `KeepOpen`, `FullMessage`, `Since`, injectable `Delay` / `Now`; retries transient transport errors).
- `Rent`: `GetAsync` (extendable by default), `StateAsync`, `StateOneAsync`, `ExtendAsync`, `TariffsAsync`, `TariffsOneAsync`, `CloseAsync`.
- `User`: `BalanceAsync`, `ProfileAsync`, `PaymentHistoryAsync`, `SetWebhookUrlAsync`, `ClearWebhookUrlAsync`, `WebhookLogsAsync`.
- `Free.ListAsync`.
- `Webhook.Parse` for inbound `receiving_sms` / `rent_sms` webhooks (lenient).
- Normalisation of server quirks: decimal-string money (`balance`, rent `sum`) parsed to `decimal`, empty PHP maps `[]` read as empty dictionaries.
- `MockOnlineSim`: a built-in, in-process fake of the OnlineSim API for testing integrations offline — no network, key, money or clock. `mock.Client()` returns a normal `OnlineSimClient` wired to it (`mock.Transport` is the fake `ITransport`). Test helpers: `AddSms`, `AddSmsAfter`, `FailNext`, `FailNextTransport`, `SetBalance`, `Balance`, `Operations`, `Rents`, `Requests`, `Reset`. See the [testing guide](https://s00d.github.io/onlinesim-dotnet-api/testing).
- Package: Source Link, deterministic build, symbol package (`.snupkg`), package validation, XML docs.
- Offline xUnit suite (fake transport, fixtures, mock tests through the real client, docs examples compiled against the SDK), opt-in integration test, CI on Ubuntu and Windows with the .NET 8 and the latest SDK, smoke test of the packed `.nupkg`, documentation site (Nuxt Content).
