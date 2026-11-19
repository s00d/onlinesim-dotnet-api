using System.Threading;
using System.Threading.Tasks;

namespace OnlineSim.Api;

/// <summary>Public free numbers: <c>client.Free</c>. No key needed.</summary>
public sealed class FreeApi
{
    private readonly RequestFn _request;

    internal FreeApi(RequestFn request) => _request = request;

    /// <summary>Countries, the numbers of the selected country and the SMS of the selected number, in one call.</summary>
    /// <param name="country">Country code; server default 44.</param>
    /// <param name="number">Number without the country code, as the keys of <see cref="FreeList.Numbers"/>.</param>
    /// <param name="page">Page of <see cref="FreeList.Messages"/>.</param>
    /// <param name="count">SMS per page, 10–500.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task<FreeList> ListAsync(int? country = null, string? number = null, int? page = null, int? count = null, CancellationToken cancellationToken = default)
    {
        var query = new Query().Add("country", country).Add("number", number).Add("page", page).Add("count", count);
        return Json.Read<FreeList>(await _request("getFreeList", query, null, cancellationToken).ConfigureAwait(false));
    }
}
