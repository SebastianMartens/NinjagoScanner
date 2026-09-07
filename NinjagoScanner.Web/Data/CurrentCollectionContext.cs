using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace NinjagoScanner.Web.Data;

internal sealed class CurrentCollectionContext(AppDbContext dbContext) : ICurrentCollectionContext
{
    public async Task<Collection?> GetOwnedCollectionAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        return await dbContext.CollectionMemberships
            .Where(membership => membership.UserId == userId && membership.Role == CollectionRole.Owner)
            .Join(dbContext.Collections, membership => membership.CollectionId, collection => collection.Id, (membership, collection) => collection)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
