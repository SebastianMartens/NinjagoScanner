using Microsoft.EntityFrameworkCore;
using NinjagoScanner.Web.Data;

namespace NinjagoScanner.Web.Services;

public enum FriendRelationship
{
    None,
    RequestSent,
    RequestReceived,
    Friends
}

/// <summary>Search hit: deliberately only the user id (for follow-up actions), name and relationship - never email.</summary>
public sealed record FriendSearchResult(string UserId, string UserName, FriendRelationship Relationship);

public enum SendFriendRequestOutcome
{
    Sent,
    BecameFriends,
    AlreadyExists,
    CannotBefriendSelf,
    UserNotFound
}

public sealed record FriendEntry(string FriendshipId, string UserId, string UserName);

public sealed record FriendLists(
    IReadOnlyList<FriendEntry> Friends,
    IReadOnlyList<FriendEntry> Incoming,
    IReadOnlyList<FriendEntry> Outgoing);

/// <summary>
/// Friend request lifecycle (web-friends). All methods take the acting user's id, which callers
/// must derive from the authenticated principal, never from client input.
/// </summary>
public sealed class FriendService(AppDbContext dbContext)
{
    public const int MinSearchLength = 2;
    public const int MaxSearchResults = 20;

    public async Task<IReadOnlyList<FriendSearchResult>> SearchAsync(
        string viewerUserId, string? query, CancellationToken cancellationToken = default)
    {
        var trimmed = query?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length < MinSearchLength)
        {
            return [];
        }

        var normalized = trimmed.ToUpperInvariant();
        var users = await dbContext.Users
            .Where(user => user.Id != viewerUserId
                           && user.NormalizedUserName != null
                           && user.NormalizedUserName.Contains(normalized))
            .OrderBy(user => user.NormalizedUserName)
            .Select(user => new { user.Id, user.UserName })
            .Take(MaxSearchResults)
            .ToListAsync(cancellationToken);

        if (users.Count == 0)
        {
            return [];
        }

        var ids = users.Select(user => user.Id).ToList();
        var relationships = await dbContext.Friendships
            .Where(friendship => (friendship.RequesterUserId == viewerUserId && ids.Contains(friendship.AddresseeUserId))
                                 || (friendship.AddresseeUserId == viewerUserId && ids.Contains(friendship.RequesterUserId)))
            .ToListAsync(cancellationToken);

