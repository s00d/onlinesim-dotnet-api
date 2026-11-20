using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace OnlineSim.Api.Tests;

public class RentTests
{
    private static readonly Dictionary<string, string> Detail = Fake.Map("pagination", "0", "number", "1");

    private static JsonArray List() => JsonNode.Parse(Fake.Fixture("rent_getRentState"))!["list"]!.AsArray();

    private static string Item(int i) => $$"""{"response":1,"item":{{List()[i]!.ToJsonString()}}}""";

    private static Dictionary<string, string> With(params string[] keyValues) =>
        Fake.Map(keyValues).Concat(Detail).ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public async Task GetSendsExtensionExplicitlyPaginationAndNumberParsesSum()
    {
        var (client, fake) = Fake.Client(Reply.Json(Item(0)));
        var rent = await client.Rent.GetAsync(7, 3);
        Assert.Equal("/api/rent/getRentNum.php", fake.Last.Url.AbsolutePath);
        Assert.Equal(With("country", "7", "days", "3", "extension", "1"), Fake.Query(fake.Last));
        Assert.Equal(5.14m, rent.Sum);
        Assert.Equal(new Dictionary<int, decimal> { [1] = 14.7m, [3] = 25.3m, [7] = 51m }, rent.Extend);
        Assert.Equal("195895", rent.Messages[0].Code);
        Assert.Equal((430L, "781746533", 77, 300578L, 18034680L, 1), (rent.Tzid, rent.Number, rent.Country, rent.Time, rent.TimeSeconds, rent.Extension));
    }

    [Fact]
    public async Task DecodesTheSpecShapesSumNumberOrStringCode0ForANotifySms()
    {
        var item = List()[0]!.AsObject();
        item["status"] = 0;
        item["checked"] = false;
        item["sum"] = 1.5m;
        item["messages"] = JsonNode.Parse("""
            [{"id":2,"service":"notify","text":"Messages from this sender are not accepted","code":0,"created_at":"2026-11-19 10:01:12"},
             {"id":1,"service":"Google","text":"G-070051","code":"070051","created_at":"2026-11-19 10:01:00"}]
            """);
        var got = $$"""{"response":1,"item":{{item.ToJsonString()}}}""";
        item["sum"] = "2.40";
        var listed = $$"""{"response":1,"list":[{{item.ToJsonString()}}]}""";
        var (client, _) = Fake.Client(Reply.Json(got), Reply.Json(listed));
        var rent = await client.Rent.GetAsync(7, 3);
        Assert.Equal((0, (bool?)false, 1.5m), (rent.Status, rent.Checked, rent.Sum));
        Assert.Equal(new[] { "0", "070051" }, rent.Messages.Select(m => m.Code)); // the integer 0 is normalised to "0"
        Assert.Equal(2.4m, (await client.Rent.StateOneAsync(rent.Tzid)).Sum);
    }

    [Fact]
    public async Task GetWithExtensionFalseSendsExtension0()
    {
        var (client, fake) = Fake.Client(Reply.Json(Item(1)));
        await client.Rent.GetAsync(7, 1, extension: false);
        Assert.Equal("0", Fake.Query(fake.Last)["extension"]);
    }

    [Fact]
    public async Task StateNormalisesSumAndTheEmptyExtendMap()
    {
        var (client, fake) = Fake.Client(Reply.Json(Fake.Fixture("rent_getRentState")));
        var list = await client.Rent.StateAsync();
        Assert.Equal("/api/rent/getRentState.php", fake.Last.Url.AbsolutePath);
        Assert.Equal(Detail, Fake.Query(fake.Last));
        Assert.Equal(new[] { 5.14m, 0m }, list.Select(r => r.Sum));
        Assert.Empty(list[1].Extend);
    }

