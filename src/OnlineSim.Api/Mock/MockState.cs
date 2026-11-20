using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace OnlineSim.Api;

/// <summary>
/// State of the mock backend: the single owner of everything the fake API remembers — balance, operations,
/// rents, the profile's webhook URL and its delivery log, scheduled SMS, the failure queue and the request log.
/// Handlers (<see cref="MockHandlers"/>) read and change it only through this object; <see cref="MockOnlineSim"/>
/// serialises access to it.
/// Deterministic: ids and numbers come from a counter, timestamps from a fixed epoch plus that counter.
/// Money is kept in integer thousandths of a USD ("mills") so sums never drift.
/// </summary>
internal sealed class MockState
{
    internal const long UserId = 1;

    /// <summary>Seconds an operation stays open, as reported in <c>time</c>. The mock has no clock, so it never runs down.</summary>
    internal const int OperationTime = 1200;

    /// <summary>Minutes an unconfirmed rent waits for its first SMS (<c>stop_time</c> = start + this).</summary>
    internal const int RentFirstSmsMinutes = 20;

    /// <summary>Sender of a rent SMS from a prohibited sender: it gets the integer <c>code</c> <c>0</c> and does not confirm the rent.</summary>
    internal const string Notify = "notify";

    private static readonly DateTime Epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Regex DigitGap = new Regex(@"([0-9])[\s-]+(?=[0-9])", RegexOptions.CultureInvariant);
    private static readonly Regex CodeRun = new Regex(@"(?<![0-9])[0-9]{4,8}(?![0-9])", RegexOptions.CultureInvariant);

    private readonly long _startBalance;

    internal MockState(MockOptions options)
    {
        ApiKey = options.ApiKey;
        GuardIntervalSeconds = (long)options.GuardInterval.TotalSeconds;
        Prices = (options.Prices ?? new Dictionary<int, Dictionary<string, decimal>>
        {
            [7] = new Dictionary<string, decimal> { ["vkcom"] = 0.5m, ["telegram"] = 1.2m, ["google"] = 0.3m },
            [44] = new Dictionary<string, decimal> { ["google"] = 0.2m, ["vkcom"] = 0.36m },
        }).ToDictionary(c => c.Key, c => new Dictionary<string, decimal>(c.Value));
        RentTariffs = (options.RentTariffs ?? new Dictionary<int, MockRentTariff>
        {
            [7] = new MockRentTariff { Days = new Dictionary<int, decimal> { [1] = 1.0m, [3] = 2.5m, [7] = 5.0m }, Extend = 1 },
        }).ToDictionary(t => t.Key, t => new MockRentTariff { Days = new Dictionary<int, decimal>(t.Value.Days), Extend = t.Value.Extend });
        _startBalance = ToMills(options.Balance);
        Reset();
    }

    internal string ApiKey { get; }

    internal long GuardIntervalSeconds { get; }

    internal Dictionary<int, Dictionary<string, decimal>> Prices { get; }

    internal Dictionary<int, MockRentTariff> RentTariffs { get; }

    /// <summary>Total balance incl. the frozen part, mills.</summary>
    internal long Balance { get; set; }

    /// <summary>Frozen by open operations and unconfirmed rents, mills.</summary>
    internal long Frozen { get; set; }

    /// <summary>Keyed by tzid; ids only grow, so the sorted order is the creation order.</summary>
    internal SortedDictionary<long, OperationState> Operations { get; } = new SortedDictionary<long, OperationState>();

    internal SortedDictionary<long, RentState> Rents { get; } = new SortedDictionary<long, RentState>();

    internal string? WebhookUrl { get; set; }

    internal List<WebhookLogEntry> WebhookLogs { get; } = new List<WebhookLogEntry>();

    internal List<MockRequest> Requests { get; } = new List<MockRequest>();

    internal List<Failure> Failures { get; } = new List<Failure>();

    internal Dictionary<long, List<ScheduledSms>> Scheduled { get; } = new Dictionary<long, List<ScheduledSms>>();

    /// <summary>Source of tzids, numbers, message ids and timestamps.</summary>
    internal long Seq { get; private set; }

    /// <summary>Available balance (total minus frozen), mills.</summary>
    internal long Available => Balance - Frozen;

