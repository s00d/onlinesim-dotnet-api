using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OnlineSim.Api;

/// <summary>Account: <c>client.User</c>.</summary>
public sealed class UserApi
{
    private readonly RequestFn _request;

    internal UserApi(RequestFn request) => _request = request;

    /// <summary>Balance in USD. <see cref="UserBalance.Balance"/> includes the frozen <see cref="UserBalance.Zbalance"/>: available = Balance − Zbalance.</summary>
    public async Task<UserBalance> BalanceAsync(CancellationToken cancellationToken = default)
    {
        var res = await _request("getBalance.php", new Query().Add("income", 1), null, cancellationToken).ConfigureAwait(false);
        return new UserBalance
        {
            Balance = Json.Field<decimal?>(res, "balance") ?? 0,
            Zbalance = Json.Field<decimal?>(res, "zbalance") ?? 0,
            Income = Json.Field<decimal?>(res, "income"),
        };
    }

    /// <summary>Account profile.</summary>
    public async Task<Profile> ProfileAsync(CancellationToken cancellationToken = default) =>
        Json.Field<Profile>(await _request("getProfile.php", null, null, cancellationToken).ConfigureAwait(false), "profile") ?? new Profile();

    /// <summary>Top-up orders (paginated, newest first) and the available payment methods.</summary>
    public async Task<PaymentHistory> PaymentHistoryAsync(int page = 1, CancellationToken cancellationToken = default) =>
        Json.Read<PaymentHistory>(await _request("getPaymentHistory.php", new Query().Add("page", page), null, cancellationToken).ConfigureAwait(false));

    /// <summary>Set the URL that receives SMS webhooks. Throws <see cref="ApiException"/> <c>VALIDATION_ERROR</c> for an invalid URL.</summary>
    public Task SetWebhookUrlAsync(string url, CancellationToken cancellationToken = default) => PostProfile(url, cancellationToken);

    /// <summary>Remove the webhook URL (also from operations in progress).</summary>
    public Task ClearWebhookUrlAsync(CancellationToken cancellationToken = default) => PostProfile(null, cancellationToken);

    /// <summary>Webhook delivery log, newest first, 10 per page.</summary>
    public async Task<Paginator<WebhookLog>> WebhookLogsAsync(int page = 1, CancellationToken cancellationToken = default) =>
        Json.Field<Paginator<WebhookLog>>(await _request("webhook-logs", new Query().Add("page", page), null, cancellationToken).ConfigureAwait(false), "data")
        ?? new Paginator<WebhookLog>();

    private Task<JsonElement> PostProfile(string? url, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?> { ["profile"] = new Dictionary<string, object?> { ["webhook_url"] = url } };
        return _request("profile.php", null, body, cancellationToken);
    }
}
