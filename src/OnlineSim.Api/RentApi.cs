using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OnlineSim.Api;

/// <summary>Rented numbers: <c>client.Rent</c>.</summary>
public sealed class RentApi
{
    private readonly RequestFn _request;

    internal RentApi(RequestFn request) => _request = request;

    /// <summary>Rent a number for <paramref name="days"/> days and charge the balance.</summary>
    /// <param name="country">Country code.</param>
    /// <param name="days">One of the tariff's <see cref="RentTariff.Days"/> keys.</param>
    /// <param name="extension"><c>false</c> makes the rent non-extendable for good.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task<RentItem> GetAsync(int country = 7, int days = 1, bool extension = true, CancellationToken cancellationToken = default)
    {
        var query = Detail(new Query().Add("country", country).Add("days", days).Add("extension", extension));
        return Json.Read<RentItem>(Item(await _request("rent/getRentNum.php", query, null, cancellationToken).ConfigureAwait(false), "item"));
    }

    /// <summary>All rents of the account.</summary>
    public async Task<IReadOnlyList<RentItem>> StateAsync(CancellationToken cancellationToken = default) =>
        Json.Read<List<RentItem>>(Item(await _request("rent/getRentState.php", Detail(new Query()), null, cancellationToken).ConfigureAwait(false), "list"));

    /// <summary>One rent. Throws <see cref="ApiException"/> <c>ERROR_NO_OPERATIONS</c> when it does not exist.</summary>
    public async Task<RentItem> StateOneAsync(long tzid, CancellationToken cancellationToken = default)
    {
        var res = await _request("rent/getRentState.php", Detail(new Query().Add("tzid", tzid)), null, cancellationToken).ConfigureAwait(false);
        var list = Json.Read<List<RentItem>>(Item(res, "list"));
        if (list.Count == 0) throw new ApiException("ERROR_NO_OPERATIONS");
        return list[0];
    }

    /// <summary>Extend a rent by <paramref name="days"/> days and charge the balance (only rents created with <c>extension: true</c>).</summary>
    public async Task<RentItem> ExtendAsync(long tzid, int days = 1, CancellationToken cancellationToken = default)
    {
        var query = Detail(new Query().Add("tzid", tzid).Add("days", days));
        return Json.Read<RentItem>(Item(await _request("rent/extendRentState.php", query, null, cancellationToken).ConfigureAwait(false), "item"));
    }

    /// <summary>Rent prices of every country, keyed by country code. No key needed.</summary>
    public async Task<IReadOnlyDictionary<int, RentTariff>> TariffsAsync(CancellationToken cancellationToken = default) =>
        Json.Read<IReadOnlyDictionary<int, RentTariff>>(await _request("rent/tariffsRent.php", null, null, cancellationToken).ConfigureAwait(false));

    /// <summary>Rent prices of one country. Throws <see cref="ApiException"/> <c>NO_COUNTRY</c> for a country without rent. No key needed.</summary>
    public async Task<RentTariff> TariffsOneAsync(int country = 7, CancellationToken cancellationToken = default)
    {
        var res = await _request("rent/tariffsRent.php", new Query().Add("country", country), null, cancellationToken).ConfigureAwait(false);
        // An unknown country answers `[]`.
        if (res.ValueKind != JsonValueKind.Object || !res.TryGetProperty("code", out _)) throw new ApiException("NO_COUNTRY");
        return Json.Read<RentTariff>(res);
    }

    /// <summary>Return the number before the rent period ends.</summary>
    public Task CloseAsync(long tzid, CancellationToken cancellationToken = default) =>
        _request("rent/closeRentNum.php", new Query().Add("tzid", tzid), null, cancellationToken);

    // Always sent: flat `messages` list and `time_seconds`.
    private static Query Detail(Query query) => query.Add("pagination", false).Add("number", 1);

    private static JsonElement Item(JsonElement res, string name) =>
        res.ValueKind == JsonValueKind.Object && res.TryGetProperty(name, out var value)
            ? value
            : throw new TransportException($"Unexpected response shape: no `{name}`", null, OnlineSimClient.Excerpt(res.GetRawText()));
}
