using System;
using System.Collections.Generic;

namespace OnlineSim.Api;

/// <summary>Options of <see cref="MockOnlineSim"/>.</summary>
public sealed class MockOptions
{
    /// <summary>Key the mock accepts as <c>Authorization: Bearer &lt;ApiKey&gt;</c>. Default <c>mock-key</c>.</summary>
    public string ApiKey { get; set; } = "mock-key";

    /// <summary>Starting balance, USD. Default 100.</summary>
    public decimal Balance { get; set; } = 100m;

    /// <summary>
    /// Number prices: country code → service slug → USD; replaces the defaults
    /// <c>{7: {vkcom: 0.5, telegram: 1.2, google: 0.3}, 44: {google: 0.2, vkcom: 0.36}}</c>.
    /// </summary>
    public Dictionary<int, Dictionary<string, decimal>>? Prices { get; set; }

    /// <summary>Rent tariffs: country code → tariff; replaces the default <c>{7: {days: {1: 1.0, 3: 2.5, 7: 5.0}, extend: 1}}</c>.</summary>
    public Dictionary<int, MockRentTariff>? RentTariffs { get; set; }

    /// <summary>
    /// Guard interval of new operations and rents. Above zero, an operation without SMS, or an unconfirmed rent without
    /// any SMS, cannot be closed (<c>NO_COMPLETE_TZID</c>). Default zero.
    /// </summary>
    public TimeSpan GuardInterval { get; set; } = TimeSpan.Zero;
}

/// <summary>Rent tariff of one country for <see cref="MockOptions.RentTariffs"/>.</summary>
public sealed class MockRentTariff
{
    /// <summary>Rent days → USD.</summary>
    public Dictionary<int, decimal> Days { get; set; } = new Dictionary<int, decimal>();

    /// <summary>1: rents can be extended, 0: they cannot. Default 1.</summary>
    public int Extend { get; set; } = 1;
}

/// <summary>One SMS received by an operation or a rent.</summary>
public class MockSms
{
    internal MockSms(string service, string text, string? code)
    {
        Service = service;
        Text = text;
        Code = code;
    }

    /// <summary>Service (sender).</summary>
    public string Service { get; }

    /// <summary>Full text.</summary>
    public string Text { get; }

    /// <summary>Code extracted from <see cref="Text"/> (first run of 4–8 digits), <c>null</c> when there is none.</summary>
    public string? Code { get; }
}

/// <summary>One SMS received by a rent.</summary>
public sealed class MockRentSms : MockSms
{
    internal MockRentSms(string service, string text, string? code, long id, string createdAt) : base(service, text, code)
    {
        Id = id;
        CreatedAt = createdAt;
    }

    /// <summary>Message id.</summary>
    public long Id { get; }

    /// <summary>Arrival time, <c>YYYY-MM-DD HH:MM:SS</c>.</summary>
    public string CreatedAt { get; }
}

/// <summary>A number operation as the mock stores it (a snapshot).</summary>
public sealed class MockOperation
{
    internal MockOperation(long tzid, string service, int country, string number, decimal sum, string status, IReadOnlyList<MockSms> messages, int cursor)
    {
        Tzid = tzid;
        Service = service;
        Country = country;
        Number = number;
        Sum = sum;
        Status = status;
        Messages = messages;
        Cursor = cursor;
    }

    /// <summary>Operation id.</summary>
    public long Tzid { get; }

    /// <summary>Service slug.</summary>
    public string Service { get; }

    /// <summary>Country code.</summary>
    public int Country { get; }

    /// <summary>Full number with <c>+</c>.</summary>
    public string Number { get; }

    /// <summary>Price, USD. Frozen while open; spent on close after an SMS, refunded on close without one.</summary>
    public decimal Sum { get; }

    /// <summary><c>TZ_NUM_WAIT</c>, <c>TZ_NUM_ANSWER</c> or <c>TZ_OVER_OK</c>.</summary>
    public string Status { get; }

    /// <summary>SMS received, oldest first.</summary>
    public IReadOnlyList<MockSms> Messages { get; }

    /// <summary>Index of the SMS shown in <c>msg</c>; <see cref="NumbersApi.NextAsync"/> moves it past the last one.</summary>
    public int Cursor { get; }
}

/// <summary>An open rent as the mock stores it (a snapshot). Closed rents are removed.</summary>
public sealed class MockRent
{
    internal MockRent(long tzid, int country, string number, int days, int initialDays, int status, decimal sum, bool extension, IReadOnlyList<MockRentSms> messages)
    {
        Tzid = tzid;
        Country = country;
        Number = number;
        Days = days;
        InitialDays = initialDays;
        Status = status;
        Sum = sum;
        Extension = extension;
        Messages = messages;
    }

    /// <summary>Rent id.</summary>
    public long Tzid { get; }

    /// <summary>Country code.</summary>
    public int Country { get; }

    /// <summary>Number without the country code, as the backend sends it for rents.</summary>
    public string Number { get; }

    /// <summary>Total rent days: the initial period plus extensions.</summary>
    public int Days { get; }

    /// <summary>The initial period (<c>days</c> of getRentNum).</summary>
    public int InitialDays { get; }

    /// <summary><c>0</c>: reserved, waiting for the first SMS; <c>1</c>: confirmed by an SMS that is not <c>notify</c>, price charged.</summary>
    public int Status { get; }

    /// <summary>Total cost incl. extensions, USD. Frozen while <see cref="Status"/> is 0.</summary>
    public decimal Sum { get; }

    /// <summary>Extendable.</summary>
    public bool Extension { get; }

    /// <summary>SMS received, oldest first.</summary>
    public IReadOnlyList<MockRentSms> Messages { get; }
}

/// <summary>One request the mock received.</summary>
public sealed class MockRequest
{
    internal MockRequest(string method, string path, IReadOnlyDictionary<string, string> query, string? body)
    {
        Method = method;
        Path = path;
        Query = query;
        Body = body;
    }

    /// <summary><c>GET</c> or <c>POST</c>.</summary>
    public string Method { get; }

    /// <summary>Path relative to <c>/api/</c>, e.g. <c>getNum.php</c>, <c>rent/getRentNum.php</c>, <c>getFreeList</c>.</summary>
    public string Path { get; }

    /// <summary>Query parameters, decoded, in the order sent.</summary>
    public IReadOnlyDictionary<string, string> Query { get; }

    /// <summary>JSON body of a POST as sent, <c>null</c> for GET.</summary>
    public string? Body { get; }
}