    internal void Reset()
    {
        Balance = _startBalance;
        Frozen = 0;
        Operations.Clear();
        Rents.Clear();
        WebhookUrl = null;
        WebhookLogs.Clear();
        Requests.Clear();
        Failures.Clear();
        Scheduled.Clear();
        Seq = 0;
    }

    /// <summary>The id the next operation/rent will get.</summary>
    internal long PeekId() => Seq + 1;

    internal long NextId() => ++Seq;

    /// <summary>Short (country-less) number for an id: deterministic, 9 digits.</summary>
    internal static string ShortNumber(long id) => (900_000_000 + id).ToString(CultureInfo.InvariantCulture);

    /// <summary><c>YYYY-MM-DD HH:MM:SS</c> at <paramref name="seconds"/> after the fixed mock epoch.</summary>
    internal static string Stamp(long seconds = 0) =>
        Epoch.AddSeconds(seconds).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// The code rule shared by every SDK mock: dashes/spaces between digits are removed, then the first run of
    /// 4–8 digits (not part of a longer run) is the code; <c>null</c> when there is none.
    /// </summary>
    internal static string? ExtractCode(string text)
    {
        var match = CodeRun.Match(DigitGap.Replace(text, "$1"));
        return match.Success ? match.Value : null;
    }

    internal static long ToMills(decimal usd) => (long)Math.Round(usd * 1000m, MidpointRounding.AwayFromZero);

    internal static decimal FromMills(long mills) => mills / 1000m;

    /// <summary>Remove and return the first queued failure that applies to <paramref name="path"/>.</summary>
    internal Failure? TakeFailure(string path)
    {
        var i = Failures.FindIndex(f => f.Endpoint == null || f.Endpoint == path);
        if (i < 0) return null;
        var failure = Failures[i];
        Failures.RemoveAt(i);
        return failure;
    }

    /// <summary>An SMS arrives at operation or rent <paramref name="tzid"/> now. Returns <c>false</c> when there is no open one.</summary>
    internal bool Deliver(long tzid, string text, string? service)
    {
        var code = ExtractCode(text);
        if (Operations.TryGetValue(tzid, out var op) && op.Status != OperationStatus.OverOk)
        {
            var sms = new MockSms(service ?? op.Service, text, code);
            op.Messages.Add(sms);
            op.Status = OperationStatus.NumAnswer;
            // Activation: `+<digits>`, seconds left, the order time.
            RecordWebhook(Webhook.ReceivingSms, tzid, op.Country, op.Number, sms, OperationTime, Stamp(op.Tzid));
            return true;
        }
        if (Rents.TryGetValue(tzid, out var rent))
        {
            var id = NextId();
            var sms = new MockRentSms(service ?? "other", text, code, id, Stamp(id));
            rent.Messages.Add(sms);
            if (rent.Status == 0 && sms.Service != Notify)
            {
                // The first real SMS confirms the rent: the reservation is charged.
                var cost = ToMills(rent.Sum);
                Frozen -= cost;
                Balance -= cost;
                rent.Status = 1;
            }
            // Rent: full number in digits without `+`, minutes left, the SMS time.
            var minutesLeft = rent.Status == 1 ? rent.Days * 24 * 60 : RentFirstSmsMinutes;
            var fullNumber = rent.Country.ToString(CultureInfo.InvariantCulture) + rent.Number;
            RecordWebhook(Webhook.RentSms, tzid, rent.Country, fullNumber, sms, minutesLeft, Stamp(id));
            return true;
        }
        return false;
    }

    internal void Schedule(long tzid, string text, int polls, string? service)
    {
        if (!Scheduled.TryGetValue(tzid, out var list)) Scheduled[tzid] = list = new List<ScheduledSms>();
        list.Add(new ScheduledSms(text, service, polls));
    }

    /// <summary>One state poll of <paramref name="tzid"/>: delivers the scheduled SMS whose polls ran out, counts down the rest.</summary>
    internal void Poll(long tzid)
    {
        if (!Scheduled.TryGetValue(tzid, out var list)) return;
        var due = list.Where(s => s.Polls <= 0).ToList();
        var rest = list.Where(s => s.Polls > 0).ToList();
        foreach (var s in rest) s.Polls--;
        if (rest.Count > 0) Scheduled[tzid] = rest;
        else Scheduled.Remove(tzid);
        // Dropped silently when the operation was closed meanwhile: a real SMS would not arrive either.
        foreach (var s in due) Deliver(tzid, s.Text, s.Service);
    }

