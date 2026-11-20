using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace OnlineSim.Api;

/// <summary>Options of <see cref="NumbersApi.WaitCodeAsync"/>.</summary>
public sealed class WaitCodeOptions
{
    /// <summary>Poll interval. Default 5 s.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Give up after this long. Default 10 min.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Call <see cref="NumbersApi.NextAsync"/> instead of <see cref="NumbersApi.CloseAsync"/> after the code, to wait for another SMS.</summary>
    public bool KeepOpen { get; set; }

    /// <summary>Return the full SMS text instead of the code.</summary>
    public bool FullMessage { get; set; }

    /// <summary>Ignore this value (the previous code when waiting for the next one).</summary>
    public string? Since { get; set; }

    /// <summary>Injectable delay (tests). Default <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</summary>
    public Func<TimeSpan, CancellationToken, Task>? Delay { get; set; }

    /// <summary>Injectable monotonic clock: time elapsed since any fixed origin (tests). Default a <see cref="Stopwatch"/>.</summary>
    public Func<TimeSpan>? Now { get; set; }
}

/// <summary>Numbers for receiving SMS: <c>client.Numbers</c>.</summary>
public sealed class NumbersApi
{
    /// <summary>Error codes that mean "try again"; <see cref="WaitCodeAsync"/> retries them.</summary>
    private static readonly HashSet<string> Retryable = new HashSet<string> { "TRY_AGAIN_LATER", "ERROR_UNDEFINED", "INTERVAL_CONCURRENT_REQUESTS_ERROR" };

    private readonly RequestFn _request;

    internal NumbersApi(RequestFn request) => _request = request;

    /// <summary>Price (USD) of one number of <paramref name="service"/> in <paramref name="country"/>; <c>null</c> when the country has no price for it.</summary>
    public async Task<decimal?> PriceAsync(string service, int country = 7, CancellationToken cancellationToken = default)
    {
        var res = await _request("getPrice.php", new Query().Add("service", service).Add("country", country), null, cancellationToken).ConfigureAwait(false);
        return Json.Field<decimal?>(res, "price");
    }

    /// <summary>Order a number. Returns the operation id (<c>tzid</c>).</summary>
    /// <param name="service">Service slug, e.g. <c>google</c>.</param>
    /// <param name="country">Country code.</param>
    /// <param name="reject">Number prefixes to skip, e.g. <c>["4470", "4471"]</c>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task<long> GetAsync(string service, int country = 7, IEnumerable<string>? reject = null, CancellationToken cancellationToken = default)
    {
        var res = await _request("getNum.php", new Query().Add("service", service).Add("country", country).Add("reject", reject), null, cancellationToken).ConfigureAwait(false);
        return Json.Field<long>(res, "tzid");
    }

