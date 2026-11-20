using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OnlineSim.Api;

/// <summary>Options of <see cref="OnlineSimClient"/>. Every property is optional.</summary>
public sealed class OnlineSimClientOptions
{
    /// <summary>API key, sent as <c>Authorization: Bearer &lt;ApiKey&gt;</c>, never in the URL. Not needed for tariffs and free numbers.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Full API base; trailing slash optional. <c>null</c>: <see cref="OnlineSimClient.DefaultBaseUrl"/>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Language of server texts (<c>lang</c> query parameter). Default <c>en</c>.</summary>
    public string Lang { get; set; } = "en";

    /// <summary>Partner developer id (<c>dev_id</c> query parameter), sent only when set.</summary>
    public string? DevId { get; set; }

    /// <summary>Per-request timeout. Default 30 s; <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> disables it.</summary>
    public TimeSpan Timeout { get; set; } = OnlineSimClient.DefaultTimeout;

    /// <summary>Transport. <c>null</c>: <see cref="HttpClientTransport"/> on a shared <see cref="System.Net.Http.HttpClient"/>.</summary>
    public ITransport? Transport { get; set; }

    internal OnlineSimClientOptions Copy() => (OnlineSimClientOptions)MemberwiseClone();
}

/// <summary>The one request function the sections call: <paramref name="endpoint"/> is relative to the base URL, suffix included (<c>getNum.php</c>).</summary>
internal delegate Task<JsonElement> RequestFn(string endpoint, Query? query, object? body, CancellationToken cancellationToken);

/// <summary>
/// OnlineSim API client. Thread-safe; create one and reuse it.
/// <code>
/// var client = new OnlineSimClient(Environment.GetEnvironmentVariable("ONLINESIM_KEY"));
/// var balance = await client.User.BalanceAsync();
/// </code>
/// </summary>
public sealed class OnlineSimClient
{
    /// <summary>Default API base.</summary>
    public const string DefaultBaseUrl = "https://onlinesim.host/api/";

    /// <summary>Default per-request timeout, 30 s.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private const int BodyExcerpt = 500;

    private readonly string? _apiKey;
    private readonly Uri _baseUrl;
    private readonly string _lang;
    private readonly string? _devId;
    private readonly TimeSpan _timeout;
    private readonly ITransport _transport;

    /// <summary>Client with an API key and the default options.</summary>
    public OnlineSimClient(string? apiKey) : this(new OnlineSimClientOptions { ApiKey = apiKey }) { }

    /// <summary>Client with <paramref name="options"/>; <c>null</c> means all defaults (no key).</summary>
    public OnlineSimClient(OnlineSimClientOptions? options = null)
    {
        options ??= new OnlineSimClientOptions();
        _apiKey = string.IsNullOrEmpty(options.ApiKey) ? null : options.ApiKey;
        var baseUrl = options.BaseUrl ?? DefaultBaseUrl;
        _baseUrl = new Uri(baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : baseUrl + "/", UriKind.Absolute);
        _lang = options.Lang;
        _devId = string.IsNullOrEmpty(options.DevId) ? null : options.DevId;
        if (options.Timeout <= TimeSpan.Zero && options.Timeout != System.Threading.Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Timeout must be positive or Timeout.InfiniteTimeSpan");
        }
        _timeout = options.Timeout;
        _transport = options.Transport ?? new HttpClientTransport();

        RequestFn request = RequestAsync;
        Numbers = new NumbersApi(request);
        Rent = new RentApi(request);
        User = new UserApi(request);
        Free = new FreeApi(request);
    }

    /// <summary>Numbers for receiving SMS.</summary>
    public NumbersApi Numbers { get; }

    /// <summary>Rented numbers.</summary>
    public RentApi Rent { get; }

    /// <summary>Account: balance, profile, payments, webhook URL.</summary>
    public UserApi User { get; }

    /// <summary>Public free numbers (no key needed).</summary>
    public FreeApi Free { get; }

    private async Task<JsonElement> RequestAsync(string endpoint, Query? query, object? body, CancellationToken cancellationToken)
    {
        var pairs = (query?.Pairs ?? Array.Empty<KeyValuePair<string, string>>()).ToList();
        pairs.Add(new KeyValuePair<string, string>("lang", _lang));
        if (_devId != null) pairs.Add(new KeyValuePair<string, string>("dev_id", _devId));
        var url = new UriBuilder(new Uri(_baseUrl, endpoint))
        {
            Query = string.Join("&", pairs.Where(p => p.Value.Length > 0).Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value))),
        }.Uri;

        var headers = new Dictionary<string, string> { ["Accept"] = "application/json" };
        if (_apiKey != null) headers["Authorization"] = "Bearer " + _apiKey;
        string? json = null;
        var method = "GET";
        if (body != null)
        {
            method = "POST";
            headers["Content-Type"] = "application/json";
            json = JsonSerializer.Serialize(body);
        }

        TransportResponse response;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(_timeout);
            try
            {
                response = await _transport.SendAsync(new TransportRequest(method, url, headers, json), timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException e) when (timeout.IsCancellationRequested)
            {
                var ms = ((long)_timeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
                throw new TransportException($"Request timed out after {ms} ms", null, "", e);
            }
            catch (Exception e) when (!(e is OnlineSimException))
            {
                throw new TransportException("Network error: " + e.Message, null, "", e);
            }
        }

        JsonElement data;
        try
        {
            using var doc = JsonDocument.Parse(response.Body);
            data = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new TransportException($"HTTP {response.Status}: response is not JSON", response.Status, Excerpt(response.Body));
        }

        // The body is checked before the status: a wrong key is HTTP 456 with {"response":"ERROR_WRONG_KEY"},
        // the rate limit HTTP 429 with {"response":"TRY_AGAIN_LATER"}.
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("response", out var code))
        {
            Unwrap(data, code);
            return data;
        }
        if (response.Status < 200 || response.Status >= 300)
        {
            throw new TransportException($"HTTP {response.Status}", response.Status, Excerpt(response.Body));
        }
        return data;
    }

    /// <summary>Success (<c>1</c> / <c>"1"</c>): return. An error code: throw.</summary>
    private static void Unwrap(JsonElement data, JsonElement response)
    {
        if (response.ValueKind == JsonValueKind.Number && response.TryGetInt64(out var n) && n == 1) return;
        if (response.ValueKind == JsonValueKind.String && response.GetString() == "1") return;

        if (response.ValueKind == JsonValueKind.Object)
        {
            // profile.php validation errors: {"response":{"webhook_url":["The webhook url must be a valid URL."]}}
            var messages = new List<string>();
            foreach (var field in response.EnumerateObject())
            {
                if (field.Value.ValueKind == JsonValueKind.Array) messages.AddRange(field.Value.EnumerateArray().Select(Text));
                else messages.Add(Text(field.Value));
            }
            throw new ApiException("VALIDATION_ERROR", messages.Count > 0 ? string.Join("; ", messages) : null);
        }

        var error = Text(response);
        // Debug backends echo the code as `message`; prefer the readable fallback then.
        string? message = null;
        if (data.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String && m.GetString() != error) message = m.GetString();
        if (error == "NO_NUMBER") throw new NoNumberException(error, message);
        throw new ApiException(error, message);
    }

    private static string Text(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();

    internal static string Excerpt(string body) => body.Length <= BodyExcerpt ? body : body.Substring(0, BodyExcerpt);
}
