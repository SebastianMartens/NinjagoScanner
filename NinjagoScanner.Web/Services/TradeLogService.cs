using Microsoft.EntityFrameworkCore;
using NinjagoScanner.Web.Data;

namespace NinjagoScanner.Web.Services;

/// <summary>One row of the /trade/log page, from the viewing user's perspective.</summary>
public sealed record TradeLogRow(
    string TradeId,
    string PartnerUserName,
    DateTime DateUtc,
    TradeStatus Outcome,
    IReadOnlyList<TradeCardSummary> Given,
    IReadOnlyList<TradeCardSummary> Received);

/// <summary>
/// Read-only trade history (web-trade-log). Completed trades come from the immutable log entry
/// snapshot (usernames and cards as they were), declined/cancelled/failed trades from the trade
/// itself. Only trades the user took part in are returned; there are no write operations.
/// </summary>
internal sealed class TradeLogService(AppDbContext dbContext)
{
    public async Task<IReadOnlyList<TradeLogRow>> ListAsync(string userId, CancellationToken cancellationToken = default)
    {
        var rows = new List<TradeLogRow>();

        var entries = await dbContext.TradeLogEntries
            .AsNoTracking()
            .Include(e => e.Items)
            .Where(e => e.ProposerUserId == userId || e.RecipientUserId == userId)
            .ToListAsync(cancellationToken);
        foreach (var entry in entries)
        {
            var isProposer = entry.ProposerUserId == userId;
            var givenSide = isProposer ? TradeSide.FromProposer : TradeSide.FromRecipient;
            rows.Add(new TradeLogRow(
                entry.TradeId,
                isProposer ? entry.RecipientUserName : entry.ProposerUserName,
                DateTime.SpecifyKind(entry.CompletedAtUtc, DateTimeKind.Utc),
                TradeStatus.Completed,
                entry.Items.Where(i => i.Side == givenSide).Select(i => new TradeCardSummary(i.SeriesName, i.CardNumber, i.CardName, i.Rarity)).ToList(),
                entry.Items.Where(i => i.Side != givenSide).Select(i => new TradeCardSummary(i.SeriesName, i.CardNumber, i.CardName, i.Rarity)).ToList()));
        }

        var loggedIds = entries.Select(e => e.TradeId).ToHashSet();
        var others = await dbContext.Trades
            .AsNoTracking()
            .Include(t => t.Items)
            .Where(t => (t.ProposerUserId == userId || t.RecipientUserId == userId)
                && (t.Status == TradeStatus.Declined || t.Status == TradeStatus.Cancelled || t.Status == TradeStatus.Failed))
            .ToListAsync(cancellationToken);
        others = others.Where(t => !loggedIds.Contains(t.Id)).ToList();

        var partnerIds = others
            .Select(t => t.ProposerUserId == userId ? t.RecipientUserId : t.ProposerUserId)
            .Distinct()
            .ToList();
        var names = await dbContext.Users
            .Where(u => partnerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName })
            .ToDictionaryAsync(u => u.Id, u => u.UserName ?? string.Empty, cancellationToken);
        foreach (var trade in others)
        {
            var isProposer = trade.ProposerUserId == userId;
            var partnerId = isProposer ? trade.RecipientUserId : trade.ProposerUserId;
            var givenSide = isProposer ? TradeSide.FromProposer : TradeSide.FromRecipient;
            var snapshotName = isProposer ? trade.RecipientUserName : trade.ProposerUserName;
            rows.Add(new TradeLogRow(
                trade.Id,
                !string.IsNullOrEmpty(snapshotName) ? snapshotName : names.GetValueOrDefault(partnerId, string.Empty),
                (trade.ResolvedAt ?? trade.CreatedAt).UtcDateTime,
                trade.Status,
                trade.Items.Where(i => i.Side == givenSide).Select(i => new TradeCardSummary(i.SeriesName, i.CardNumber, i.CardName, i.Rarity)).ToList(),
                trade.Items.Where(i => i.Side != givenSide).Select(i => new TradeCardSummary(i.SeriesName, i.CardNumber, i.CardName, i.Rarity)).ToList()));
        }

        return rows.OrderByDescending(r => r.DateUtc).ToList();
    }
}
