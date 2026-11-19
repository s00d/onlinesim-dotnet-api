using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OnlineSim.Api.Tests;

/// <summary>A canned answer: a JSON (or raw) body with a status, or an exception thrown by the transport.</summary>
internal sealed class Reply
{
    private Reply(int status, string body, Exception? error)
    {
        Status = status;
        Body = body;
        Error = error;
    }

    internal int Status { get; }

    internal string Body { get; }

    internal Exception? Error { get; }

    internal static Reply Json(string json, int status = 200) => new Reply(status, json, null);

    internal static Reply Raw(string body, int status) => new Reply(status, body, null);

    internal static Reply Throw(Exception error) => new Reply(0, "", error);
}

/// <summary>Transport that answers <see cref="Reply"/>s in order and records every request.</summary>
internal sealed class FakeTransport : ITransport
{
    private readonly Queue<Reply> _replies;

    internal FakeTransport(IEnumerable<Reply> replies) => _replies = new Queue<Reply>(replies);

    internal List<TransportRequest> Calls { get; } = new List<TransportRequest>();

    /// <summary>The only call made; fails when there were more or none.</summary>
    internal TransportRequest Last
    {
        get
        {
            Assert.Single(Calls);
            return Calls[0];
        }
    }

    public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        Calls.Add(request);
        if (_replies.Count == 0) throw new InvalidOperationException("unexpected request " + request.Url);
        var reply = _replies.Dequeue();
        if (reply.Error != null) throw reply.Error;
        return Task.FromResult(new TransportResponse(reply.Status, reply.Body));
    }
}

internal static class Fake
{
    /// <summary>Client with key <c>test-key</c> on <c>https://api.test/api/</c> over a fake transport.</summary>
    internal static (OnlineSimClient Client, FakeTransport Transport) Client(params Reply[] replies) => Client(null, replies);

    internal static (OnlineSimClient Client, FakeTransport Transport) Client(Action<OnlineSimClientOptions>? configure, params Reply[] replies)
    {
        var transport = new FakeTransport(replies);
        var options = new OnlineSimClientOptions { ApiKey = "test-key", BaseUrl = "https://api.test/api/", Transport = transport };
        configure?.Invoke(options);
        return (new OnlineSimClient(options), transport);
    }

    /// <summary>Query of a call as a dictionary, without the <c>lang</c> every request carries.</summary>
    internal static Dictionary<string, string> Query(TransportRequest call)
    {
        var q = AllQuery(call);
        q.Remove("lang");
        return q;
    }

    internal static Dictionary<string, string> AllQuery(TransportRequest call) =>
        call.Url.Query.TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split(new[] { '=' }, 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");

    internal static Dictionary<string, string> Map(params string[] keyValues)
    {
        var map = new Dictionary<string, string>();
        for (var i = 0; i < keyValues.Length; i += 2) map[keyValues[i]] = keyValues[i + 1];
        return map;
    }

    internal static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".json"));
}
