using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OnlineSim.Api;

/// <summary>Balance, USD.</summary>
public sealed class UserBalance
{
    /// <summary>Total balance. Includes <see cref="Zbalance"/>: available = Balance − Zbalance.</summary>
    public decimal Balance { get; init; }

    /// <summary>Sum frozen by numbers in progress.</summary>
    public decimal Zbalance { get; init; }

    /// <summary>Partner income, when sent.</summary>
    public decimal? Income { get; init; }
}

/// <summary>Account profile.</summary>
public sealed class Profile
{
    /// <summary>User id.</summary>
    public long Id { get; init; }

    /// <summary>Public hash id.</summary>
    public string? HashId { get; init; }

    /// <summary>Name.</summary>
    public string? Name { get; init; }

    /// <summary>Username.</summary>
    public string? Username { get; init; }

    /// <summary>E-mail.</summary>
    public string? Email { get; init; }

    /// <summary>E-mail verification time.</summary>
    public string? EmailVerifiedAt { get; init; }

    /// <summary>API key.</summary>
    public string? Apikey { get; init; }

    /// <summary><c>true</c>, or a map of allowed IPs.</summary>
    public JsonElement? ApiAccess { get; init; }

    /// <summary>Locale.</summary>
    public string? Locale { get; init; }

    /// <summary>Currency.</summary>
    public string? Currency { get; init; }

    /// <summary>Default country of numbers.</summary>
    public int? NumberCountry { get; init; }

    /// <summary>Rejected number prefixes: a list, or a map.</summary>
    public JsonElement? NumberReject { get; init; }

    /// <summary>Webhook URL.</summary>
    public string? WebhookUrl { get; init; }

    /// <summary>Phone number.</summary>
    public string? Number { get; init; }

    /// <summary>Country.</summary>
    public int? Country { get; init; }

    /// <summary>Region of numbers.</summary>
    public int? NumberRegion { get; init; }

    /// <summary>API user.</summary>
    public bool? IsApi { get; init; }

    /// <summary>Active flag, as sent (a string).</summary>
    public string? Active { get; init; }

    /// <summary>Blocked flag.</summary>
    public int? Block { get; init; }

    /// <summary>Disabled.</summary>
    public bool? Disable { get; init; }

    /// <summary>Verification level.</summary>
    public int? Verify { get; init; }

    /// <summary>User group.</summary>
    public int? Ugroup { get; init; }

    /// <summary>Subscription flag.</summary>
    public int? Sub { get; init; }

    /// <summary>First login.</summary>
    public bool? FirstLogin { get; init; }

    /// <summary>Project subscription.</summary>
    public bool? IsProjectSubscription { get; init; }

    /// <summary>Reset counter.</summary>
    [JsonPropertyName("resetCount")]
    public int? ResetCount { get; init; }

    /// <summary>Registration time.</summary>
    public string? CreatedAt { get; init; }

    /// <summary>Last notification time.</summary>
    public string? NotificationAt { get; init; }
}

/// <summary>Laravel paginator.</summary>
public class Paginator<T>
{
    /// <summary>Current page, from 1.</summary>
    public int CurrentPage { get; init; }

    /// <summary>Items of the page.</summary>
    public IReadOnlyList<T> Data { get; init; } = new List<T>();

    /// <summary>Index of the first item, <c>null</c> on an empty page.</summary>
    public int? From { get; init; }

    /// <summary>Index of the last item, <c>null</c> on an empty page.</summary>
    public int? To { get; init; }

    /// <summary>Last page.</summary>
    public int LastPage { get; init; }

    /// <summary>Items per page.</summary>
    public int PerPage { get; init; }

    /// <summary>Items in total.</summary>
    public int Total { get; init; }

    /// <summary>URL of the first page.</summary>
    public string? FirstPageUrl { get; init; }

    /// <summary>URL of the last page.</summary>
    public string? LastPageUrl { get; init; }

    /// <summary>URL of the next page.</summary>
    public string? NextPageUrl { get; init; }

    /// <summary>URL of the previous page.</summary>
    public string? PrevPageUrl { get; init; }

    /// <summary>Base URL.</summary>
    public string? Path { get; init; }

    /// <summary>Page links.</summary>
    public IReadOnlyList<JsonElement>? Links { get; init; }
}

/// <summary>One top-up order.</summary>
public sealed class PaymentOrder
{
    /// <summary>Order id.</summary>
    public long Id { get; init; }

    /// <summary>Decimal string, e.g. <c>"89.00"</c>.</summary>
    public string Sum { get; init; } = "";

    /// <summary>Currency.</summary>
    public string Currency { get; init; } = "";

    /// <summary>Status code.</summary>
    public int Stat { get; init; }

    /// <summary>Payment form.</summary>
    public string? FormText { get; init; }

    /// <summary>Status text.</summary>
    public string? StatusText { get; init; }

    /// <summary>Creation time.</summary>
    public string CreatedAt { get; init; } = "";

    /// <summary>Every other field as sent.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>Top-up orders and payment methods.</summary>
public sealed class PaymentHistory
{
    /// <summary>Orders, newest first.</summary>
    public Paginator<PaymentOrder> Orders { get; init; } = new Paginator<PaymentOrder>();

    /// <summary>Payment methods by list id; empty when none.</summary>
    public IReadOnlyDictionary<string, JsonElement> Paylist { get; init; } = new Dictionary<string, JsonElement>();

    /// <summary>Payment domains.</summary>
    public IReadOnlyList<string>? Domains { get; init; }

    /// <summary>Payment forms.</summary>
    public IReadOnlyDictionary<string, string>? Forms { get; init; }

    /// <summary>Currency rates.</summary>
    public IReadOnlyDictionary<string, decimal>? Currency { get; init; }

    /// <summary>Quick top-up buttons.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<decimal>>? Buttons { get; init; }
}

/// <summary>One webhook delivery.</summary>
public sealed class WebhookLog
{
    /// <summary>Log id.</summary>
    public long Id { get; init; }

    /// <summary>User id.</summary>
    public long UserId { get; init; }

    /// <summary>URL the payload was sent to.</summary>
    public string WebhookUrl { get; init; } = "";

    /// <summary><c>receiving_sms</c> or <c>rent_sms</c>.</summary>
    public string Type { get; init; } = "";

    /// <summary>The payload that was sent (parsed leniently, as <see cref="Webhook.Parse(string)"/>).</summary>
    public WebhookPayload Params { get; init; } = new WebhookPayload();

    /// <summary><c>success</c>, <c>fail</c> or <c>pending</c>.</summary>
    public string Status { get; init; } = "";

    /// <summary>Error of a failed delivery.</summary>
    public string? Error { get; init; }

    /// <summary>Creation time.</summary>
    public string CreatedAt { get; init; } = "";

    /// <summary>Update time.</summary>
    public string UpdatedAt { get; init; } = "";
}
