using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NinjagoScanner.PictureService.Protos;
using NinjagoScanner.Web.Data;

namespace NinjagoScanner.Web.Services;

public enum TradeFailure
{
    None,
    NotFound,
    NotAllowed,
    NotFriends,
    /// <summary>The recipient's collection visibility is "Nur ich", so it cannot be browsed or traded with.</summary>
    CollectionPrivate,
    InvalidSelection,
    UnequalCounts,
    NotTradable,
    AlreadyReserved,
    NotPending,
    Stale,
    TransferFailed,
    Unavailable
}

/// <param name="Status">The trade's status after the call, when a trade exists.</param>
public sealed record TradeActionResult(
    bool Success,
    TradeFailure Failure,
    string Message,
    string? TradeId = null,
    TradeStatus? Status = null)
{
    internal static TradeActionResult Ok(string message, string tradeId, TradeStatus status) =>
        new(true, TradeFailure.None, message, tradeId, status);

    internal static TradeActionResult Fail(TradeFailure failure, string message, string? tradeId = null, TradeStatus? status = null) =>
        new(false, failure, message, tradeId, status);
}

public sealed record TradeCardSummary(string SeriesName, string CardNumber, string CardName, string Rarity);

/// <summary>One trade from the viewing user's perspective (for history / log pages).</summary>
public sealed record TradeSummary(
    string TradeId,
    string PartnerUserName,
    bool IsProposer,
    TradeStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    IReadOnlyList<TradeCardSummary> Given,
    IReadOnlyList<TradeCardSummary> Received);