    /// <summary>Order a number and get it right away together with the operation id.</summary>
    /// <param name="service">Service slug, e.g. <c>google</c>.</param>
    /// <param name="country">Country code.</param>
    /// <param name="reject">Number prefixes to skip.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task<NumberOrder> GetWithNumberAsync(string service, int country = 7, IEnumerable<string>? reject = null, CancellationToken cancellationToken = default)
    {
        // `number` is a presence flag on getNum: sent here only.
        var query = new Query().Add("service", service).Add("country", country).Add("reject", reject).Add("number", 1);
        return Json.Read<NumberOrder>(await _request("getNum.php", query, null, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>All operations of the account; empty when there are none.</summary>
    /// <param name="messageToCode"><c>true</c>: <see cref="Operation.Msg"/> is the code only; <c>false</c>: the full SMS text.</param>
    /// <param name="orderBy">Order of operations.</param>
    /// <param name="msgList"><c>true</c>: all SMS in <see cref="Operation.MsgList"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task<IReadOnlyList<Operation>> StateAsync(bool messageToCode = true, SortOrder orderBy = SortOrder.Asc, bool msgList = false, CancellationToken cancellationToken = default)
    {
        var query = new Query()
            .Add("message_to_code", messageToCode)
            .Add("orderby", orderBy == SortOrder.Desc ? "desc" : "asc")
            .Add("msg_list", msgList)
            .Add("number", 1);
        try
        {
            return Json.Read<List<Operation>>(await _request("getState.php", query, null, cancellationToken).ConfigureAwait(false));
        }
        catch (ApiException e) when (e.Code == "ERROR_NO_OPERATIONS")
        {
            return new List<Operation>();
        }
    }

    /// <summary>One operation. Throws <see cref="ApiException"/> <c>ERROR_NO_OPERATIONS</c> when it does not exist (any more).</summary>
    /// <param name="tzid">Operation id.</param>
    /// <param name="messageToCode"><c>true</c>: <see cref="Operation.Msg"/> is the code only; <c>false</c>: the full SMS text.</param>
    /// <param name="msgList"><c>true</c>: all SMS in <see cref="Operation.MsgList"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task<Operation> StateOneAsync(long tzid, bool messageToCode = true, bool msgList = false, CancellationToken cancellationToken = default)
    {
        var query = new Query().Add("tzid", tzid).Add("message_to_code", messageToCode).Add("msg_list", msgList).Add("number", 1);
        var list = Json.Read<List<Operation>>(await _request("getState.php", query, null, cancellationToken).ConfigureAwait(false));
        if (list.Count == 0) throw new ApiException("ERROR_NO_OPERATIONS");
        return list[0];
    }

    /// <summary>Switch the operation to its next SMS and keep it open. <c>ERROR_WRONG_TZID</c> for a tzid that is not yours.</summary>
    public Task NextAsync(long tzid, CancellationToken cancellationToken = default) =>
        _request("setOperationRevise.php", new Query().Add("tzid", tzid), null, cancellationToken);

    /// <summary>Finish the operation.</summary>
    public Task CloseAsync(long tzid, CancellationToken cancellationToken = default) =>
        _request("setOperationOk.php", new Query().Add("tzid", tzid), null, cancellationToken);

    /// <summary>
    /// Finish the operation and report the number as bad (only before any SMS arrived). After an SMS the server changes
    /// nothing and answers <c>"0"</c>: <see cref="ApiException"/> with <see cref="ApiException.Code"/> <c>"0"</c>.
    /// </summary>
    public Task BanAsync(long tzid, CancellationToken cancellationToken = default) =>
        _request("setOperationOk.php", new Query().Add("tzid", tzid).Add("ban", true), null, cancellationToken);

    /// <summary>Services, prices and stock of every country, keyed by country code. No key needed.</summary>
    public async Task<IReadOnlyDictionary<int, CountryStat>> TariffsAsync(CancellationToken cancellationToken = default) =>
        Json.Read<IReadOnlyDictionary<int, CountryStat>>(await _request("getNumbersStats.php", new Query().Add("country", "all"), null, cancellationToken).ConfigureAwait(false));

    /// <summary>Services, prices and stock of one country. No key needed.</summary>
    public async Task<CountryStat> TariffsOneAsync(int country = 7, CancellationToken cancellationToken = default) =>
        Json.Read<CountryStat>(await _request("getNumbersStats.php", new Query().Add("country", country), null, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Poll <see cref="StateOneAsync"/> every <see cref="WaitCodeOptions.Interval"/> until a code different from
    /// <see cref="WaitCodeOptions.Since"/> arrives, then close the operation (or <see cref="NextAsync"/> with
    /// <see cref="WaitCodeOptions.KeepOpen"/>) and return the code. A finished operation (<c>TZ_OVER_OK</c>,
    /// <c>ERROR_NO_ITEMS</c>) without a new code throws <see cref="ApiException"/> with that state as the code;
    /// running out of <see cref="WaitCodeOptions.Timeout"/> throws <see cref="WaitTimeoutException"/>.
    /// <see cref="TransportException"/> and the transient <see cref="ApiException"/> codes <c>TRY_AGAIN_LATER</c>,
    /// <c>ERROR_UNDEFINED</c> and <c>INTERVAL_CONCURRENT_REQUESTS_ERROR</c> are retried until the timeout; any other
    /// <see cref="ApiException"/> is thrown at once.
    /// </summary>
    public async Task<string> WaitCodeAsync(long tzid, WaitCodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new WaitCodeOptions();
        var delay = options.Delay ?? Task.Delay;
        var watch = Stopwatch.StartNew();
        var now = options.Now ?? (() => watch.Elapsed);

        var deadline = now() + options.Timeout;
        while (true)
        {
            Operation? op = null;
            try
            {
                op = await StateOneAsync(tzid, messageToCode: !options.FullMessage, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (TransportException)
            {
                // transient: retried until the timeout
            }
            catch (ApiException e) when (Retryable.Contains(e.Code))
            {
                // rate limit (HTTP 429), transient server error, too many concurrent requests: retried until the timeout
            }

            if (op != null)
            {
                var code = op.Msg;
                if (!string.IsNullOrEmpty(code) && code != options.Since)
                {
                    await (options.KeepOpen ? NextAsync(tzid, cancellationToken) : CloseAsync(tzid, cancellationToken)).ConfigureAwait(false);
                    return code!;
                }
                if (op.Response == OperationStatus.OverOk || op.Response == OperationStatus.ErrorNoItems) throw new ApiException(op.Response!);
            }

            var left = deadline - now();
            if (left <= TimeSpan.Zero)
            {
                var ms = ((long)options.Timeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
                throw new WaitTimeoutException($"No code for operation {tzid} within {ms} ms");
            }
            await delay(options.Interval < left ? options.Interval : left, cancellationToken).ConfigureAwait(false);
        }
    }
}
