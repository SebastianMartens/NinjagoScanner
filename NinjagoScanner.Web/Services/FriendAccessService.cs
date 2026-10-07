using Microsoft.EntityFrameworkCore;
using NinjagoScanner.Web.Data;

namespace NinjagoScanner.Web.Services;

public enum FriendAccessStatus
{
    /// <summary>Unknown user, not an accepted friend, or the viewer is the owner: respond as not found.</summary>
    NotFound,
    /// <summary>Accepted friend, but the owner's visibility is "Nur ich".</summary>
    Private,
    Visible
}

/// <param name="CollectionId">Set only when <see cref="Status"/> is <see cref="FriendAccessStatus.Visible"/>.</param>
public sealed record FriendCollectionAccess(
    FriendAccessStatus Status,
    string? CollectionId = null,
    string? OwnerUserId = null,
    string? OwnerUserName = null)
{
    public static readonly FriendCollectionAccess NotFound = new(FriendAccessStatus.NotFound);
}

/// <summary>
/// The single choke point for reading a foreign collection: a collection ID is only ever
/// returned for an accepted friend whose owner allows "Freunde" visibility. Pages must pass a
/// username (never a collection ID) from the URL and use only the ID returned here.
/// </summary>
public sealed class FriendAccessService(AppDbContext dbContext)
{
    public async Task<FriendCollectionAccess> ResolveVisibleCollectionAsync(
        string viewerUserId, string? username, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(viewerUserId) || string.IsNullOrWhiteSpace(username))
        {
            return FriendCollectionAccess.NotFound;
        }

        var normalized = username.Trim().ToUpperInvariant();
        var owner = await dbContext.Users
            .Where(user => user.NormalizedUserName == normalized)
            .Select(user => new { user.Id, user.UserName })
            .FirstOrDefaultAsync(cancellationToken);
        if (owner is null || owner.Id == viewerUserId)
        {
            return FriendCollectionAccess.NotFound;
        }

        var (low, high) = Friendship.CanonicalPair(viewerUserId, owner.Id);
        var isFriend = await dbContext.Friendships.AnyAsync(f =>
            f.UserLowId == low && f.UserHighId == high && f.Status == FriendshipStatus.Accepted,
            cancellationToken);
        if (!isFriend)
        {
            return FriendCollectionAccess.NotFound;
        }

        var collectionId = await GetOwnedCollectionIdAsync(owner.Id, cancellationToken);
        if (collectionId is null)
        {
            return FriendCollectionAccess.NotFound;
        }

        var visibility = await GetVisibilityAsync(collectionId, cancellationToken);
        return visibility == CollectionVisibility.Friends
            ? new FriendCollectionAccess(FriendAccessStatus.Visible, collectionId, owner.Id, owner.UserName)
            : new FriendCollectionAccess(FriendAccessStatus.Private, null, owner.Id, owner.UserName);
    }

    /// <summary>Missing settings row means the default, <see cref="CollectionVisibility.Friends"/>.</summary>
    public async Task<CollectionVisibility> GetVisibilityAsync(
        string collectionId, CancellationToken cancellationToken = default)
    {
        var settings = await dbContext.CollectionSharingSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.CollectionId == collectionId, cancellationToken);
        return settings?.Visibility ?? CollectionVisibility.Friends;
    }

    public async Task<CollectionVisibility> GetOwnVisibilityAsync(
        string ownerUserId, CancellationToken cancellationToken = default)
    {
        var collectionId = await GetOwnedCollectionIdAsync(ownerUserId, cancellationToken);
        return collectionId is null
            ? CollectionVisibility.Friends
            : await GetVisibilityAsync(collectionId, cancellationToken);
    }

    /// <summary>Sets visibility for the collection the given user owns. Returns false if they own none.</summary>
    public async Task<bool> SetOwnVisibilityAsync(
        string ownerUserId, CollectionVisibility visibility, CancellationToken cancellationToken = default)
    {
        var collectionId = await GetOwnedCollectionIdAsync(ownerUserId, cancellationToken);
        if (collectionId is null)
        {
            return false;
        }

        var settings = await dbContext.CollectionSharingSettings
            .FirstOrDefaultAsync(s => s.CollectionId == collectionId, cancellationToken);
        if (settings is null)
        {
            dbContext.CollectionSharingSettings.Add(new CollectionSharingSettings
            {
                CollectionId = collectionId,
                Visibility = visibility
            });
        }
        else
        {
            settings.Visibility = visibility;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private Task<string?> GetOwnedCollectionIdAsync(string userId, CancellationToken cancellationToken) =>
        dbContext.CollectionMemberships
            .Where(m => m.UserId == userId && m.Role == CollectionRole.Owner)
            .Select(m => (string?)m.CollectionId)
            .FirstOrDefaultAsync(cancellationToken);
}
