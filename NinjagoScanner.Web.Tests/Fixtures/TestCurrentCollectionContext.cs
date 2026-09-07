using System.Security.Claims;
using NinjagoScanner.Web.Data;

namespace NinjagoScanner.Web.Tests.Fixtures;

/// <summary>
/// Always resolves to the same fixed collection, for tests that don't specifically exercise
/// multi-collection isolation or missing-membership behavior.
/// </summary>
internal sealed class TestCurrentCollectionContext : ICurrentCollectionContext
{
    public const string CollectionId = "test-collection";

    public Task<Collection?> GetOwnedCollectionAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Collection?>(new Collection { Id = CollectionId, Name = "Test Collection" });
    }
}
