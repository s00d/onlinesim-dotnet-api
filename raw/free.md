# Free numbers

> Public numbers and their SMS — client.Free. No API key needed.

## ListAsync

Countries, the numbers of the selected country and the SMS of the selected number, in one call.

```csharp
Task<FreeList> ListAsync(int? country = null, string? number = null, int? page = null, int? count = null, CancellationToken cancellationToken = default)
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
        int?
      </code>
    </td>
    
    <td>
      <code>
        44
      </code>
      
       (server)
    </td>
    
    <td>
      Country code; an unknown one falls back to the first available.
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        number
      </code>
    </td>
    
    <td>
      <code>
        string?
      </code>
    </td>
    
    <td>
      first number
    </td>
    
    <td>
      Number <strong>
        without
      </strong>
      
       the country code, as the keys of <code>
        Numbers
      </code>
      
      .
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        page
      </code>
    </td>
    
    <td>
      <code>
        int?
      </code>
    </td>
    
    <td>
      <code>
        1
      </code>
    </td>
    
    <td>
      Page of <code>
        Messages
      </code>
      
      .
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        count
      </code>
    </td>
    
    <td>
      <code>
        int?
      </code>
    </td>
    
    <td>
      server default
    </td>
    
    <td>
      SMS per page, 10–500.
    </td>
  </tr>
</tbody>
</table>

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
