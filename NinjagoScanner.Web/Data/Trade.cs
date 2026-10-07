namespace NinjagoScanner.Web.Data;

public enum TradeStatus
{
    Pending,
    Executing,
    Completed,
    Declined,
    Cancelled,
    Failed
}

public enum TradeSide
{
    FromProposer,
    FromRecipient
}

public class Trade
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string ProposerCollectionId { get; set; } = string.Empty;
    public string RecipientCollectionId { get; set; } = string.Empty;
    public string ProposerUserId { get; set; } = string.Empty;
    public string RecipientUserId { get; set; } = string.Empty;
    /// <summary>User names as they were at proposal time, so the log survives account deletion.</summary>
    public string ProposerUserName { get; set; } = string.Empty;
    public string RecipientUserName { get; set; } = string.Empty;
    public TradeStatus Status { get; set; } = TradeStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExecutingStartedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>Optimistic concurrency token; regenerate on every status change.</summary>
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("n");

    public List<TradeItem> Items { get; set; } = [];
}

/// <summary>A card offered in a trade, snapshotted at proposal time.</summary>
public class TradeItem
{
    public int Id { get; set; }
    public string TradeId { get; set; } = string.Empty;
    public TradeSide Side { get; set; }
    public string PhotoId { get; set; } = string.Empty;

    /// <summary>
    /// True while the owning trade is open (Pending/Executing). A filtered unique index on
    /// (PhotoId) WHERE Reserved makes the database refuse a second open reservation of a photo,
    /// whichever machine proposes it. Cleared when the trade reaches a terminal status.
    /// </summary>
    public bool Reserved { get; set; }
    public string SeriesName { get; set; } = string.Empty;
    public string CardNumber { get; set; } = string.Empty;
    public string CardName { get; set; } = string.Empty;
    public string Rarity { get; set; } = string.Empty;
}

/// <summary>Immutable record of a completed trade; no update/delete paths exist.</summary>
public class TradeLogEntry
{
    public int Id { get; set; }
    public string TradeId { get; set; } = string.Empty;
    public DateTime CompletedAtUtc { get; set; }
    public string ProposerUserId { get; set; } = string.Empty;
    public string RecipientUserId { get; set; } = string.Empty;
    public string ProposerUserName { get; set; } = string.Empty;
    public string RecipientUserName { get; set; } = string.Empty;
    public List<TradeLogItem> Items { get; set; } = [];
}

public class TradeLogItem
{
    public int Id { get; set; }
    public int TradeLogEntryId { get; set; }
    public TradeSide Side { get; set; }
    public string SeriesName { get; set; } = string.Empty;
    public string CardNumber { get; set; } = string.Empty;
    public string CardName { get; set; } = string.Empty;
    public string Rarity { get; set; } = string.Empty;
}
