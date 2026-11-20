# Rent

> Rent a number for days and receive every SMS sent to it — client.Rent.

A rent keeps a number for `days` days. Its SMS come in `RentItem.Messages`, newest first. Every method also takes a `CancellationToken cancellationToken = default` as its last parameter.

`GetAsync` reserves the price (it shows in `Zbalance`): the rent starts with `Status` `0` and `Checked` `false` and waits up to 20 minutes for its first SMS. The first SMS that is not a `notify` notice confirms it (`Status` `1`) and charges the price; without one the rent closes and the reservation is released. Some countries (`Confirm` in `TariffsAsync`, e.g. US numbers) are confirmed and charged at once.

`RentItem`: `Tzid`, `Status`, `Number` (without the country code), `Country`, `Sum`, `Days`, `Hours`, `Time`, `TimeSeconds?`, `Extension`, `Extend`, `Messages`, `StartTime`, `StopTime`, `DayExtend?`, … —
`Sum` is the cost in USD incl. extensions (a `decimal`; the server sends a number, or a decimal string in `StateAsync`), `Time` minutes left, `TimeSeconds` seconds left,
`Extension` `0` not extendable / `1` extendable / `2` auto-extension, `Extend` maps days to the extension price (empty when not extendable),
`Messages` is a list of `RentSms`: `Id`, `Service`, `Text`, `Code`, `CreatedAt` — `Code` is `""` when no code was found and `"0"` for a `notify` SMS (a prohibited sender; the server sends the integer `0`, the SDK normalises it to a string).

## GetAsync

Rent a number. The price is reserved; the first SMS charges it.

```csharp
Task<RentItem> GetAsync(int country = 7, int days = 1, bool extension = true)
```

<table>
<thead>
  <tr>
    <th>
      Param
    </th>
    
    <th>
      Type
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
        country
      </code>
    </td>
    
    <td>
      <code>
        int
      </code>
    </td>
    
    <td>
      <code>
        7
      </code>
    </td>
    
    <td>
      Country code.
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        days
      </code>
    </td>
    
    <td>
      <code>
        int
      </code>
    </td>
    
    <td>
      <code>
        1
      </code>
    </td>
    
    <td>
      One of the tariff's <code>
        Days
      </code>
      
       keys.
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        extension
      </code>
    </td>
    
    <td>
      <code>
        bool
      </code>
    </td>
    
    <td>
      <code>
        true
      </code>
    </td>
    
    <td>
      <code>
        false
      </code>
      
       makes the rent non-extendable for good.
    </td>
  </tr>
</tbody>
</table>

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

Extend a rent by `days` days and charge the balance at once. Only confirmed rents (`Status` `1` or `4`) created with `extension: true`.

```csharp
Task<RentItem> ExtendAsync(long tzid, int days = 1)
```

```csharp
var rent = await client.Rent.ExtendAsync(tzid, 3);
Console.WriteLine(rent.StopTime);
```

Errors: `ApiException` `TRY_AGAIN_LATER` (unknown tzid, not confirmed yet or closed, not extendable, unknown period, a repeat within 10 s), `WARNING_LOW_BALANCE`.

## CloseAsync

Return the number before the period ends. A rent that is not confirmed yet (`Status` `0`) and has no SMS can be closed only after 120 s; then, or with only `notify` SMS, the reservation is released. With a real SMS the rent is confirmed (charged) and closed. A confirmed rent closes without a refund. US numbers cannot be closed early.

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
