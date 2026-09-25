using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using NinjagoScanner.PictureService.Protos;
using NinjagoScanner.Web.Data;
using NinjagoScanner.Web.Models;

namespace NinjagoScanner.Web.Services;

/// <summary>
/// Achievement/XP/rank evaluation (web-achievements, web-rank-progression). Recomputes everything
/// from the collection's catalog + photo data on every call - see design.md's "Progress is
/// derived, never stored" - and only persists the first time each achievement's goal is crossed
/// (AchievementUnlock), so a later review correction that lowers the collection's state is
/// reflected immediately in what's shown as unlocked, without ever un-writing history.
/// Scoped (one instance per Blazor circuit, same as PictureServiceClient) so it can resolve and
/// cache the acting user's collection_id for that circuit's lifetime.
/// </summary>
internal sealed class GamificationService(
    CatalogServiceClient catalogServiceClient,
    PictureServiceClient pictureServiceClient,
    AppDbContext dbContext,
    ICurrentCollectionContext currentCollectionContext,
    AuthenticationStateProvider authenticationStateProvider)
{
    internal const int XpNewCard = 50;
    internal const int XpConfirmedReview = 10;

    private string? cachedCollectionId;

    public static RankDefinition GetRank(int xp) => RankDefinitions.ForXp(xp);

    /// <summary>The current derived XP total, with no side effects - used by callers to snapshot "before" state ahead of an action, then compared via <see cref="EvaluateAsync"/> once it's done.</summary>
    public async Task<int> GetXpAsync(CancellationToken cancellationToken = default)
    {
        return await GetXpAsync(await BuildStateAsync(cancellationToken), cancellationToken);
    }

    /// <summary>
    /// <see cref="GetXpAsync(CancellationToken)"/> computed from a snapshot the caller already holds
    /// in memory (the /review page's session) instead of re-fetching the catalog and the whole
    /// collection - which made every review action pay for two full collection listings.
    /// </summary>
    public Task<int> GetXpAsync(ReviewSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        return GetXpAsync(BuildState(snapshot), cancellationToken);
    }

    private async Task<int> GetXpAsync(GamificationCollectionState state, CancellationToken cancellationToken)
    {
        var collectionId = await GetCollectionIdAsync(cancellationToken);
        var bonusXp = await GetBonusXpAsync(collectionId, cancellationToken);
        return ComputeXp(state, CurrentlyUnlockedIds(state), bonusXp);
    }

    /// <summary>Every achievement's live progress, for the Achievements page. Opportunistically persists any achievement this call finds newly crossed, so its unlock date is available from the first read.</summary>
    public async Task<IReadOnlyList<AchievementProgress>> GetProgressAsync(CancellationToken cancellationToken = default)
    {
        var collectionId = await GetCollectionIdAsync(cancellationToken);
        var state = await BuildStateAsync(cancellationToken);
        var (unlockDates, _) = await SyncUnlocksAsync(collectionId, state, cancellationToken);

        return AchievementDefinitions.All
            .Select(achievement =>
            {
                var goal = Math.Max(1, achievement.GoalRule(state));
                var current = Math.Max(0, achievement.ProgressRule(state));
                var isUnlocked = current >= goal;
                var unlockedAt = isUnlocked && unlockDates.TryGetValue(achievement.Id, out var at) ? at : (DateTime?)null;

                return new AchievementProgress
                {
                    Achievement = achievement,
                    Current = Math.Min(current, goal),
                    Goal = goal,
                    IsUnlocked = isUnlocked,
                    UnlockedAtUtc = unlockedAt
                };
            })
            .ToArray();
    }

    /// <summary>
    /// Re-evaluates the collection after an action that may have changed Owned Copies or review
    /// confirmations, persists any newly-crossed achievement unlocks, and reports what changed
    /// relative to the <paramref name="xpBefore"/> the caller captured before the action - for the
    /// unlock-feedback overlay/toasts (web-unlock-feedback) to decide what to celebrate.
    /// </summary>
    public async Task<GamificationEvaluationResult> EvaluateAsync(int xpBefore, CancellationToken cancellationToken = default)
    {
        return await EvaluateAsync(xpBefore, await BuildStateAsync(cancellationToken), cancellationToken);
    }

    /// <summary><see cref="EvaluateAsync(int, CancellationToken)"/> computed from an in-memory snapshot - see <see cref="GetXpAsync(ReviewSnapshot, CancellationToken)"/>.</summary>
    public Task<GamificationEvaluationResult> EvaluateAsync(int xpBefore, ReviewSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        return EvaluateAsync(xpBefore, BuildState(snapshot), cancellationToken);
    }

    private async Task<GamificationEvaluationResult> EvaluateAsync(int xpBefore, GamificationCollectionState state, CancellationToken cancellationToken)
    {
        var collectionId = await GetCollectionIdAsync(cancellationToken);
        var (_, newlyUnlocked) = await SyncUnlocksAsync(collectionId, state, cancellationToken);

        var bonusXp = await GetBonusXpAsync(collectionId, cancellationToken);
        var xpAfter = ComputeXp(state, CurrentlyUnlockedIds(state), bonusXp);

        return new GamificationEvaluationResult
        {
            NewlyUnlockedAchievements = newlyUnlocked,
            XpBefore = xpBefore,
            XpAfter = xpAfter,
            RankBefore = RankDefinitions.ForXp(xpBefore),
            RankAfter = RankDefinitions.ForXp(xpAfter)
        };
    }

    private static HashSet<string> CurrentlyUnlockedIds(GamificationCollectionState state)
    {
        return AchievementDefinitions.All
            .Where(achievement => IsCurrentlyUnlocked(achievement, state))
            .Select(achievement => achievement.Id)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsCurrentlyUnlocked(Achievement achievement, GamificationCollectionState state)
    {
        var goal = Math.Max(1, achievement.GoalRule(state));
        return achievement.ProgressRule(state) >= goal;
    }

    private static int ComputeXp(GamificationCollectionState state, IReadOnlySet<string> unlockedAchievementIds, int bonusXp)
    {
        var achievementXp = AchievementDefinitions.All
            .Where(achievement => unlockedAchievementIds.Contains(achievement.Id))
            .Sum(achievement => achievement.Xp);

        // Duplicate copies earn no XP of their own - only the dupes-25 achievement counts them.
        return achievementXp
            + state.DistinctOwnedCards * XpNewCard
            + state.VerifiedReviewCount * XpConfirmedReview
            + bonusXp;
    }

    /// <summary>Inserts an AchievementUnlock row for every achievement newly found at/above its goal. A composite (CollectionId, AchievementId) key that already exists is left untouched, which is what makes unlocking the same achievement twice a no-op.</summary>
    private async Task<(Dictionary<string, DateTime> UnlockDates, IReadOnlyList<Achievement> NewlyUnlocked)> SyncUnlocksAsync(
        string collectionId, GamificationCollectionState state, CancellationToken cancellationToken)
    {
        var unlockDates = await dbContext.AchievementUnlocks
            .AsNoTracking()
            .Where(unlock => unlock.CollectionId == collectionId)
            .ToDictionaryAsync(unlock => unlock.AchievementId, unlock => unlock.UnlockedAtUtc, StringComparer.Ordinal, cancellationToken);

        var newlyUnlocked = new List<Achievement>();
        var now = DateTime.UtcNow;

        foreach (var achievement in AchievementDefinitions.All)
        {
            if (unlockDates.ContainsKey(achievement.Id) || !IsCurrentlyUnlocked(achievement, state))
            {
                continue;
            }

            dbContext.AchievementUnlocks.Add(new AchievementUnlock
            {
                CollectionId = collectionId,
                AchievementId = achievement.Id,
                UnlockedAtUtc = now
            });
            unlockDates[achievement.Id] = now;
            newlyUnlocked.Add(achievement);
        }

        if (newlyUnlocked.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return (unlockDates, newlyUnlocked);
    }

    private async Task<int> GetBonusXpAsync(string collectionId, CancellationToken cancellationToken)
    {
        return await dbContext.GamificationProfiles
            .AsNoTracking()
            .Where(profile => profile.CollectionId == collectionId)
            .Select(profile => profile.BonusXp)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<GamificationCollectionState> BuildStateAsync(CancellationToken cancellationToken)
    {
        var cardsFromCatalog = await catalogServiceClient.ListCatalogCardsAsync(cancellationToken);
        var photoEntries = await pictureServiceClient.ListCardEntriesAsync(cancellationToken);
        return BuildState(
            cardsFromCatalog,
            photoEntries.Select(entry => new PhotoFacts(entry.SetName, entry.CardNumber, entry.Rarity, entry.ReviewStatus)).ToArray());
    }

    private static GamificationCollectionState BuildState(ReviewSnapshot snapshot)
    {
        return BuildState(
            snapshot.Catalog,
            snapshot.Photos.Select(photo => new PhotoFacts(photo.SetName, photo.CardNumber, photo.Rarity, photo.ReviewStatus)).ToArray());
    }

    /// <summary>The only photo fields gamification reads, shared by gRPC CardEntry and in-memory CardListItem sources.</summary>
    private sealed record PhotoFacts(string? SetName, string? CardNumber, string? Rarity, string? ReviewStatus);

    private static GamificationCollectionState BuildState(
        IReadOnlyList<(string Series, string Category, string CardNumber, string CardName, int SortOrder)> cardsFromCatalog,
        IReadOnlyList<PhotoFacts> photoEntries)
    {
        var photosByKey = photoEntries.ToLookup(entry => CollectionQueryService.BuildOwnershipKey(entry.SetName, entry.CardNumber));

        var distinctOwnedCards = 0;
        var duplicateCopies = 0;
        var legendaryOwnedCards = 0;
        var limitedEditionOwnedCards = 0;
        var seriesWithAtLeastOneOwnedCard = 0;
        var anySeriesComplete = false;
        var seriesCount = 0;

        foreach (var seriesGroup in cardsFromCatalog.GroupBy(card => card.Series, StringComparer.Ordinal))
        {
            seriesCount++;
            var ownedInSeries = 0;
            var totalInSeries = 0;

            foreach (var card in seriesGroup)
            {
                totalInSeries++;

                var key = CollectionQueryService.BuildOwnershipKey(card.Series, card.CardNumber);
                var matches = string.IsNullOrWhiteSpace(key) ? Array.Empty<PhotoFacts>() : photosByKey[key].ToArray();
                if (matches.Length == 0)
                {
                    continue;
                }

                ownedInSeries++;
                distinctOwnedCards++;
                duplicateCopies += matches.Length - 1;

                if (matches.Any(entry => IsRarity(entry.Rarity, "legendary")))
                {
                    legendaryOwnedCards++;
                }

                if (matches.Any(entry => IsRarity(entry.Rarity, "limited edition")))
                {
                    limitedEditionOwnedCards++;
                }
            }

            if (ownedInSeries > 0)
            {
                seriesWithAtLeastOneOwnedCard++;
            }

            if (totalInSeries > 0 && ownedInSeries == totalInSeries)
            {
                anySeriesComplete = true;
            }
        }

        var verifiedReviewCount = photoEntries.Count(entry =>
            string.Equals(NormalizeNullable(entry.ReviewStatus) ?? ReviewStatuses.Unreviewed, ReviewStatuses.Verified, StringComparison.OrdinalIgnoreCase));

        return new GamificationCollectionState
        {
            TotalPhotos = photoEntries.Count,
            DistinctOwnedCards = distinctOwnedCards,
            DuplicateCopies = duplicateCopies,
            SeriesWithAtLeastOneOwnedCard = seriesWithAtLeastOneOwnedCard,
            TotalSeries = seriesCount,
            AnySeriesComplete = anySeriesComplete,
            VerifiedReviewCount = verifiedReviewCount,
            LegendaryOwnedCards = legendaryOwnedCards,
            LimitedEditionOwnedCards = limitedEditionOwnedCards
        };
    }

    private static bool IsRarity(string? rarity, string expected)
    {
        return !string.IsNullOrWhiteSpace(rarity) && string.Equals(rarity.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private async Task<string> GetCollectionIdAsync(CancellationToken cancellationToken)
    {
        if (cachedCollectionId is not null)
        {
            return cachedCollectionId;
        }

        var authState = await authenticationStateProvider.GetAuthenticationStateAsync();
        var collection = await currentCollectionContext.GetOwnedCollectionAsync(authState.User, cancellationToken);
        if (collection is null)
        {
            throw new InvalidOperationException("Der aktuelle Benutzer besitzt keine Sammlung.");
        }

        cachedCollectionId = collection.Id;
        return cachedCollectionId;
    }
}
