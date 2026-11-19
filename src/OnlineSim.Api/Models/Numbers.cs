using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OnlineSim.Api;

/// <summary>States of a number operation (<see cref="Operation.Response"/>).</summary>
public static class OperationStatus
{
    /// <summary>The number is being assigned.</summary>
    public const string InPool = "TZ_INPOOL";

    /// <summary>Waiting for an SMS.</summary>
    public const string NumWait = "TZ_NUM_WAIT";

    /// <summary>An SMS arrived.</summary>
    public const string NumAnswer = "TZ_NUM_ANSWER";

    /// <summary>Finished.</summary>
    public const string OverOk = "TZ_OVER_OK";

    /// <summary>Failed.</summary>
    public const string ErrorNoItems = "ERROR_NO_ITEMS";
}

/// <summary>Order of <see cref="NumbersApi.StateAsync"/> results.</summary>
public enum SortOrder
{
    /// <summary>Oldest first (<c>orderby=asc</c>).</summary>
    Asc,

    /// <summary>Newest first (<c>orderby=desc</c>).</summary>
    Desc,
}

/// <summary>One SMS of an operation when <c>msgList</c> is set.</summary>
public sealed class OperationSms
{
    /// <summary>Service of the SMS.</summary>
    public string Service { get; init; } = "";

    /// <summary>Code (or full text without <c>messageToCode</c>).</summary>
    public string? Msg { get; init; }

    /// <summary>Full text, when sent.</summary>
    public string? Text { get; init; }

    /// <summary>Code, when sent.</summary>
    public string? Code { get; init; }
}

/// <summary>Number operation (a getState item).</summary>
public sealed class Operation
{
    /// <summary>Operation id.</summary>
    public long Tzid { get; init; }

    /// <summary>State, one of <see cref="OperationStatus"/>.</summary>
    public string Response { get; init; } = "";

    /// <summary>Full number with <c>+</c>.</summary>
    public string? Number { get; init; }

    /// <summary>Service slug.</summary>
    public string? Service { get; init; }

    /// <summary>Country code.</summary>
    public int? Country { get; init; }

    /// <summary>Price, USD.</summary>
    public decimal? Sum { get; init; }

    /// <summary>Seconds left.</summary>
    public long? Time { get; init; }

    /// <summary>Form the number was ordered from.</summary>
    public string? Form { get; init; }

    /// <summary>Subscription package, when ordered through one.</summary>
    public long? SubscriptionPackageId { get; init; }

    /// <summary>Webhook URL of the operation.</summary>
    public string? WebhookUrl { get; init; }

    /// <summary>Extension flag.</summary>
    public int? Extend { get; init; }

    /// <summary>Seconds until the operation may be closed.</summary>
    public int? GuardIntervalRemainingSeconds { get; init; }

    /// <summary>The code (or the full text without <c>messageToCode</c>); <c>null</c> until an SMS arrives and with <c>msgList</c>.</summary>
    [JsonIgnore]
    public string? Msg => RawMsg.ValueKind == JsonValueKind.String ? RawMsg.GetString() : null;

    /// <summary>All SMS of the operation with <c>msgList</c>; otherwise <c>null</c>.</summary>
    [JsonIgnore]
    public IReadOnlyList<OperationSms>? MsgList =>
        RawMsg.ValueKind == JsonValueKind.Array ? Json.Read<List<OperationSms>>(RawMsg) : null;

    // `msg` is a string, a list of {service, msg} with msg_list=1, `false`, or absent.
    [JsonInclude]
    [JsonPropertyName("msg")]
    internal JsonElement RawMsg { get; set; }
}

/// <summary>Result of <see cref="NumbersApi.GetWithNumberAsync"/>.</summary>
public sealed class NumberOrder
{
    /// <summary>Operation id.</summary>
    public long Tzid { get; init; }

    /// <summary>Full number with <c>+</c>, e.g. <c>+447459608320</c>.</summary>
    public string Number { get; init; } = "";

    /// <summary>Country code.</summary>
    public int Country { get; init; }

    /// <summary>Seconds left.</summary>
    public long Time { get; init; }

    /// <summary>Service slug.</summary>
    public string Service { get; init; } = "";

    /// <summary>Display name of the service, when sent.</summary>
    public string? Title { get; init; }

    /// <summary>Seconds until the operation may be closed, when sent.</summary>
    public int? GuardIntervalRemainingSeconds { get; init; }
}

/// <summary>One service of a country in <see cref="CountryStat.Services"/>.</summary>
public sealed class ServiceStat
{
    /// <summary>Numbers in stock; <c>null</c> when unknown (the server sends <c>"?"</c>).</summary>
    public int? Count { get; init; }

    /// <summary>Popular service.</summary>
    public bool Popular { get; init; }

    /// <summary>Country code.</summary>
    public int Code { get; init; }

    /// <summary>Price, USD.</summary>
    public decimal Price { get; init; }

    /// <summary>Service id.</summary>
    public long Id { get; init; }

    /// <summary>Display name.</summary>
    public string Service { get; init; } = "";

    /// <summary>Service id for <see cref="NumbersApi.GetAsync"/> / <see cref="NumbersApi.PriceAsync"/>.</summary>
    public string Slug { get; init; } = "";
}

/// <summary>Services, prices and stock of one country.</summary>
public sealed class CountryStat
{
    /// <summary>Country name (slug), e.g. <c>united_kingdom</c>.</summary>
    public string Name { get; init; } = "";

    /// <summary>Localised name.</summary>
    public string? LocaleName { get; init; }

    /// <summary>Sort position.</summary>
    public int Position { get; init; }

    /// <summary>Country code.</summary>
    public int Code { get; init; }

    /// <summary>Price of other services; <c>null</c> when not set (the server sends <c>false</c>).</summary>
    public decimal? Other { get; init; }

    /// <summary>New country.</summary>
    public bool New { get; init; }

    /// <summary>Available.</summary>
    public bool Enabled { get; init; }

    /// <summary>Map <c>service_&lt;slug&gt;</c> → service; empty when none.</summary>
    public IReadOnlyDictionary<string, ServiceStat> Services { get; init; } = new Dictionary<string, ServiceStat>();
}