/// <summary>
/// Trade lifecycle (web-trade-execution, web-trade-log): proposal with validation and
/// reservations, decline/cancel, accept with re-validation and one all-or-nothing
/// TransferPhotos call, the completion transaction (status + immutable log + bonus XP) and
/// recovery of stale Executing trades. All methods take the acting user's id, which callers must
/// derive from the authenticated principal, never from client input.
/// </summary>
internal sealed class TradeService(
    AppDbContext dbContext,
    CatalogServiceClient catalogServiceClient,
    PictureServiceClient pictureServiceClient)
{
    public const int TradeXp = 25;

    /// <summary>A trade left Executing for longer than this is picked up by the recovery sweep.</summary>
    public static readonly TimeSpan StaleExecutingAfter = TimeSpan.FromMinutes(5);

    // Best-effort in-process serialization of proposals so concurrent surplus checks on one
    // machine do not interleave. It is NOT the double-reservation guard: that is the filtered
    // unique index on open TradeItem reservations, which holds across machines.
    private static readonly SemaphoreSlim ProposalGate = new(1, 1);

    // ---------------------------------------------------------------- propose

    public async Task<TradeActionResult> ProposeAsync(
        string proposerUserId,
        string recipientUserId,
        IReadOnlyCollection<string> offeredPhotoIds,
        IReadOnlyCollection<string> requestedPhotoIds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(proposerUserId) || string.IsNullOrEmpty(recipientUserId)
            || proposerUserId == recipientUserId)
        {
            return TradeActionResult.Fail(TradeFailure.NotFriends, "Mit dieser Person kann nicht getauscht werden.");
        }

        var offered = offeredPhotoIds.ToList();
        var requested = requestedPhotoIds.ToList();
        if (offered.Count == 0 || requested.Count == 0)
        {
            return TradeActionResult.Fail(TradeFailure.InvalidSelection, "Wähle auf beiden Seiten mindestens eine Karte aus.");
        }

        if (offered.Count != requested.Count)
        {
            return TradeActionResult.Fail(TradeFailure.UnequalCounts, "Beide Seiten müssen gleich viele Karten enthalten.");
        }

        if (offered.Distinct(StringComparer.Ordinal).Count() != offered.Count
            || requested.Distinct(StringComparer.Ordinal).Count() != requested.Count)
        {
            return TradeActionResult.Fail(TradeFailure.InvalidSelection, "Eine Karte kann nur einmal ausgewählt werden.");
        }

        if (!await AreFriendsAsync(proposerUserId, recipientUserId, cancellationToken))
        {
            return TradeActionResult.Fail(TradeFailure.NotFriends, "Du kannst nur mit Freunden tauschen.");
        }

        var proposerCollectionId = await GetOwnedCollectionIdAsync(proposerUserId, cancellationToken);
        var recipientCollectionId = await GetOwnedCollectionIdAsync(recipientUserId, cancellationToken);
        if (proposerCollectionId is null || recipientCollectionId is null)
        {
            return TradeActionResult.Fail(TradeFailure.NotFriends, "Mit dieser Person kann nicht getauscht werden.");
        }

        // Proposing means choosing from the recipient's collection (and snapshotting card name,
        // number and rarity into the trade), so it requires the same visibility as browsing it.
        // This is checked before any photo is validated so a private collection leaks nothing.
        // Deliberately NOT re-checked in AcceptAsync: the recipient going private afterwards must
        // not take away their own ability to accept/decline the incoming trade (the cards were
        // snapshotted before they went private and the proposer only sees what they already saw).
        var recipientVisibility = (await dbContext.CollectionSharingSettings
            .AsNoTracking()
            .Where(s => s.CollectionId == recipientCollectionId)
            .Select(s => (CollectionVisibility?)s.Visibility)
            .FirstOrDefaultAsync(cancellationToken)) ?? CollectionVisibility.Friends;
        if (recipientVisibility != CollectionVisibility.Friends)
        {
            return TradeActionResult.Fail(TradeFailure.CollectionPrivate, "Die Sammlung dieser Person ist privat. Mit ihr kann nicht getauscht werden.");
        }

        await ProposalGate.WaitAsync(cancellationToken);
        try
        {
            ValidatedSelection selection;
            try
            {
                selection = await ValidateSelectionAsync(
                    proposerCollectionId, recipientCollectionId, offered, requested, excludeTradeId: null, cancellationToken);
            }
            catch (RpcException)
            {
                return TradeActionResult.Fail(TradeFailure.Unavailable, "Die Sammlungen konnten gerade nicht geladen werden. Bitte versuche es später erneut.");
            }

            if (selection.Failure is { } failure)
            {
                return TradeActionResult.Fail(failure.Failure, failure.Message);
            }

            var proposalNames = await dbContext.Users
                .Where(u => u.Id == proposerUserId || u.Id == recipientUserId)
                .Select(u => new { u.Id, u.UserName })
                .ToDictionaryAsync(u => u.Id, u => u.UserName ?? string.Empty, cancellationToken);
            var trade = new Trade
            {
                ProposerUserName = proposalNames.GetValueOrDefault(proposerUserId, string.Empty),
                RecipientUserName = proposalNames.GetValueOrDefault(recipientUserId, string.Empty),
                ProposerUserId = proposerUserId,
                RecipientUserId = recipientUserId,
                ProposerCollectionId = proposerCollectionId,
                RecipientCollectionId = recipientCollectionId,
                Status = TradeStatus.Pending,
                Items = selection.Items
            };
            foreach (var item in trade.Items)
            {
                item.Reserved = true;
            }

            dbContext.Trades.Add(trade);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // The unique open-reservation index fired: another proposal (possibly on another
                // machine) reserved one of these photos between validation and insert.
                dbContext.ChangeTracker.Clear();
                return TradeActionResult.Fail(TradeFailure.AlreadyReserved, "Eine der Karten ist bereits in einem anderen offenen Tausch.");
            }

            return TradeActionResult.Ok("Der Tauschvorschlag wurde gesendet.", trade.Id, TradeStatus.Pending);
        }
        finally
        {
            ProposalGate.Release();
        }
    }

    // ---------------------------------------------------------- decline / cancel

    /// <summary>Only the recipient of a pending trade can decline it.</summary>
    public async Task<TradeActionResult> DeclineAsync(string userId, string tradeId, CancellationToken cancellationToken = default)
    {
        var trade = await LoadTradeAsync(tradeId, cancellationToken);
        if (trade is null)
        {
            return TradeActionResult.Fail(TradeFailure.NotFound, "Der Tausch wurde nicht gefunden.");
        }

        if (trade.RecipientUserId != userId)
        {
            return TradeActionResult.Fail(TradeFailure.NotAllowed, "Du kannst diesen Tausch nicht ablehnen.", tradeId, trade.Status);
        }

        return await CloseAsync(trade, TradeStatus.Declined, "Der Tausch wurde abgelehnt.", cancellationToken);
    }

    /// <summary>Only the proposer of a pending trade can cancel it.</summary>
    public async Task<TradeActionResult> CancelAsync(string userId, string tradeId, CancellationToken cancellationToken = default)
    {
        var trade = await LoadTradeAsync(tradeId, cancellationToken);
        if (trade is null)
        {
            return TradeActionResult.Fail(TradeFailure.NotFound, "Der Tausch wurde nicht gefunden.");
        }

        if (trade.ProposerUserId != userId)
        {
            return TradeActionResult.Fail(TradeFailure.NotAllowed, "Du kannst diesen Tausch nicht zurückziehen.", tradeId, trade.Status);
        }

        return await CloseAsync(trade, TradeStatus.Cancelled, "Der Tauschvorschlag wurde zurückgezogen.", cancellationToken);
    }

    private async Task<TradeActionResult> CloseAsync(
        Trade trade, TradeStatus target, string successMessage, CancellationToken cancellationToken)
    {
        var moved = await TransitionAsync(trade.Id, TradeStatus.Pending, target, cancellationToken);
        if (!moved)
        {
            return TradeActionResult.Fail(
                TradeFailure.NotPending, "Dieser Tausch wurde bereits bearbeitet.", trade.Id,
                await GetStatusAsync(trade.Id, cancellationToken));
        }

        return TradeActionResult.Ok(successMessage, trade.Id, target);
    }

    // ------------------------------------------------------------------ accept

    /// <summary>
    /// Only the recipient can accept. Re-validates every card against fresh PictureService data,
    /// flips Pending to Executing atomically (so a double accept moves once), performs one
    /// TransferPhotos call and completes the trade in one transaction.
    /// </summary>
    public async Task<TradeActionResult> AcceptAsync(string userId, string tradeId, CancellationToken cancellationToken = default)
    {
        var trade = await LoadTradeAsync(tradeId, cancellationToken);
        if (trade is null)
        {
            return TradeActionResult.Fail(TradeFailure.NotFound, "Der Tausch wurde nicht gefunden.");
        }

        if (trade.RecipientUserId != userId)
        {
            return TradeActionResult.Fail(TradeFailure.NotAllowed, "Du kannst diesen Tausch nicht annehmen.", tradeId, trade.Status);
        }

        if (trade.Status != TradeStatus.Pending)
        {
            return TradeActionResult.Fail(TradeFailure.NotPending, "Dieser Tausch wurde bereits bearbeitet.", tradeId, trade.Status);
        }

        // Re-validate with fresh data. A transient read failure leaves the trade pending.
        string? staleMessage = null;
        if (!await AreFriendsAsync(trade.ProposerUserId, trade.RecipientUserId, cancellationToken))
        {
            staleMessage = "Ihr seid nicht mehr befreundet.";
        }
        else
        {
            try
            {
                var selection = await ValidateSelectionAsync(
                    trade.ProposerCollectionId,
                    trade.RecipientCollectionId,
                    trade.Items.Where(i => i.Side == TradeSide.FromProposer).Select(i => i.PhotoId).ToList(),
                    trade.Items.Where(i => i.Side == TradeSide.FromRecipient).Select(i => i.PhotoId).ToList(),
                    excludeTradeId: trade.Id,
                    cancellationToken);
                staleMessage = selection.Failure?.Message;
                if (staleMessage is null)
                {
                    // The card a photo shows must still be the one the proposal snapshotted.
                    var snapshots = trade.Items.ToDictionary(i => i.PhotoId, StringComparer.Ordinal);
                    var changed = selection.Items.Any(fresh =>
                        !snapshots.TryGetValue(fresh.PhotoId, out var snap)
                        || !string.Equals(snap.SeriesName, fresh.SeriesName, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(snap.CardNumber, fresh.CardNumber, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(snap.CardName, fresh.CardName, StringComparison.Ordinal)
                        || !string.Equals(snap.Rarity, fresh.Rarity, StringComparison.Ordinal));
                    if (changed)
                    {
                        staleMessage = "Eine der Karten wurde nach dem Angebot geändert.";
                    }
                }
            }
            catch (RpcException)
            {
                return TradeActionResult.Fail(
                    TradeFailure.Unavailable,
                    "Die Sammlungen konnten gerade nicht geladen werden. Der Tausch bleibt offen, bitte versuche es später erneut.",
                    tradeId, TradeStatus.Pending);
            }
        }

        if (staleMessage is not null)
        {
            if (!await TransitionAsync(trade.Id, TradeStatus.Pending, TradeStatus.Failed, cancellationToken))
            {
                return TradeActionResult.Fail(
                    TradeFailure.NotPending, "Dieser Tausch wurde bereits bearbeitet.", tradeId,
                    await GetStatusAsync(tradeId, cancellationToken));
            }

            return TradeActionResult.Fail(
                TradeFailure.Stale,
                $"Der Tausch ist nicht mehr gültig und wurde abgebrochen. {staleMessage}",
                tradeId, TradeStatus.Failed);
        }

        // Optimistic flip: exactly one of several concurrent accepts wins.
        var now = DateTimeOffset.UtcNow;
        var flipped = await dbContext.Trades
            .Where(t => t.Id == trade.Id && t.Status == TradeStatus.Pending && t.ConcurrencyStamp == trade.ConcurrencyStamp)
            .ExecuteUpdateAsync(set => set
                .SetProperty(t => t.Status, TradeStatus.Executing)
                .SetProperty(t => t.ExecutingStartedAt, now)
                .SetProperty(t => t.ConcurrencyStamp, Guid.NewGuid().ToString("n")),
                cancellationToken);
        if (flipped == 0)
        {
            return TradeActionResult.Fail(
                TradeFailure.NotPending, "Dieser Tausch wurde bereits bearbeitet.", tradeId,
                await GetStatusAsync(tradeId, cancellationToken));
        }

        return await ExecuteAsync(trade, recovering: false, cancellationToken);
    }

    /// <summary>Runs the transfer for a trade already flipped to Executing, then finalizes it.</summary>
    private async Task<TradeActionResult> ExecuteAsync(Trade trade, bool recovering, CancellationToken cancellationToken)
    {
        try
        {
            await pictureServiceClient.TransferPhotosAsync(trade.Id, BuildMoves(trade), cancellationToken);
        }
        catch (RpcException exception)
        {
            // Only definitive failures (PictureService rolled back, or rejected the request) fail
            // the trade. Anything else (Aborted "transfer busy", Unavailable, Unknown, timeouts,
            // unclassified codes) may have committed or be in progress elsewhere: stay Executing.
            var definitive = exception.StatusCode is StatusCode.Internal or StatusCode.NotFound
                or StatusCode.InvalidArgument or StatusCode.AlreadyExists;
            if (!definitive)
            {
                // The recovery sweep retries with the same (idempotent) transfer id and then finalizes.
                return TradeActionResult.Fail(
                    TradeFailure.Unavailable,
                    "Der Tausch wird gerade ausgeführt. Bitte prüfe es in Kürze erneut.",
                    trade.Id, TradeStatus.Executing);
            }

            // PictureService is all-or-nothing and compensates, so both collections are unchanged.
            await TransitionAsync(trade.Id, TradeStatus.Executing, TradeStatus.Failed, CancellationToken.None);
            return TradeActionResult.Fail(
                TradeFailure.TransferFailed,
                "Der Tausch konnte nicht ausgeführt werden. Es wurden keine Karten verschoben.",
                trade.Id, TradeStatus.Failed);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return TradeActionResult.Fail(
                TradeFailure.Unavailable,
                "Der Tausch wird gerade ausgeführt. Bitte prüfe es in Kürze erneut.",
                trade.Id, TradeStatus.Executing);
        }

        return await CompleteAsync(trade, cancellationToken);
    }

    private static IEnumerable<(string SourceCollectionId, string DestCollectionId, string PhotoId)> BuildMoves(Trade trade) =>
        trade.Items.Select(item => item.Side == TradeSide.FromProposer
            ? (trade.ProposerCollectionId, trade.RecipientCollectionId, item.PhotoId)
            : (trade.RecipientCollectionId, trade.ProposerCollectionId, item.PhotoId));

    /// <summary>
    /// ONE transaction: Executing to Completed (guarded, so a replay never repeats it), the
    /// immutable log entry with its items and +25 bonus XP for both collections.
    /// </summary>
    private async Task<TradeActionResult> CompleteAsync(Trade trade, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var completed = await dbContext.Trades
            .Where(t => t.Id == trade.Id && t.Status == TradeStatus.Executing)
            .ExecuteUpdateAsync(set => set
                .SetProperty(t => t.Status, TradeStatus.Completed)
                .SetProperty(t => t.ResolvedAt, now)
                .SetProperty(t => t.ConcurrencyStamp, Guid.NewGuid().ToString("n")),
                CancellationToken.None);
        if (completed == 0)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return TradeActionResult.Fail(
                TradeFailure.NotPending, "Dieser Tausch wurde bereits bearbeitet.", trade.Id,
                await GetStatusAsync(trade.Id, CancellationToken.None));
        }

        await ReleaseReservationsAsync(dbContext, [trade.Id], CancellationToken.None);

        var names = await dbContext.Users
            .Where(u => u.Id == trade.ProposerUserId || u.Id == trade.RecipientUserId)
            .Select(u => new { u.Id, u.UserName })
            .ToDictionaryAsync(u => u.Id, u => u.UserName ?? string.Empty, CancellationToken.None);

        dbContext.TradeLogEntries.Add(new TradeLogEntry
        {
            TradeId = trade.Id,
            CompletedAtUtc = now.UtcDateTime,
            ProposerUserId = trade.ProposerUserId,
            RecipientUserId = trade.RecipientUserId,
            ProposerUserName = names.GetValueOrDefault(trade.ProposerUserId, string.Empty),
            RecipientUserName = names.GetValueOrDefault(trade.RecipientUserId, string.Empty),
            Items = trade.Items.Select(item => new TradeLogItem
            {
                Side = item.Side,
                SeriesName = item.SeriesName,
                CardNumber = item.CardNumber,
                CardName = item.CardName,
                Rarity = item.Rarity
            }).ToList()
        });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await AddBonusXpAsync(trade.ProposerCollectionId);
        await AddBonusXpAsync(trade.RecipientCollectionId);

        await transaction.CommitAsync(CancellationToken.None);
        return TradeActionResult.Ok("Der Tausch wurde abgeschlossen.", trade.Id, TradeStatus.Completed);
    }

    /// <summary>
    /// Atomic upsert-increment: insert-or-ignore the profile row, then increment in SQL. Two
    /// first-ever trades for one collection can no longer both take an "Add" path and hit the
    /// primary key; each increment is applied exactly once inside the caller's transaction.
    /// </summary>
    private async Task AddBonusXpAsync(string collectionId)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT OR IGNORE INTO GamificationProfiles (CollectionId, BonusXp) VALUES ({collectionId}, 0)",
            CancellationToken.None);
        await dbContext.GamificationProfiles
            .Where(p => p.CollectionId == collectionId)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.BonusXp, p => p.BonusXp + TradeXp), CancellationToken.None);
    }

    // ---------------------------------------------------------------- recovery

    /// <summary>
    /// Re-drives trades stuck in Executing for longer than <paramref name="olderThan"/> (default
    /// <see cref="StaleExecutingAfter"/>): repeats TransferPhotos with the same transfer id
    /// (idempotent) and finalizes. A definitive failure marks the trade Failed; a transient one
    /// leaves it for the next sweep. Returns how many trades were resolved (completed or failed).
    /// </summary>
    public async Task<int> RecoverStaleExecutingAsync(TimeSpan? olderThan = null, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - (olderThan ?? StaleExecutingAfter);
        var candidates = await dbContext.Trades
            .AsNoTracking()
            .Include(t => t.Items)
            .Where(t => t.Status == TradeStatus.Executing)
            .ToListAsync(cancellationToken);

        var resolved = 0;
        foreach (var trade in candidates.Where(t => (t.ExecutingStartedAt ?? t.CreatedAt) <= cutoff))
        {
            var result = await ExecuteAsync(trade, recovering: true, cancellationToken);
            if (result.Status is TradeStatus.Completed or TradeStatus.Failed)
            {
                resolved++;
            }
        }

        return resolved;
    }

    // ----------------------------------------------------------------- queries

    /// <summary>Pending trades awaiting the user's answer (nav badge).</summary>
    public Task<int> CountIncomingPendingAsync(string userId, CancellationToken cancellationToken = default) =>
        dbContext.Trades.CountAsync(t => t.RecipientUserId == userId && t.Status == TradeStatus.Pending, cancellationToken);

    /// <summary>The user's own trades (only ones they took part in), newest first, any outcome.</summary>
    public async Task<IReadOnlyList<TradeSummary>> ListTradesAsync(string userId, CancellationToken cancellationToken = default)
    {
        var trades = await dbContext.Trades
            .AsNoTracking()
            .Include(t => t.Items)
            .Where(t => t.ProposerUserId == userId || t.RecipientUserId == userId)
            .ToListAsync(cancellationToken);

        var partnerIds = trades
            .Select(t => t.ProposerUserId == userId ? t.RecipientUserId : t.ProposerUserId)
            .Distinct()
            .ToList();
        var names = await dbContext.Users
            .Where(u => partnerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName })
            .ToDictionaryAsync(u => u.Id, u => u.UserName ?? string.Empty, cancellationToken);
        var logs = await dbContext.TradeLogEntries
            .AsNoTracking()
            .Where(e => e.ProposerUserId == userId || e.RecipientUserId == userId)
            .ToDictionaryAsync(e => e.TradeId, e => e, cancellationToken);

        return trades
            .OrderByDescending(t => t.CreatedAt.UtcDateTime)
            .Select(t =>
            {
                var isProposer = t.ProposerUserId == userId;
                var partnerId = isProposer ? t.RecipientUserId : t.ProposerUserId;
                var givenSide = isProposer ? TradeSide.FromProposer : TradeSide.FromRecipient;
                static TradeCardSummary Card(TradeItem i) => new(i.SeriesName, i.CardNumber, i.CardName, i.Rarity);
                return new TradeSummary(
                    t.Id,
                    names.GetValueOrDefault(partnerId, string.Empty),
                    isProposer,
                    t.Status,
                    t.CreatedAt,
                    t.ResolvedAt,
                    t.Items.Where(i => i.Side == givenSide).Select(Card).ToList(),
                    t.Items.Where(i => i.Side != givenSide).Select(Card).ToList());
            })
            .ToList();
    }

    // ----------------------------------------------------------------- helpers

    private sealed record SelectionFailure(TradeFailure Failure, string Message);

    private sealed record ValidatedSelection(List<TradeItem> Items, SelectionFailure? Failure);

    /// <summary>
    /// Validates both sides against fresh PictureService data: every photo exists in its side's
    /// collection, is mapped to a catalog card, is not reserved by another open trade, and is a
    /// surplus copy (the owner keeps at least one copy of the card after all offered and already
    /// reserved copies leave).
    /// </summary>
    private async Task<ValidatedSelection> ValidateSelectionAsync(
        string proposerCollectionId,
        string recipientCollectionId,
        IReadOnlyList<string> offeredPhotoIds,
        IReadOnlyList<string> requestedPhotoIds,
        string? excludeTradeId,
        CancellationToken cancellationToken)
    {
        var catalog = (await catalogServiceClient.ListCatalogCardsAsync(cancellationToken))
            .GroupBy(card => CollectionQueryService.BuildOwnershipKey(card.Series, card.CardNumber), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrEmpty(group.Key))
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var proposerCards = await pictureServiceClient.ListCardEntriesForCollectionAsync(proposerCollectionId, cancellationToken);
        var recipientCards = await pictureServiceClient.ListCardEntriesForCollectionAsync(recipientCollectionId, cancellationToken);

        var reservedRows = await dbContext.TradeItems
            .AsNoTracking()
            .Join(dbContext.Trades, item => item.TradeId, trade => trade.Id, (item, trade) => new { item, trade })
            .Where(x => (x.trade.Status == TradeStatus.Pending || x.trade.Status == TradeStatus.Executing)
                        && x.trade.Id != excludeTradeId)
            .Select(x => x.item.PhotoId)
            .ToListAsync(cancellationToken);
        var reserved = reservedRows.ToHashSet(StringComparer.Ordinal);

        var items = new List<TradeItem>();
        foreach (var (side, ids, cards) in new[]
                 {
                     (TradeSide.FromProposer, offeredPhotoIds, proposerCards),
                     (TradeSide.FromRecipient, requestedPhotoIds, recipientCards)
                 })
        {
            var byId = cards.ToDictionary(card => card.PhotoId, StringComparer.Ordinal);
            // Photos marked "incorrect" cannot be vouched for: they are neither tradable nor surplus.
            var copiesPerKey = cards
                .Where(card => !IsIncorrect(card.ReviewStatus))
                .Select(card => CollectionQueryService.BuildOwnershipKey(card.SetName, card.CardNumber))
                .Where(key => !string.IsNullOrEmpty(key) && catalog.ContainsKey(key))
                .GroupBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            var reservedPerKey = cards
                .Where(card => reserved.Contains(card.PhotoId))
                .Select(card => CollectionQueryService.BuildOwnershipKey(card.SetName, card.CardNumber))
                .Where(key => !string.IsNullOrEmpty(key))
                .GroupBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            var offeredPerKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var photoId in ids)
            {
                if (reserved.Contains(photoId))
                {
                    return Rejected(TradeFailure.AlreadyReserved, "Eine der Karten ist bereits in einem anderen offenen Tausch.");
                }

                if (!byId.TryGetValue(photoId, out var entry))
                {
                    return Rejected(TradeFailure.NotTradable, "Eine der Karten ist nicht mehr in der Sammlung vorhanden.");
                }

                if (IsIncorrect(entry.ReviewStatus))
                {
                    return Rejected(TradeFailure.NotTradable, "Als fehlerhaft markierte Fotos können nicht getauscht werden.");
                }

                var key = CollectionQueryService.BuildOwnershipKey(entry.SetName, entry.CardNumber);
                if (string.IsNullOrEmpty(key) || !catalog.TryGetValue(key, out var catalogCard))
                {
                    return Rejected(TradeFailure.NotTradable, "Nicht zugeordnete Fotos können nicht getauscht werden.");
                }

                offeredPerKey.TryGetValue(key, out var alreadyOffered);
                offeredPerKey[key] = alreadyOffered + 1;
                reservedPerKey.TryGetValue(key, out var reservedCopies);
                if (copiesPerKey.GetValueOrDefault(key) - reservedCopies - offeredPerKey[key] < 1)
                {
                    return Rejected(
                        TradeFailure.NotTradable,
                        $"„{catalogCard.CardName}“ ist keine Dublette: Die letzte Kopie einer Karte kann nicht getauscht werden.");
                }

                items.Add(new TradeItem
                {
                    Side = side,
                    PhotoId = photoId,
                    SeriesName = catalogCard.Series,
                    CardNumber = catalogCard.CardNumber,
                    CardName = catalogCard.CardName,
                    Rarity = catalogCard.Rarity
                });
            }
        }

        return new ValidatedSelection(items, null);

        static ValidatedSelection Rejected(TradeFailure failure, string message) =>
            new([], new SelectionFailure(failure, message));
    }

    private static bool IsIncorrect(string? reviewStatus) =>
        string.Equals(reviewStatus, Models.ReviewStatuses.Incorrect, StringComparison.OrdinalIgnoreCase);

    private async Task<bool> AreFriendsAsync(string userA, string userB, CancellationToken cancellationToken)
    {
        var (low, high) = Friendship.CanonicalPair(userA, userB);
        return await dbContext.Friendships.AnyAsync(f =>
            f.UserLowId == low && f.UserHighId == high && f.Status == FriendshipStatus.Accepted,
            cancellationToken);
    }

    private Task<string?> GetOwnedCollectionIdAsync(string userId, CancellationToken cancellationToken) =>
        dbContext.CollectionMemberships
            .Where(m => m.UserId == userId && m.Role == CollectionRole.Owner)
            .Select(m => (string?)m.CollectionId)
            .FirstOrDefaultAsync(cancellationToken);

    private Task<Trade?> LoadTradeAsync(string tradeId, CancellationToken cancellationToken) =>
        dbContext.Trades.AsNoTracking().Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == tradeId, cancellationToken);

    private async Task<TradeStatus?> GetStatusAsync(string tradeId, CancellationToken cancellationToken) =>
        await dbContext.Trades.AsNoTracking().Where(t => t.Id == tradeId)
            .Select(t => (TradeStatus?)t.Status).FirstOrDefaultAsync(cancellationToken);

    /// <summary>Atomic compare-and-set of the trade status; false when the trade was not in <paramref name="from"/>.</summary>
    private async Task<bool> TransitionAsync(string tradeId, TradeStatus from, TradeStatus to, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var ownsTransaction = dbContext.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var rows = await dbContext.Trades
            .Where(t => t.Id == tradeId && t.Status == from)
            .ExecuteUpdateAsync(set => set
                .SetProperty(t => t.Status, to)
                .SetProperty(t => t.ResolvedAt, now)
                .SetProperty(t => t.ConcurrencyStamp, Guid.NewGuid().ToString("n")),
                cancellationToken);
        if (rows > 0)
        {
            await ReleaseReservationsAsync(dbContext, [tradeId], cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return rows > 0;
    }

    /// <summary>
    /// Clears the open-reservation flag of every item whose trade is no longer Pending/Executing,
    /// freeing the photos for new proposals. Call after moving a trade to a terminal status, in
    /// the same transaction.
    /// </summary>
    internal static Task<int> ReleaseReservationsAsync(
        AppDbContext db, IReadOnlyCollection<string> tradeIds, CancellationToken cancellationToken) =>
        db.TradeItems
            .Where(i => i.Reserved && tradeIds.Contains(i.TradeId)
                        && !db.Trades.Any(t => t.Id == i.TradeId
                                               && (t.Status == TradeStatus.Pending || t.Status == TradeStatus.Executing)))
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.Reserved, false), cancellationToken);
}
