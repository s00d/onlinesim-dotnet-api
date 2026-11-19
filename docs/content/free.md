---
title: Free numbers
description: Public numbers and their SMS — client.Free. No API key needed.
navigation:
  order: 4
---

## ListAsync

Countries, the numbers of the selected country and the SMS of the selected number, in one call.

```csharp
Task<FreeList> ListAsync(int? country = null, string? number = null, int? page = null, int? count = null, CancellationToken cancellationToken = default)
```

| Param | Type | Default | Description |
|---|---|---|---|
| `country` | `int?` | `44` (server) | Country code; an unknown one falls back to the first available. |
| `number` | `string?` | first number | Number **without** the country code, as the keys of `Numbers`. |
| `page` | `int?` | `1` | Page of `Messages`. |
| `count` | `int?` | server default | SMS per page, 10–500. |

`FreeList`:

- `Countries`: list of `FreeCountry` — `Country`, `CountryText`, `CountryOriginal`
- `Numbers`: `IReadOnlyDictionary<string, FreeNumber>` keyed by the short number — `Country`, `CountryOriginal`, `FullNumber`, `DataHumans`, `IsArchive`
- `Messages`: a paginator of `FreeMessage` (`Id`, `Text`, `InNumber`, `MyNumber`, `Code`, `CreatedAt`, `DataHumans`) plus `Number` and `Country`; `Code` is `-` when no code was found

```csharp
var numbers = (await client.Free.ListAsync(country: 44)).Numbers;
var shortNumber = numbers.Keys.FirstOrDefault();
if (shortNumber != null)
{
    var messages = (await client.Free.ListAsync(country: 44, number: shortNumber, count: 20)).Messages;
    foreach (var sms in messages.Data) Console.WriteLine($"{sms.InNumber} {sms.Code} {sms.Text}");
}
```