        return users.Select(user =>
        {
            var friendship = relationships.FirstOrDefault(r =>
                r.RequesterUserId == user.Id || r.AddresseeUserId == user.Id);
            var state = friendship switch
            {
                null => FriendRelationship.None,
                { Status: FriendshipStatus.Accepted } => FriendRelationship.Friends,
                _ when friendship.RequesterUserId == viewerUserId => FriendRelationship.RequestSent,
                _ => FriendRelationship.RequestReceived
            };
            return new FriendSearchResult(user.Id, user.UserName ?? string.Empty, state);
        }).ToList();
    }

    public async Task<SendFriendRequestOutcome> SendRequestAsync(
        string requesterUserId, string addresseeUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(requesterUserId) || string.IsNullOrEmpty(addresseeUserId))
        {
            return SendFriendRequestOutcome.UserNotFound;
        }

        if (requesterUserId == addresseeUserId)
        {
            return SendFriendRequestOutcome.CannotBefriendSelf;
        }

        if (!await dbContext.Users.AnyAsync(user => user.Id == addresseeUserId, cancellationToken))
        {
            return SendFriendRequestOutcome.UserNotFound;
        }

        var (low, high) = Friendship.CanonicalPair(requesterUserId, addresseeUserId);
        var existing = await dbContext.Friendships
            .FirstOrDefaultAsync(f => f.UserLowId == low && f.UserHighId == high, cancellationToken);

        if (existing is not null)
        {
            // Mutual pending: the other side already asked us, so this request acts as acceptance.
            if (existing.Status == FriendshipStatus.Pending && existing.AddresseeUserId == requesterUserId)
            {
                existing.Status = FriendshipStatus.Accepted;
                existing.RespondedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                return SendFriendRequestOutcome.BecameFriends;
            }

            return SendFriendRequestOutcome.AlreadyExists;
        }

        dbContext.Friendships.Add(new Friendship
        {
            RequesterUserId = requesterUserId,
            AddresseeUserId = addresseeUserId,
            UserLowId = low,
            UserHighId = high,
            Status = FriendshipStatus.Pending
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Unique pair index hit by a concurrent request.
            dbContext.ChangeTracker.Clear();
            return SendFriendRequestOutcome.AlreadyExists;
        }

        return SendFriendRequestOutcome.Sent;
    }

    /// <summary>Only the addressee of a pending request can accept it.</summary>
    public async Task<bool> AcceptAsync(string userId, string friendshipId, CancellationToken cancellationToken = default)
    {
        var friendship = await dbContext.Friendships.FirstOrDefaultAsync(f =>
            f.Id == friendshipId && f.AddresseeUserId == userId && f.Status == FriendshipStatus.Pending,
            cancellationToken);
        if (friendship is null)
        {
            return false;
        }

        friendship.Status = FriendshipStatus.Accepted;
        friendship.RespondedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Only the addressee of a pending request can decline it.</summary>
    public async Task<bool> DeclineAsync(string userId, string friendshipId, CancellationToken cancellationToken = default)
    {
        var friendship = await dbContext.Friendships.FirstOrDefaultAsync(f =>
            f.Id == friendshipId && f.AddresseeUserId == userId && f.Status == FriendshipStatus.Pending,
            cancellationToken);
        return await DeleteAsync(friendship, cancellationToken);
    }

    /// <summary>Only the requester of a pending request can cancel it.</summary>
    public async Task<bool> CancelAsync(string userId, string friendshipId, CancellationToken cancellationToken = default)
    {
        var friendship = await dbContext.Friendships.FirstOrDefaultAsync(f =>
            f.Id == friendshipId && f.RequesterUserId == userId && f.Status == FriendshipStatus.Pending,
            cancellationToken);
        return await DeleteAsync(friendship, cancellationToken);
    }

    /// <summary>
    /// Either friend can remove an accepted friendship. Open (pending) trades between the two are
    /// cancelled in the same save; completed trade log entries are untouched.
    /// </summary>
    public async Task<bool> RemoveAsync(string userId, string otherUserId, CancellationToken cancellationToken = default)
    {
        var (low, high) = Friendship.CanonicalPair(userId, otherUserId);
        var friendship = await dbContext.Friendships.FirstOrDefaultAsync(f =>
            f.UserLowId == low && f.UserHighId == high && f.Status == FriendshipStatus.Accepted
            && (f.RequesterUserId == userId || f.AddresseeUserId == userId),
            cancellationToken);
        if (friendship is null)
        {
            return false;
        }

        var openTrades = await dbContext.Trades
            .Where(t => t.Status == TradeStatus.Pending
                        && ((t.ProposerUserId == userId && t.RecipientUserId == otherUserId)
                            || (t.ProposerUserId == otherUserId && t.RecipientUserId == userId)))
            .ToListAsync(cancellationToken);
        foreach (var trade in openTrades)
        {
            trade.Status = TradeStatus.Cancelled;
            trade.ResolvedAt = DateTimeOffset.UtcNow;
            trade.ConcurrencyStamp = Guid.NewGuid().ToString("n");
        }

        dbContext.Friendships.Remove(friendship);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AreFriendsAsync(string userId, string otherUserId, CancellationToken cancellationToken = default)
    {
        var (low, high) = Friendship.CanonicalPair(userId, otherUserId);
        return await dbContext.Friendships.AnyAsync(f =>
            f.UserLowId == low && f.UserHighId == high && f.Status == FriendshipStatus.Accepted,
            cancellationToken);
    }

    public async Task<FriendLists> ListAsync(string userId, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Friendships
            .Where(f => f.RequesterUserId == userId || f.AddresseeUserId == userId)
            .ToListAsync(cancellationToken);

        var otherIds = rows
            .Select(f => f.RequesterUserId == userId ? f.AddresseeUserId : f.RequesterUserId)
            .Distinct()
            .ToList();
        var names = await dbContext.Users
            .Where(user => otherIds.Contains(user.Id))
            .Select(user => new { user.Id, user.UserName })
            .ToDictionaryAsync(user => user.Id, user => user.UserName ?? string.Empty, cancellationToken);

        FriendEntry ToEntry(Friendship f)
        {
            var otherId = f.RequesterUserId == userId ? f.AddresseeUserId : f.RequesterUserId;
            return new FriendEntry(f.Id, otherId, names.GetValueOrDefault(otherId, string.Empty));
        }

        static int ByName(FriendEntry a, FriendEntry b) =>
            string.Compare(a.UserName, b.UserName, StringComparison.OrdinalIgnoreCase);

        List<FriendEntry> Pick(Func<Friendship, bool> predicate)
        {
            var list = rows.Where(predicate).Select(ToEntry).ToList();
            list.Sort(ByName);
            return list;
        }

        return new FriendLists(
            Pick(f => f.Status == FriendshipStatus.Accepted),
            Pick(f => f.Status == FriendshipStatus.Pending && f.AddresseeUserId == userId),
            Pick(f => f.Status == FriendshipStatus.Pending && f.RequesterUserId == userId));
    }

    public Task<int> CountIncomingRequestsAsync(string userId, CancellationToken cancellationToken = default) =>
        dbContext.Friendships.CountAsync(f =>
            f.AddresseeUserId == userId && f.Status == FriendshipStatus.Pending, cancellationToken);

    private async Task<bool> DeleteAsync(Friendship? friendship, CancellationToken cancellationToken)
    {
        if (friendship is null)
        {
            return false;
        }

        dbContext.Friendships.Remove(friendship);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
