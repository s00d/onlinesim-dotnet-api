using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace OnlineSim.Api;

/// <summary>
/// In-process fake OnlineSim API for tests: no network, no key, no money, no clock.
/// It is an <see cref="ITransport"/> (<see cref="Transport"/>) that answers like the real backend;
/// <see cref="Client"/> returns a normal <see cref="OnlineSimClient"/> wired to it. Test helpers add SMS,
/// queue failures and expose the state. Thread-safe.
/// <code>
/// var mock = new MockOnlineSim(new MockOptions { Balance = 10 });
/// var client = mock.Client();
/// var tzid = await client.Numbers.GetAsync("google", 44);
/// mock.AddSmsAfter(tzid, "Your code: 123-456", 2);
/// var code = await client.Numbers.WaitCodeAsync(tzid, new WaitCodeOptions { Interval = TimeSpan.Zero }); // "123456"
/// </code>
/// </summary>
public sealed class MockOnlineSim : ITransport
{
    /// <summary>Base URL of clients made by <see cref="Client"/>. Never contacted: the mock answers in-process.</summary>
    public const string MockBaseUrl = "https://mock.onlinesim.invalid/api/";

    private readonly object _gate = new object();
    private readonly MockState _state;

    /// <summary>Creates the mock; <c>null</c> means all defaults.</summary>
    public MockOnlineSim(MockOptions? options = null)
    {
        _state = new MockState(options ?? new MockOptions());
    }

    /// <summary>The fake transport; set it as <see cref="OnlineSimClientOptions.Transport"/> when you build the client yourself. Any base URL works.</summary>
    public ITransport Transport => this;

    /// <summary>Every request received, in order (a copy).</summary>
    public IReadOnlyList<MockRequest> Requests
    {
        get
        {
            lock (_gate) return _state.Requests.ToArray();
        }
    }

    /// <summary>
    /// A normal client wired to the mock. <see cref="OnlineSimClientOptions.ApiKey"/> defaults to the mock's key
    /// (set another one, or <c>""</c> for none, to test a wrong key); <see cref="OnlineSimClientOptions.Transport"/> is ignored.
    /// </summary>
    public OnlineSimClient Client(OnlineSimClientOptions? options = null)
    {
        var o = options?.Copy() ?? new OnlineSimClientOptions();
        o.ApiKey ??= _state.ApiKey;
        o.BaseUrl ??= MockBaseUrl;
        o.Transport = this;
        return new OnlineSimClient(o);
    }

    /// <summary>An SMS arrives now at number operation or rent <paramref name="tzid"/>. Throws <see cref="InvalidOperationException"/> when there is no open operation or rent with that id.</summary>
    /// <param name="tzid">Operation or rent id.</param>
    /// <param name="text">SMS text; the code is its first run of 4–8 digits.</param>
    /// <param name="service">Service (sender); default the operation's service, <c>other</c> for rents.</param>
    public void AddSms(long tzid, string text, string? service = null)
    {
        lock (_gate)
        {
            if (!_state.Deliver(tzid, text, service)) throw new InvalidOperationException($"MockOnlineSim: no open operation or rent {tzid}");
        }
    }

    /// <summary>
    /// The SMS arrives after <paramref name="polls"/> state requests for <paramref name="tzid"/> (getState / rent
    /// getRentState, listing it alone or with others): with <c>polls = 2</c> the third poll sees it.
    /// Lets <see cref="NumbersApi.WaitCodeAsync"/> run with a zero interval.
    /// </summary>
    /// <param name="tzid">Operation or rent id.</param>
    /// <param name="text">SMS text.</param>
    /// <param name="polls">State requests to pass first.</param>
    /// <param name="service">Service (sender).</param>
    public void AddSmsAfter(long tzid, string text, int polls, string? service = null)
    {
        lock (_gate)
        {
            if (!_state.Operations.ContainsKey(tzid) && !_state.Rents.ContainsKey(tzid))
            {
                throw new InvalidOperationException($"MockOnlineSim: no operation or rent {tzid}");
            }
            _state.Schedule(tzid, text, polls, service);
        }
    }

    /// <summary>The next request (to <paramref name="endpoint"/> only, e.g. <c>getNum.php</c>, when given) answers <c>{"response": code}</c>.</summary>
    public void FailNext(string code, string? endpoint = null)
    {
        lock (_gate) _state.Failures.Add(new Failure(endpoint, code, 200));
    }

    /// <summary>The next request (to <paramref name="endpoint"/> only, when given) fails at transport level: a non-JSON body with HTTP <paramref name="status"/>.</summary>
    public void FailNextTransport(int status = 502, string? endpoint = null)
    {
        lock (_gate) _state.Failures.Add(new Failure(endpoint, null, status));
    }

    /// <summary>Set the total balance, USD.</summary>
    public void SetBalance(decimal usd)
    {
        lock (_gate) _state.Balance = MockState.ToMills(usd);
    }

    /// <summary>Total balance, USD, including the part frozen by open operations (as <see cref="UserApi.BalanceAsync"/> reports it).</summary>
    public decimal Balance()
    {
        lock (_gate) return MockState.FromMills(_state.Balance);
    }

    /// <summary>Number operations, oldest first (snapshots).</summary>
    public IReadOnlyList<MockOperation> Operations()
    {
        lock (_gate) return _state.Operations.Values.Select(o => o.Snapshot()).ToArray();
    }

    /// <summary>Open rents, oldest first (snapshots).</summary>
    public IReadOnlyList<MockRent> Rents()
    {
        lock (_gate) return _state.Rents.Values.Select(r => r.Snapshot()).ToArray();
    }

    /// <summary>Back to the constructor's state: balance, no operations, rents, logs, schedules or failures.</summary>
    public void Reset()
    {
        lock (_gate) _state.Reset();
    }

    Task<TransportResponse> ITransport.SendAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(Handle(request));
    }

    private TransportResponse Handle(TransportRequest request)
    {
        var absolute = request.Url.AbsolutePath;
        var at = absolute.IndexOf("/api/", StringComparison.Ordinal);
        var path = at < 0 ? absolute.TrimStart('/') : absolute.Substring(at + 5);
        var req = new MockRequest(request.Method.ToUpperInvariant(), path, ParseQuery(request.Url.Query), request.Body);
        _state.Requests.Add(req);

        var failure = _state.TakeFailure(req.Path);
        if (failure != null)
        {
            return failure.Code != null
                ? Json(new JsonObject { ["response"] = failure.Code })
                : Text(failure.Status, "Mock transport failure");
        }

        if (!MockHandlers.Routes.TryGetValue(req.Path, out var handler)) return Text(404, "Not Found");
        if (!MockHandlers.PublicPaths.Contains(req.Path) && Authorization(request) != "Bearer " + _state.ApiKey)
        {
            return Json(new JsonObject { ["response"] = "ERROR_WRONG_KEY" }, 456);
        }
        return Json(handler(_state, req));
    }

    private static string? Authorization(TransportRequest request) =>
        request.Headers.FirstOrDefault(h => string.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase)).Value;

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>();
        foreach (var pair in query.TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = Decode(eq < 0 ? pair : pair.Substring(0, eq));
            result[key] = eq < 0 ? "" : Decode(pair.Substring(eq + 1));
        }
        return result;
    }

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));

    private static TransportResponse Json(JsonNode body, int status = 200) => new TransportResponse(status, body.ToJsonString());

    private static TransportResponse Text(int status, string body) => new TransportResponse(status, $"<html><body>{status} {body}</body></html>");
}
