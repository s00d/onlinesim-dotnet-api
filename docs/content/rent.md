---
title: Rent
description: Rent a number for days and receive every SMS sent to it — client.Rent.
navigation:
  order: 2
---

A rent keeps a number for `days` days. Its SMS come in `RentItem.Messages`, newest first. Every method also takes a `CancellationToken cancellationToken = default` as its last parameter.

`RentItem`: `Tzid`, `Status`, `Number` (without the country code), `Country`, `Sum`, `Days`, `Hours`, `Time`, `TimeSeconds?`, `Extension`, `Extend`, `Messages`, `StartTime`, `StopTime`, `DayExtend?`, … —
`Sum` is the price paid in USD (a `decimal`; the server sends a decimal string), `Time` minutes left, `TimeSeconds` seconds left,
`Extension` `0` not extendable / `1` extendable / `2` auto-extension, `Extend` maps days to the extension price (empty when not extendable),
`Messages` is a list of `RentSms`: `Id`, `Service`, `Text`, `Code`, `CreatedAt`.

## GetAsync

Rent a number and charge the balance.

```csharp
Task<RentItem> GetAsync(int country = 7, int days = 1, bool extension = true)
```

| Param | Type | Default | Description |
|---|---|---|---|
| `country` | `int` | `7` | Country code. |
| `days` | `int` | `1` | One of the tariff's `Days` keys. |
| `extension` | `bool` | `true` | `false` makes the rent non-extendable for good. |

```csharp
var rent = await client.Rent.GetAsync(7, 7);
Console.WriteLine($"{rent.Tzid} {rent.Number} {rent.Sum}");
```

Errors: `NoNumberException`; `ApiException` `UNDEFINED_DAYS`, `UNDEFINED_COUNTRY`, `WARNING_LOW_BALANCE`, `INTERVAL_CONCURRENT_REQUESTS_ERROR`, `TRY_AGAIN_LATER`.

## StateAsync

All rents of the account.

```csharp
Task<IReadOnlyList<RentItem>> StateAsync()
```

```csharp
foreach (var rent in await client.Rent.StateAsync())
{
    Console.WriteLine($"{rent.Number} {rent.Messages.FirstOrDefault()?.Code}");
}
```

## StateOneAsync

One rent.

```csharp
Task<RentItem> StateOneAsync(long tzid)
```

```csharp
var messages = (await client.Rent.StateOneAsync(tzid)).Messages;
```

Errors: `ApiException` `ERROR_NO_OPERATIONS` for an unknown tzid.

## ExtendAsync

Extend a rent by `days` days and charge the balance. Only rents created with `extension: true`.

```csharp
Task<RentItem> ExtendAsync(long tzid, int days = 1)
```

```csharp
var rent = await client.Rent.ExtendAsync(tzid, 3);
Console.WriteLine(rent.StopTime);
```

Errors: `ApiException` `TRY_AGAIN_LATER` (not extendable, unknown tzid or period, a repeat within 10 s), `WARNING_LOW_BALANCE`.

## CloseAsync

Return the number before the period ends. A rent that is not confirmed yet and has no SMS can be closed only after 120 s; US numbers cannot be closed early.

```csharp
Task CloseAsync(long tzid)
```

```csharp
await client.Rent.CloseAsync(tzid);
```

Errors: `ApiException` `ERROR_NO_OPERATIONS`, `NO_COMPLETE_TZID`, `NO_CLOSE_RENT_US`, `TRY_AGAIN_LATER`.

## TariffsAsync

Rent prices of every country, keyed by country code. No API key needed.

```csharp
Task<IReadOnlyDictionary<int, RentTariff>> TariffsAsync()
```

`RentTariff`: `Code`, `Name`, `Enabled`, `New`, `Position`, `Extend`, `Confirm?`, `Days`, `Count`, `ExtCounts?` — `Days` maps rent days to the price in USD, `Count` to the numbers available.

```csharp
var tariffs = await client.Rent.TariffsAsync();
```

## TariffsOneAsync

Rent prices of one country.

```csharp
Task<RentTariff> TariffsOneAsync(int country = 7)
```

```csharp
var tariff = await client.Rent.TariffsOneAsync(7);
Console.WriteLine(tariff.Days[7]); // price of 7 days
```

Errors: `ApiException` `NO_COUNTRY` for a country without rent.
