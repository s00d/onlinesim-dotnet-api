# Webhooks

> Receive SMS as HTTP callbacks and parse them with Webhook.Parse.

Set a URL with [`client.User.SetWebhookUrlAsync`](/user#setwebhookurlasync). For each SMS the server POSTs a JSON body to it: `webhook_type` `receiving_sms` for numbers, `rent_sms` for rents. Delivery attempts are listed by [`client.User.WebhookLogsAsync`](/user#webhooklogsasync).

## Webhook.Parse

Parse an inbound webhook body from a JSON string or an already parsed `JsonElement`.

```csharp
static WebhookPayload Parse(string payload)
static WebhookPayload Parse(JsonElement payload)
```

Lenient: numeric fields may arrive as strings, missing or malformed fields become `null`, unknown fields are dropped. It throws `OnlineSimException` only when the input is not a JSON object.

Any web framework works: pass it the raw request body. With the BCL's `HttpListener` (also on .NET Framework):

```csharp
using System.IO;
using System.Net;
using OnlineSim.Api;

var listener = new HttpListener();
listener.Prefixes.Add("http://+:8080/onlinesim-hook/");
listener.Start();
while (true)
{
    var context = await listener.GetContextAsync();
    using (var reader = new StreamReader(context.Request.InputStream))
    {
        var hook = Webhook.Parse(await reader.ReadToEndAsync());
        if (hook.WebhookType == Webhook.ReceivingSms) Console.WriteLine($"{hook.OperationId} {hook.Code}");
    }
    context.Response.Close();
}
```

## Payload fields

`WebhookPayload`:

<table>
<thead>
  <tr>
    <th>
      Property
    </th>
    
    <th>
      Type
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
        WebhookType
      </code>
    </td>
    
    <td>
      <code>
        string?
      </code>
    </td>
    
    <td>
      <code>
        Webhook.ReceivingSms
      </code>
      
      , <code>
        Webhook.RentSms
      </code>
      
      , or <code>
        null
      </code>
      
      .
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        OperationId
      </code>
    </td>
    
    <td>
      <code>
        long?
      </code>
    </td>
    
    <td>
      <code>
        tzid
      </code>
      
       of the operation or rent.
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        UserId
      </code>
    </td>
    
    <td>
      <code>
        long?
      </code>
    </td>
    
    <td>
      Account id.
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        CountryCode
      </code>
    </td>
    
    <td>
      <code>
        int?
      </code>
    </td>
    
    <td>
      Country code of the number.
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        Number
      </code>
    </td>
    
    <td>
      <code>
        string?
      </code>
    </td>
    
    <td>
      Numbers: <code>
        +
      </code>
      
       and the full number (<code>
        +79001234567
      </code>
      
      ), or <code>
        hidden
      </code>
      
      . Rent: the full number in digits without <code>
        +
      </code>
      
       (<code>
        79001234567
      </code>
      
      ).
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        Sender
      </code>
    </td>
    
    <td>
      <code>
        string?
      </code>
    </td>
    
    <td>
      Sender of the SMS, e.g. <code>
        VK
      </code>
      
      .
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        Message
      </code>
    </td>
    
    <td>
      <code>
        string?
      </code>
    </td>
    
    <td>
      Full SMS text.
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        Code
      </code>
    </td>
    
    <td>
      <code>
        string?
      </code>
    </td>
    
    <td>
      Code extracted from the SMS; <code>
        ""
      </code>
      
       when none was found.
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        TimeStart
      </code>
    </td>
    
    <td>
      <code>
        string?
      </code>
    </td>
    
    <td>
      Numbers: when the operation was ordered. Rent: when the SMS arrived. <code>
        YYYY-MM-DD HH:MM:SS
      </code>
      
      .
    </td>
  </tr>
  
  <tr>
    <td>
      <code>
        TimeLeft
      </code>
    </td>
    
    <td>
      <code>
        long?
      </code>
    </td>
    
    <td>
      Time left: seconds for numbers, minutes for rent.
    </td>
  </tr>
</tbody>
</table>
