using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OnlineSim.Api;

/// <summary>One SMS of a rent.</summary>
public sealed class RentSms
{
    /// <summary>Message id.</summary>
    public long Id { get; init; }

    /// <summary>Sender. <c>notify</c>: a notice about a prohibited sender (does not confirm the rent); <c>init</c>: record of a rent confirmed at once.</summary>
    public string Service { get; init; } = "";

    /// <summary>Full text.</summary>
    public string Text { get; init; } = "";

    /// <summary>Code; empty when none was found. A <c>notify</c> SMS carries the integer <c>0</c>, normalised to <c>"0"</c>.</summary>
    [JsonConverter(typeof(LenientStringConverter))]
    public string Code { get; init; } = "";

    /// <summary>Arrival time, <c>YYYY-MM-DD HH:MM:SS</c>.</summary>
    public string CreatedAt { get; init; } = "";
}

/// <summary>Rent operation.</summary>
public sealed class RentItem
{
    /// <summary>Rent id.</summary>
    public long Tzid { get; init; }

    /// <summary><c>0</c>: price reserved, waiting for the first SMS; <c>1</c>: confirmed by an SMS that is not <c>notify</c>, price charged.</summary>
    public int Status { get; init; }

    /// <summary>Number without the country code.</summary>
    public string Number { get; init; } = "";

    /// <summary>Country code.</summary>
    public int Country { get; init; }

    /// <summary>Cost of the rent incl. extensions, USD (the server sends a number or a decimal string). Reserved while <see cref="Status"/> is 0, charged once confirmed.</summary>
    public decimal Sum { get; init; }

    /// <summary>Rent days.</summary>
    public int Days { get; init; }

    /// <summary>Rent hours.</summary>
    public int Hours { get; init; }

    /// <summary>Minutes left.</summary>
    public long Time { get; init; }

    /// <summary>Seconds left.</summary>
    public long? TimeSeconds { get; init; }

    /// <summary>Seconds until the rent may be closed, when sent.</summary>
    public int? GuardIntervalRemainingSeconds { get; init; }

    /// <summary>0 – not extendable, 1 – extendable, 2 – auto-extension.</summary>
    public int Extension { get; init; }

    /// <summary>Days of auto-extension.</summary>
    public int? DaysAutoextend { get; init; }

    /// <summary>Rent flag.</summary>
    public int? Rent { get; init; }

    /// <summary>Start, <c>YYYY-MM-DD HH:MM:SS</c>.</summary>
    public string StartTime { get; init; } = "";

    /// <summary>End, <c>YYYY-MM-DD HH:MM:SS</c>.</summary>
    public string StopTime { get; init; } = "";

    /// <summary>Last check time.</summary>
    public string? CheckedTime { get; init; }

    /// <summary>Confirmed.</summary>
    public bool? Checked { get; init; }

    /// <summary>Frozen.</summary>
    public bool? Freeze { get; init; }

    /// <summary>Reload counter.</summary>
    public int? Reload { get; init; }

    /// <summary>Manual extension.</summary>
    public bool? MExt { get; init; }

    /// <summary>Price of one day of extension; 0 when not configured.</summary>
    public decimal? DayExtend { get; init; }

    /// <summary>Days → extension price, USD; empty when not extendable.</summary>
    public IReadOnlyDictionary<int, decimal> Extend { get; init; } = new Dictionary<int, decimal>();

    /// <summary>Webhook URL of the rent.</summary>
    public string? WebhookUrl { get; init; }

    /// <summary>SMS, newest first.</summary>
    public IReadOnlyList<RentSms> Messages { get; init; } = new List<RentSms>();
}

/// <summary>Rent prices of one country.</summary>
public sealed class RentTariff
{
    /// <summary>Country code.</summary>
    public int Code { get; init; }

    /// <summary>Country name (slug).</summary>
    public string Name { get; init; } = "";

    /// <summary>Available.</summary>
    public bool Enabled { get; init; }

    /// <summary>New country.</summary>
    public bool New { get; init; }

    /// <summary>Sort position.</summary>
    public int Position { get; init; }

    /// <summary>1 when rents can be extended.</summary>
    public int Extend { get; init; }

    /// <summary>Confirmation required.</summary>
    public bool? Confirm { get; init; }

    /// <summary>Rent days → price, USD.</summary>
    public IReadOnlyDictionary<int, decimal> Days { get; init; } = new Dictionary<int, decimal>();

    /// <summary>Rent days → numbers available.</summary>
    public IReadOnlyDictionary<int, int> Count { get; init; } = new Dictionary<int, int>();

    /// <summary>Extension days → numbers available, when sent.</summary>
    public IReadOnlyDictionary<int, int>? ExtCounts { get; init; }
}
