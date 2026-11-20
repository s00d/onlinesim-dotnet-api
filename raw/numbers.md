# Numbers

> Order a number, read its SMS, close it — client.Numbers.

An operation (`tzid`) is one ordered number. Order it with `GetAsync`/`GetWithNumberAsync`, read the SMS with `StateOneAsync` or `WaitCodeAsync`, finish it with `CloseAsync` (or `BanAsync`). Countries are phone codes (`7`, `44`, …), services are slugs (`google`, `vkcom`, …) as returned by `TariffsAsync`. Every method also takes a `CancellationToken cancellationToken = default` as its last parameter (left out of the signatures below).

## PriceAsync

Price of one number, USD; `null` when the country has no price for the service.

```csharp
Task<decimal?> PriceAsync(string service, int country = 7)
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
        service
      </code>
    </td>
    
    <td>
      <code>
        string
      </code>
    </td>
    
    <td>
      —
    </td>
    
    <td>
      Service slug.
    </td>
  </tr>
  
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
</tbody>
</table>

```csharp
decimal? usd = await client.Numbers.PriceAsync("google", 44);
```

Errors: `ApiException` `ERROR_NO_SERVICE`, `UNDEFINED_COUNTRY`.

## GetAsync

Order a number; returns the operation id. Freezes the price on the balance.

```csharp
Task<long> GetAsync(string service, int country = 7, IEnumerable<string>? reject = null)
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
        service
      </code>
    </td>
    
    <td>
      <code>
        string
      </code>
    </td>
    
    <td>
      —
    </td>
    
    <td>
      Service slug.
    </td>
  </tr>
  
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
        reject
      </code>
    </td>
    
    <td>
      <code>
        IEnumerable<string>?
      </code>
    </td>
    
    <td>
      profile list
    </td>
    
    <td>
      Number prefixes to skip, e.g. <code>
        new[] { "4470" }
      </code>
      
      .
    </td>
  </tr>
</tbody>
</table>

```csharp
long tzid = await client.Numbers.GetAsync("google", 44, reject: new[] { "4470" });
```

Errors: `NoNumberException`; `ApiException` `WARNING_LOW_BALANCE`, `ERROR_NO_SERVICE`, `UNDEFINED_COUNTRY`, `EXCEEDED_CONCURRENT_OPERATIONS`, `INTERVAL_CONCURRENT_REQUESTS_ERROR`, `TRY_AGAIN_LATER`.

## GetWithNumberAsync

Same as `GetAsync`, but returns the number right away.

```csharp
Task<NumberOrder> GetWithNumberAsync(string service, int country = 7, IEnumerable<string>? reject = null)
```

Returns `NumberOrder`: `Tzid`, `Number` (with `+`), `Country`, `Time` (seconds left), `Service`, `Title?`, `GuardIntervalRemainingSeconds?`.

```csharp
NumberOrder order = await client.Numbers.GetWithNumberAsync("google", 44);
Console.WriteLine($"{order.Tzid}: {order.Number}");
```

Errors: as `GetAsync`.

## StateAsync

All operations of the account; empty when there are none.

