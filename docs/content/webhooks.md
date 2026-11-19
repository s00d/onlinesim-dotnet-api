---
title: Webhooks
description: Receive SMS as HTTP callbacks and parse them with Webhook.Parse.
navigation:
  order: 5
---

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

| Property | Type | Description |
|---|---|---|
| `WebhookType` | `string?` | `Webhook.ReceivingSms`, `Webhook.RentSms`, or `null`. |
| `OperationId` | `long?` | `tzid` of the operation or rent. |
| `UserId` | `long?` | Account id. |
| `CountryCode` | `int?` | Country code of the number. |
| `Number` | `string?` | The number, e.g. `+79001234567`. |
| `Sender` | `string?` | Sender of the SMS, e.g. `VK`. |
| `Message` | `string?` | Full SMS text. |
| `Code` | `string?` | Code extracted from the SMS. |
| `TimeStart` | `string?` | Start of the operation. |
| `TimeLeft` | `long?` | Time left. |
