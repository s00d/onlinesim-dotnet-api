# Changelog

## 1.0.1

Aligned with the regenerated backend API spec.

### Fixed

- Decoding no longer fails on real answers: `RentSms.Code` accepts the integer `0` the server sends for a `notify` SMS (normalised to the string `"0"`); `Profile.Number` is `long?` and `Profile.Country` is `string?` (they were swapped; both decoded leniently).
- `Operation.Response` is `string?` (absent when no number could be issued); `FreeCountry.CountryText`/`CountryOriginal`, `FreeNumber.CountryOriginal`, `PaymentOrder.Currency` are nullable (`PaymentOrder.FormText` already was).
- `User.PaymentHistoryAsync()` no longer drops `Paylist` when the server sends it as a JSON array (ids 0, 1, 2…): a PHP list is read as an index-keyed map (`"0"`, `"1"`, …), for every map.
- Rent `Sum` is read from a number (getRentNum, extendRentState) or a decimal string (getRentState).
- `Numbers.WaitCodeAsync` retries `ApiException` `TRY_AGAIN_LATER` (HTTP 429 rate limit), `ERROR_UNDEFINED` and `INTERVAL_CONCURRENT_REQUESTS_ERROR` until its timeout instead of throwing at once.
- Error messages: `"0"` (ban not applied, the operation already has an SMS), `ACCOUNT_CLEANED`, `USER_NOT_ACTIVATED`, `ERROR_UNDEFINED`, `FORWARD_NOT_SUPPORTED` added; `ERROR_NO_KEY`, `NO_NUMBER_FOR_FORWARD`, `ACCOUNT_IDENTIFICATION_REQUIRED`, `API_DISABLED` (no longer sent) removed; `ERROR_WRONG_KEY` is "Wrong or missing API key". `NoNumberException` is thrown only for `NO_NUMBER`.
- `MockOnlineSim` follows the backend: `setOperationRevise` answers `{"tzid":"N","response":"1"}` and `ERROR_WRONG_TZID` / `ERROR_NO_TZID`; `setOperationOk` tzid `0` → `ERROR_NO_TZID`; a missing key is HTTP 200 `ERROR_WRONG_KEY` (456 only for a wrong key, also on public endpoints); `getNum` with `number` adds `title`, `response_text`, `guard_interval_remaining_seconds`. Rent: `getRentNum` reserves the price (`status` 0) and the first non-`notify` SMS confirms and charges it, unknown country → `UNDEFINED_COUNTRY`, `sum` a number in getRentNum/extendRentState and a string in getRentState, `guard_interval_remaining_seconds` with `number`; `extendRentState` refusals are all `TRY_AGAIN_LATER`; `closeRentNum` unknown tzid → `ERROR_NO_OPERATIONS`, releases the reservation of an unconfirmed rent. Webhook-log params: `code` `""` without a code, rent `number` in full digits, rent `time_left` in minutes, `time_start` per type. `profile.php` accepts any `scheme://…` URL. `getPaymentHistory` adds `forms`/`buttons`/`domains`; `getFreeList` lists 44 first, clamps `count` to 10..500, sends ISO-8601 `created_at` and `ignore`/`content`. `MockRent` exposes `Status` and `InitialDays`.
- Docs: rent reservation and `Sum`, available = Balance − max(Zbalance, 0), `BanAsync` answer `"0"`, webhook `Number`/`TimeLeft`/`TimeStart`, the `WaitCodeAsync` retry policy, the mock's behaviour.

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