```csharp
Task<IReadOnlyList<Operation>> StateAsync(bool messageToCode = true, SortOrder orderBy = SortOrder.Asc, bool msgList = false)
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
        messageToCode
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
        Msg
      </code>
      
       is the code only; <code>
        false
      </code>
      
       — the full SMS text.
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        orderBy
      </code>
    </td>
    
    <td>
      <code>
        SortOrder
      </code>
    </td>
    
    <td>
      <code>
        Asc
      </code>
    </td>
    
    <td>
      Order of operations (<code>
        Asc
      </code>
      
      , <code>
        Desc
      </code>
      
      ).
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        msgList
      </code>
    </td>
    
    <td>
      <code>
        bool
      </code>
    </td>
    
    <td>
      <code>
        false
      </code>
    </td>
    
    <td>
      All SMS in <code>
        MsgList
      </code>
      
       (<code>
        OperationSms
      </code>
      
      : <code>
        Service
      </code>
      
      , <code>
        Msg
      </code>
      
      ) instead of <code>
        Msg
      </code>
      
      .
    </td>
  </tr>
</tbody>
</table>

`Operation`: `Tzid`, `Response`, `Number?`, `Service?`, `Country?`, `Sum?`, `Time?` (seconds left), `Msg?`, `MsgList?`, `GuardIntervalRemainingSeconds?`, …
`Response` is the state (`null` when no number could be issued), one of the `OperationStatus` constants: `TZ_INPOOL` (number being assigned), `TZ_NUM_WAIT` (waiting for SMS), `TZ_NUM_ANSWER` (SMS arrived), `TZ_OVER_OK` (finished), `ERROR_NO_ITEMS` (failed). `Msg` is `null` until the first SMS. The server caches answers for a few seconds.

```csharp
foreach (var op in await client.Numbers.StateAsync(orderBy: SortOrder.Desc))
{
    Console.WriteLine($"{op.Tzid} {op.Response} {op.Msg}");
}
```

## StateOneAsync

One operation.

```csharp
Task<Operation> StateOneAsync(long tzid, bool messageToCode = true, bool msgList = false)
```

```csharp
var op = await client.Numbers.StateOneAsync(tzid);
if (op.Response == OperationStatus.NumAnswer) Console.WriteLine(op.Msg);
```

Errors: `ApiException` `ERROR_NO_OPERATIONS` when the operation does not exist (any more).

## WaitCodeAsync

Poll the operation until a code arrives, then close it and return the code.

```csharp
Task<string> WaitCodeAsync(long tzid, WaitCodeOptions? options = null)
```

<table>
<thead>
  <tr>
    <th>
      <code>
        WaitCodeOptions
      </code>
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
        Interval
      </code>
    </td>
    
    <td>
      <code>
        TimeSpan
      </code>
    </td>
    
    <td>
      5 s
    </td>
    
    <td>
      Poll interval.
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        Timeout
      </code>
    </td>
    
    <td>
      <code>
        TimeSpan
      </code>
    </td>
    
    <td>
      10 min
    </td>
    
    <td>
      Give up after.
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        KeepOpen
      </code>
    </td>
    
    <td>
      <code>
        bool
      </code>
    </td>
    
    <td>
      <code>
        false
      </code>
    </td>
    
    <td>
      Call <code>
        NextAsync
      </code>
      
       instead of <code>
        CloseAsync
      </code>
      
      , to wait for another SMS.
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        FullMessage
      </code>
    </td>
    
    <td>
      <code>
        bool
      </code>
    </td>
    
    <td>
      <code>
        false
      </code>
    </td>
    
    <td>
      Return the full SMS text instead of the code.
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        Since
      </code>
    </td>
    
    <td>
      <code>
        string?
      </code>
    </td>
    
    <td>
      <code>
        null
      </code>
    </td>
    
    <td>
      Ignore this value (the previous code).
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        Delay
      </code>
      
      , <code>
        Now
      </code>
    </td>
    
    <td>
      delegates
    </td>
    
    <td>
      <code>
        Task.Delay
      </code>
      
      , a <code>
        Stopwatch
      </code>
    </td>
    
    <td>
      Injectable for tests: <code>
        Func<TimeSpan, CancellationToken, Task>
      </code>
      
      , <code>
        Func<TimeSpan>
      </code>
      
      .
    </td>
  </tr>
</tbody>
</table>

Transient errors are retried until the timeout: `TransportException`, and `ApiException` `TRY_AGAIN_LATER` (the HTTP 429 rate limit), `ERROR_UNDEFINED` and `INTERVAL_CONCURRENT_REQUESTS_ERROR`. Any other `ApiException` is thrown at once.

```csharp
var first = await client.Numbers.WaitCodeAsync(tzid, new WaitCodeOptions { KeepOpen = true });
var second = await client.Numbers.WaitCodeAsync(tzid, new WaitCodeOptions { Since = first });
```

Errors: `WaitTimeoutException`; `ApiException` with code `TZ_OVER_OK` or `ERROR_NO_ITEMS` when the operation ended without a new code; any other `ApiException` from `StateOneAsync`/`CloseAsync`/`NextAsync`.

## NextAsync

Switch the operation to its next SMS and keep it open (for services that send several SMS).

```csharp
Task NextAsync(long tzid)
```

```csharp
await client.Numbers.NextAsync(tzid);
```

Errors: `ApiException` `ERROR_WRONG_TZID` (not an operation of your account), `ERROR_NO_TZID`.

## CloseAsync

Finish the operation. An operation without SMS can be closed only after its guard interval (`GuardIntervalRemainingSeconds`); every attempt locks the tzid for 5 s.

```csharp
Task CloseAsync(long tzid)
```

```csharp
await client.Numbers.CloseAsync(tzid);
```

Errors: `ApiException` `NO_COMPLETE_TZID`, `INTERVAL_CONCURRENT_REQUESTS_ERROR`, `ERROR_WRONG_TZID`.

## BanAsync

Finish the operation and report the number as bad. Only before any SMS arrived.

```csharp
Task BanAsync(long tzid)
```

```csharp
await client.Numbers.BanAsync(tzid);
```

Errors: as `CloseAsync`; `ApiException` with `Code` `"0"` when the operation already has an SMS — nothing is changed then.

## TariffsAsync

Services, prices and stock of every country, keyed by country code. No API key needed. The answer is large (a few MB); prefer `TariffsOneAsync`.

```csharp
Task<IReadOnlyDictionary<int, CountryStat>> TariffsAsync()
```

`CountryStat`: `Name`, `LocaleName?`, `Position`, `Code`, `Other?`, `New`, `Enabled`, `Services`; `Services` maps `service_<slug>` to `ServiceStat` (`Count?` — `null` when unknown, `Popular`, `Code`, `Price`, `Id`, `Service`, `Slug`).

```csharp
var all = await client.Numbers.TariffsAsync();
Console.WriteLine($"{all.Count} countries");
```

## TariffsOneAsync

Services, prices and stock of one country.

```csharp
Task<CountryStat> TariffsOneAsync(int country = 7)
```

```csharp
var uk = await client.Numbers.TariffsOneAsync(44);
foreach (var s in uk.Services.Values) Console.WriteLine($"{s.Slug} {s.Price} {s.Count}");
```

Errors: `ApiException` `NO_COUNTRY`.
