using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OnlineSim.Api.Tests;

/// <summary>waitCode through the real client on a fake transport and a fake clock: the delay only moves the clock.</summary>
public class WaitCodeTests
{
    private sealed class Clock
    {
        internal TimeSpan Now { get; private set; }

        internal List<TimeSpan> Sleeps { get; } = new List<TimeSpan>();

        internal WaitCodeOptions Options(Action<WaitCodeOptions>? configure = null)
        {
            var options = new WaitCodeOptions
            {
                Now = () => Now,
                Delay = (span, _) =>
                {
                    Sleeps.Add(span);
                    Now += span;
                    return Task.CompletedTask;
                },
            };
            configure?.Invoke(options);
            return options;
        }
    }

    private static Reply State(string status = "TZ_NUM_WAIT", string? msg = null) =>
        Reply.Json(msg == null ? $$"""[{"tzid":7,"response":"{{status}}"}]""" : $$"""[{"tzid":7,"response":"{{status}}","msg":"{{msg}}"}]""");

    private static readonly Reply Ok = Reply.Json("""{"response":1,"tzid":7}""");

    private static IEnumerable<string> Paths(FakeTransport fake) => fake.Calls.Select(c => c.Url.AbsolutePath.Substring("/api/".Length));

    [Fact]
    public async Task ReturnsTheCodeOnceItArrivesAndClosesTheOperation()
    {
        var (client, fake) = Fake.Client(State(), State(), State("TZ_NUM_ANSWER", "195895"), Ok);
        var clock = new Clock();
        Assert.Equal("195895", await client.Numbers.WaitCodeAsync(7, clock.Options()));
        Assert.Equal(new[] { "getState.php", "getState.php", "getState.php", "setOperationOk.php" }, Paths(fake));
        Assert.All(fake.Calls.Take(3), c => Assert.Equal("1", Fake.Query(c)["message_to_code"]));
        Assert.Equal(new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5) }, clock.Sleeps);
    }

    [Fact]
    public async Task KeepOpenCallsNextSinceSkipsThePreviousCodeFullMessageAsksForText()
    {
        var (client, fake) = Fake.Client(State(msg: "old"), State(msg: "Your code is 2991"), Ok);
        var clock = new Clock();
        var code = await client.Numbers.WaitCodeAsync(7, clock.Options(o =>
        {
            o.KeepOpen = true;
            o.Since = "old";
            o.FullMessage = true;
            o.Interval = TimeSpan.FromSeconds(1);
        }));
        Assert.Equal("Your code is 2991", code);
        Assert.Equal(new[] { "getState.php", "getState.php", "setOperationRevise.php" }, Paths(fake));
        Assert.All(fake.Calls.Take(2), c => Assert.Equal("0", Fake.Query(c)["message_to_code"]));
        Assert.Equal(new[] { TimeSpan.FromSeconds(1) }, clock.Sleeps);
    }

    [Theory]
    [InlineData("TZ_OVER_OK")]
    [InlineData("ERROR_NO_ITEMS")]
    public async Task RaisesApiExceptionAtOnceForAFinishedOperation(string status)
    {
        var (client, fake) = Fake.Client(State(status));
        var e = await Assert.ThrowsAsync<ApiException>(() => client.Numbers.WaitCodeAsync(7, new Clock().Options()));
        Assert.Equal(status, e.Code);
        Assert.Single(fake.Calls);
    }

    [Fact]
    public async Task AFinishedOperationThatStillCarriesANewCodeReturnsIt()
    {
        var (client, _) = Fake.Client(State("TZ_OVER_OK", "1234"), Ok);
        Assert.Equal("1234", await client.Numbers.WaitCodeAsync(7, new Clock().Options()));
    }

    [Fact]
    public async Task RaisesWaitTimeoutExceptionTheLastSleepIsCutToTheDeadline()
    {
        var (client, fake) = Fake.Client(State(), State(), State(), State());
        var clock = new Clock();
        await Assert.ThrowsAsync<WaitTimeoutException>(() => client.Numbers.WaitCodeAsync(7, clock.Options(o =>
        {
            o.Interval = TimeSpan.FromSeconds(4);
            o.Timeout = TimeSpan.FromSeconds(10);
        })));
        Assert.Equal(new[] { 4, 4, 2 }, clock.Sleeps.Select(s => (int)s.TotalSeconds));
        Assert.Equal(4, fake.Calls.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Now);
    }

    [Fact]
    public async Task RetriesATransientTransportException()
    {
        var (client, fake) = Fake.Client(Reply.Throw(new System.Net.Http.HttpRequestException("reset")), State(msg: "42"), Ok);
        var clock = new Clock();
        Assert.Equal("42", await client.Numbers.WaitCodeAsync(7, clock.Options()));
        Assert.Equal(new[] { "getState.php", "getState.php", "setOperationOk.php" }, Paths(fake));
        Assert.Single(clock.Sleeps);
    }

    [Fact]
    public async Task RaisesAnyApiExceptionAtOnce()
    {
        var (client, fake) = Fake.Client(Reply.Json("""{"response":"ERROR_NO_OPERATIONS"}"""));
        Assert.Equal("ERROR_NO_OPERATIONS", (await Assert.ThrowsAsync<ApiException>(() => client.Numbers.WaitCodeAsync(7, new Clock().Options()))).Code);
        Assert.Single(fake.Calls);
    }
}