    [Fact]
    public async Task StateOne()
    {
        var (client, fake) = Fake.Client(Reply.Json($$"""{"response":1,"list":[{{List()[0]!.ToJsonString()}}]}"""));
        var rent = await client.Rent.StateOneAsync(430);
        Assert.Equal((430L, 5.14m), (rent.Tzid, rent.Sum));
        Assert.Equal(With("tzid", "430"), Fake.Query(fake.Last));
    }

    [Fact]
    public async Task StateOneRaisesErrorNoOperationsForAnEmptyList()
    {
        var (client, _) = Fake.Client(Reply.Json("""{"response":1,"list":[]}"""));
        Assert.Equal("ERROR_NO_OPERATIONS", (await Assert.ThrowsAsync<ApiException>(() => client.Rent.StateOneAsync(1))).Code);
    }

    [Fact]
    public async Task Extend()
    {
        var (client, fake) = Fake.Client(Reply.Json(Item(0)));
        Assert.Equal(430, (await client.Rent.ExtendAsync(430, 3)).Tzid);
        Assert.Equal("/api/rent/extendRentState.php", fake.Last.Url.AbsolutePath);
        Assert.Equal(With("tzid", "430", "days", "3"), Fake.Query(fake.Last));
    }

    [Fact]
    public async Task Tariffs()
    {
        var (client, fake) = Fake.Client(Reply.Json(Fake.Fixture("rent_tariffsRent")));
        var tariffs = await client.Rent.TariffsAsync();
        Assert.Equal("/api/rent/tariffsRent.php", fake.Last.Url.AbsolutePath);
        Assert.Empty(Fake.Query(fake.Last));
        Assert.Equal(new[] { 7, 380 }, tariffs.Keys.OrderBy(k => k));
        Assert.Equal(9.11m, tariffs[7].Days[7]);
        Assert.Null(tariffs[380].ExtCounts);
    }

    [Fact]
    public async Task TariffsOne()
    {
        var ru = JsonNode.Parse(Fake.Fixture("rent_tariffsRent"))!["7"]!.ToJsonString();
        var (client, fake) = Fake.Client(Reply.Json(ru));
        var tariff = await client.Rent.TariffsOneAsync();
        Assert.Equal(Fake.Map("country", "7"), Fake.Query(fake.Last));
        Assert.Equal((7, "russia", 1, false), (tariff.Code, tariff.Name, tariff.Extend, tariff.Confirm));
        Assert.Equal(new Dictionary<int, decimal> { [3] = 5.14m, [7] = 9.11m, [15] = 16.07m, [30] = 24.11m }, tariff.Days);
        Assert.Equal(1, tariff.Count[30]);
    }

    [Fact]
    public async Task TariffsOneRaisesNoCountryForTheEmptyArrayAnswer()
    {
        var (client, _) = Fake.Client(Reply.Json("[]"));
        Assert.Equal("NO_COUNTRY", (await Assert.ThrowsAsync<ApiException>(() => client.Rent.TariffsOneAsync(999))).Code);
    }

    [Fact]
    public async Task Close()
    {
        var (client, fake) = Fake.Client(Reply.Json("""{"response":"1"}"""));
        await client.Rent.CloseAsync(430);
        Assert.Equal("/api/rent/closeRentNum.php", fake.Last.Url.AbsolutePath);
        Assert.Equal(Fake.Map("tzid", "430"), Fake.Query(fake.Last));
    }

    [Fact]
    public async Task CloseSurfacesNoCloseRentUs()
    {
        var (client, _) = Fake.Client(Reply.Json("""{"response":"NO_CLOSE_RENT_US"}"""));
        Assert.Equal("NO_CLOSE_RENT_US", (await Assert.ThrowsAsync<ApiException>(() => client.Rent.CloseAsync(1))).Code);
    }

    [Fact]
    public void FixtureIsValidJson()
    {
        using var doc = JsonDocument.Parse(Fake.Fixture("rent_getRentState"));
        Assert.Equal(2, doc.RootElement.GetProperty("list").GetArrayLength());
    }
}
