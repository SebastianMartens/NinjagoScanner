using System.Security.Claims;

namespace NinjagoScanner.Web.Data;

/// <summary>
/// Resolves "the current collection" for a user via a live database query, per web-collections'
/// "Current collection resolution defaults to the user's own collection" requirement - not from
/// auth cookie claims, so a permission change takes effect on the next call rather than waiting
/// on cookie/claim refresh.
/// </summary>
public interface ICurrentCollectionContext
{
    Task<Collection?> GetOwnedCollectionAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
}
