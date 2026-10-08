using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NinjagoScanner.Web.Data;

namespace NinjagoScanner.Web.Seeding;

/// <summary>
/// Creates the deterministic local-development data set described by a <see cref="SeedManifest"/>.
/// Idempotent: records that already exist (matched by user name / fixed ID / user pair) are left
/// alone, so re-running yields the same state without duplicates.
/// </summary>
public sealed class LocalSeeder(AppDbContext dbContext, UserManager<AppUser> userManager)
{
    public async Task SeedAsync(SeedManifest manifest, CancellationToken cancellationToken = default)
    {
        var userIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var seedUser in manifest.Users)
        {
            userIds[seedUser.UserName] = await EnsureUserAsync(seedUser, manifest.Password, cancellationToken);
        }

        if (manifest.Friendship is { } friendship)
        {
            await EnsureFriendshipAsync(friendship, userIds, cancellationToken);
        }

        if (manifest.PendingTrade is { } trade)
        {
            await EnsureTradeAsync(trade, manifest, userIds, cancellationToken);
        }
    }

    private async Task<string> EnsureUserAsync(SeedUser seedUser, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByNameAsync(seedUser.UserName);
        if (user is null)
        {
            user = new AppUser { UserName = seedUser.UserName };
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not create seed user '{seedUser.UserName}': {string.Join("; ", result.Errors.Select(e => e.Description))}");
            }
        }

        if (!await dbContext.Collections.AnyAsync(c => c.Id == seedUser.CollectionId, cancellationToken))
        {
            dbContext.Collections.Add(new Collection
            {
                Id = seedUser.CollectionId,
                Name = $"Sammlung von {seedUser.UserName}"
            });
            dbContext.CollectionMemberships.Add(new CollectionMembership
            {
                CollectionId = seedUser.CollectionId,
                UserId = user.Id,
                Role = CollectionRole.Owner
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return user.Id;
    }

    private async Task EnsureFriendshipAsync(
        SeedFriendship friendship, Dictionary<string, string> userIds, CancellationToken cancellationToken)
    {
        var requesterId = userIds[friendship.Requester];
        var addresseeId = userIds[friendship.Addressee];
        var (low, high) = Friendship.CanonicalPair(requesterId, addresseeId);

        if (await dbContext.Friendships.AnyAsync(f => f.UserLowId == low && f.UserHighId == high, cancellationToken))
        {
            return;
        }

        dbContext.Friendships.Add(new Friendship
        {
            RequesterUserId = requesterId,
            AddresseeUserId = addresseeId,
            UserLowId = low,
            UserHighId = high,
            Status = friendship.Status,
            RespondedAt = friendship.Status == FriendshipStatus.Accepted ? DateTimeOffset.UtcNow : null
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureTradeAsync(
        SeedTrade seedTrade, SeedManifest manifest, Dictionary<string, string> userIds, CancellationToken cancellationToken)
    {
        if (await dbContext.Trades.AnyAsync(t => t.Id == seedTrade.Id, cancellationToken))
        {
            return;
        }

        var trade = new Trade
        {
            Id = seedTrade.Id,
            ProposerUserId = userIds[seedTrade.Proposer],
            RecipientUserId = userIds[seedTrade.Recipient],
            ProposerUserName = seedTrade.Proposer,
            RecipientUserName = seedTrade.Recipient,
            ProposerCollectionId = manifest.Users.Single(u => u.UserName == seedTrade.Proposer).CollectionId,
            RecipientCollectionId = manifest.Users.Single(u => u.UserName == seedTrade.Recipient).CollectionId,
            Status = TradeStatus.Pending,
            Items = seedTrade.Items.Select(item => new TradeItem
            {
                Side = item.Side,
                PhotoId = item.PhotoId,
                Reserved = true,
                SeriesName = item.SeriesName,
                CardNumber = item.CardNumber,
                CardName = item.CardName,
                Rarity = item.Rarity
            }).ToList()
        };
        dbContext.Trades.Add(trade);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
