---
title: User
description: Balance, profile, payments and the webhook URL — client.User.
navigation:
  order: 3
---

Every method also takes a `CancellationToken cancellationToken = default` as its last parameter.

## BalanceAsync

Balance in USD.

```csharp
Task<UserBalance> BalanceAsync()
```

`UserBalance`: `Balance`, `Zbalance`, `Income?` (all `decimal`). `Balance` includes the sum frozen by numbers and unconfirmed rents in progress (`Zbalance`, which may be negative): available = `Balance − max(Zbalance, 0)`. `Income` is the partner income. The server caches the answer for 60 s.

```csharp
var b = await client.User.BalanceAsync();
decimal available = b.Balance - Math.Max(b.Zbalance, 0);
```

Errors: `ApiException` `ERROR_WRONG_KEY`, `ACCOUNT_BLOCKED`, `API_ACCESS_DISABLED`, `API_ACCESS_IP`.

## ProfileAsync

The account profile.

```csharp
Task<Profile> ProfileAsync()
```

`Profile`: `Id`, `Name`, `Username`, `Email`, `Locale`, `Currency`, `NumberCountry`, `NumberReject`, `WebhookUrl`, `ApiAccess`, … (`ApiAccess` and `NumberReject` arrive in several shapes and stay `JsonElement`).

```csharp
var profile = await client.User.ProfileAsync();
Console.WriteLine($"{profile.Email} {profile.WebhookUrl}");
```

## PaymentHistoryAsync

Top-up orders (paginated, newest first) and the available payment methods.

```csharp
Task<PaymentHistory> PaymentHistoryAsync(int page = 1)
```

`PaymentHistory`: `Orders` (`Paginator<PaymentOrder>`), `Paylist`, `Domains?`, `Forms?`, `Currency?`, `Buttons?`. `PaymentOrder.Sum` stays a decimal string (`"89.00"`); `Currency` and `FormText` may be `null`. `Paylist` maps a group id to its payment methods; when the server sends it as a JSON array (ids 0, 1, 2…) it is kept, keyed `"0"`, `"1"`, …; fields without a property are in `PaymentOrder.Extra`.

```csharp
var orders = (await client.User.PaymentHistoryAsync()).Orders;
Console.WriteLine($"{orders.Total} orders, page {orders.CurrentPage}/{orders.LastPage}");
```

## SetWebhookUrlAsync

Set the URL that receives SMS webhooks (see [Webhooks](/webhooks)).

```csharp
Task SetWebhookUrlAsync(string url)
```

```csharp
await client.User.SetWebhookUrlAsync("https://example.com/onlinesim-hook");
```

Errors: `ApiException` `VALIDATION_ERROR` for an invalid URL; `Message` holds the server's field messages.

## ClearWebhookUrlAsync

Remove the webhook URL, also from operations in progress.

```csharp
Task ClearWebhookUrlAsync()
```

```csharp
await client.User.ClearWebhookUrlAsync();
```

## WebhookLogsAsync

Webhook delivery log, newest first, 10 per page.

```csharp
Task<Paginator<WebhookLog>> WebhookLogsAsync(int page = 1)
```

`WebhookLog`: `Id`, `UserId`, `WebhookUrl`, `Type`, `Params`, `Status`, `Error`, `CreatedAt`, `UpdatedAt` — `Type` is `receiving_sms` or `rent_sms`, `Status` is `success`, `fail` or `pending`, `Params` the payload that was sent (a `WebhookPayload`).

```csharp
var logs = await client.User.WebhookLogsAsync();
foreach (var log in logs.Data) Console.WriteLine($"{log.CreatedAt} {log.Status} {log.Error}");
```