    /// <summary>No HTTP call: the mock only records what the backend would have sent.</summary>
    private void RecordWebhook(string type, long tzid, int country, string number, MockSms sms, long timeLeft, string timeStart)
    {
        if (WebhookUrl == null) return;
        WebhookLogs.Add(new WebhookLogEntry(WebhookLogs.Count + 1, WebhookUrl, type, tzid, country, number, sms, Stamp(Seq), timeLeft, timeStart));
    }
}

internal sealed class OperationState
{
    internal OperationState(long tzid, string service, int country, string number, decimal sum)
    {
        Tzid = tzid;
        Service = service;
        Country = country;
        Number = number;
        Sum = sum;
    }

    internal long Tzid { get; }

    internal string Service { get; }

    internal int Country { get; }

    internal string Number { get; }

    internal decimal Sum { get; }

    internal string Status { get; set; } = OperationStatus.NumWait;

    internal List<MockSms> Messages { get; } = new List<MockSms>();

    internal int Cursor { get; set; }

    internal MockOperation Snapshot() => new MockOperation(Tzid, Service, Country, Number, Sum, Status, Messages.ToArray(), Cursor);
}

internal sealed class RentState
{
    internal RentState(long tzid, int country, string number, int days, decimal sum, bool extension)
    {
        Tzid = tzid;
        Country = country;
        Number = number;
        Days = days;
        InitialDays = days;
        Sum = sum;
        Extension = extension;
    }

    internal long Tzid { get; }

    internal int Country { get; }

    internal string Number { get; }

    /// <summary>Total rent days: the initial period plus extensions.</summary>
    internal int Days { get; set; }

    /// <summary>The initial period (<c>days</c> of getRentNum).</summary>
    internal int InitialDays { get; }

    /// <summary><c>0</c>: reserved, waiting for the first SMS; <c>1</c>: confirmed, price charged.</summary>
    internal int Status { get; set; }

    /// <summary>Total cost incl. extensions, USD. Frozen while <see cref="Status"/> is 0.</summary>
    internal decimal Sum { get; set; }

    internal bool Extension { get; }

    internal List<MockRentSms> Messages { get; } = new List<MockRentSms>();

    internal MockRent Snapshot() => new MockRent(Tzid, Country, Number, Days, InitialDays, Status, Sum, Extension, Messages.ToArray());
}

/// <summary>A queued failure: the next matching request answers <c>{"response": Code}</c>, or a non-JSON HTTP <c>Status</c>.</summary>
internal sealed class Failure
{
    internal Failure(string? endpoint, string? code, int status)
    {
        Endpoint = endpoint;
        Code = code;
        Status = status;
    }

    internal string? Endpoint { get; }

    internal string? Code { get; }

    internal int Status { get; }
}

internal sealed class ScheduledSms
{
    internal ScheduledSms(string text, string? service, int polls)
    {
        Text = text;
        Service = service;
        Polls = polls;
    }

    internal string Text { get; }

    internal string? Service { get; }

    /// <summary>getState/getRentState polls of this tzid still to pass before the SMS arrives.</summary>
    internal int Polls { get; set; }
}

internal sealed class WebhookLogEntry
{
    internal WebhookLogEntry(long id, string url, string type, long tzid, int country, string number, MockSms sms, string time, long timeLeft, string timeStart)
    {
        TimeLeft = timeLeft;
        TimeStart = timeStart;
        Id = id;
        Url = url;
        Type = type;
        Tzid = tzid;
        Country = country;
        Number = number;
        Sms = sms;
        Time = time;
    }

    internal long Id { get; }

    internal string Url { get; }

    internal string Type { get; }

    internal long Tzid { get; }

    internal int Country { get; }

    internal string Number { get; }

    internal MockSms Sms { get; }

    /// <summary>When the log was written (the SMS time).</summary>
    internal string Time { get; }

    /// <summary>Activations: seconds; rent: minutes.</summary>
    internal long TimeLeft { get; }

    /// <summary>Activations: the order time; rent: the SMS time.</summary>
    internal string TimeStart { get; }
}
