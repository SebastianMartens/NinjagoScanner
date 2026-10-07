namespace NinjagoScanner.Web.Services;

/// <summary>
/// The editable trade a user composes on /trade (web-trade-finder, "Manual adjustment"): a list of
/// give/receive pairs, so counts are always equal. Cards can be removed, swapped for other
/// tradable/wanted cards, or a further pair added; the balance is always recomputed from the pairs.
/// </summary>
public sealed class TradeProposalDraft
{
    private readonly List<TradePair> pairs;
    private readonly IReadOnlyList<TradeCardOffer> giveCandidates;
    private readonly IReadOnlyList<TradeCardOffer> receiveCandidates;

    public TradeProposalDraft(
        TradeSuggestion initial,
        IReadOnlyList<TradeCardOffer> giveCandidates,
        IReadOnlyList<TradeCardOffer> receiveCandidates)
    {
        pairs = [.. initial.Pairs];
        this.giveCandidates = giveCandidates;
        this.receiveCandidates = receiveCandidates;
    }

    public IReadOnlyList<TradePair> Pairs => pairs;

    /// <summary>True once the user removed, swapped or added a pair by hand.</summary>
    public bool IsEdited { get; private set; }

    /// <summary>
    /// True when freshly loaded data no longer supports this draft: an untouched draft differs from the
    /// recomputed suggestion, or an edited draft uses a card that is no longer offerable.
    /// </summary>
    public bool IsOutdatedAgainst(TradeSuggestion fresh, IReadOnlyList<TradeCardOffer> freshGive, IReadOnlyList<TradeCardOffer> freshReceive)
    {
        if (!IsEdited)
        {
            return !pairs.Select(p => (p.Give.PhotoId, p.Receive.PhotoId))
                .SequenceEqual(fresh.Pairs.Select(p => (p.Give.PhotoId, p.Receive.PhotoId)));
        }

        var give = freshGive.Select(c => c.PhotoId).ToHashSet(StringComparer.Ordinal);
        var receive = freshReceive.Select(c => c.PhotoId).ToHashSet(StringComparer.Ordinal);
        return pairs.Any(p => !give.Contains(p.Give.PhotoId) || !receive.Contains(p.Receive.PhotoId));
    }

    public TradeBalance Balance => TradeMatchingService.Summarize(pairs).Balance;

    public IReadOnlyList<string> GivePhotoIds => pairs.Select(p => p.Give.PhotoId).ToList();

    public IReadOnlyList<string> ReceivePhotoIds => pairs.Select(p => p.Receive.PhotoId).ToList();

    /// <summary>Cards of mine still available to swap in (not already in a pair).</summary>
    public IReadOnlyList<TradeCardOffer> UnusedGive =>
        giveCandidates.Where(c => pairs.All(p => p.Give.Key != c.Key)).ToList();

    /// <summary>Friend cards still available to swap in (not already in a pair).</summary>
    public IReadOnlyList<TradeCardOffer> UnusedReceive =>
        receiveCandidates.Where(c => pairs.All(p => p.Receive.Key != c.Key)).ToList();

    public bool CanAddPair => UnusedGive.Count > 0 && UnusedReceive.Count > 0;

    public void RemovePair(int index)
    {
        if (index >= 0 && index < pairs.Count)
        {
            pairs.RemoveAt(index);
            IsEdited = true;
        }
    }

    /// <summary>Replaces the card I give in pair <paramref name="index"/>; false if not an unused candidate.</summary>
    public bool SwapGive(int index, string photoId)
    {
        var candidate = UnusedGive.FirstOrDefault(c => c.PhotoId == photoId);
        if (index < 0 || index >= pairs.Count || candidate is null)
        {
            return false;
        }

        pairs[index] = TradeMatchingService.MakePair(candidate, pairs[index].Receive);
        IsEdited = true;
        return true;
    }

    /// <summary>Replaces the card I receive in pair <paramref name="index"/>; false if not an unused candidate.</summary>
    public bool SwapReceive(int index, string photoId)
    {
        var candidate = UnusedReceive.FirstOrDefault(c => c.PhotoId == photoId);
        if (index < 0 || index >= pairs.Count || candidate is null)
        {
            return false;
        }

        pairs[index] = TradeMatchingService.MakePair(pairs[index].Give, candidate);
        IsEdited = true;
        return true;
    }

    /// <summary>Adds a pair of the first unused card on each side, matching rarity as closely as possible.</summary>
    public bool AddPair()
    {
        var give = UnusedGive;
        var receive = UnusedReceive;
        if (give.Count == 0 || receive.Count == 0)
        {
            return false;
        }

        var g = give[0];
        var r = receive
            .OrderBy(c => Math.Abs(TradeMatchingService.Weight(c.Rarity) - TradeMatchingService.Weight(g.Rarity)))
            .First();
        pairs.Add(TradeMatchingService.MakePair(g, r));
        IsEdited = true;
        return true;
    }
}
