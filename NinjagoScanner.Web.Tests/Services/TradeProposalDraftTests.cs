using NinjagoScanner.Web.Models;
using NinjagoScanner.Web.Services;

namespace NinjagoScanner.Web.Tests.Services;

/// <summary>web-trade-finder "Manual adjustment": deselect/swap/add with recomputed balance.</summary>
public class TradeProposalDraftTests
{
    private static TradeCardOffer Offer(string photo, string number, string rarity) =>
        new(photo, "Serie 2", number, $"Karte {number}", rarity);

    private static readonly TradeCardOffer[] Give =
    [
        Offer("g1", "1", CardRarity.Common), Offer("g2", "2", CardRarity.Common),
        Offer("g3", "3", CardRarity.Legendary)
    ];

    private static readonly TradeCardOffer[] Receive =
    [
        Offer("r1", "11", CardRarity.Common), Offer("r2", "12", CardRarity.Common),
        Offer("r3", "13", CardRarity.Limited)
    ];

    private static TradeProposalDraft Draft() =>
        new(TradeMatchingService.Pair([Give[0], Give[1]], [Receive[0], Receive[1]]), Give, Receive);

    [Fact]
    public void Initial_draft_is_balanced_with_equal_counts()
    {
        var draft = Draft();

        Assert.Equal(2, draft.Pairs.Count);
        Assert.True(draft.Balance.IsBalanced);
        Assert.Equal(draft.GivePhotoIds.Count, draft.ReceivePhotoIds.Count);
    }

    [Fact]
    public void Removing_a_pair_recomputes_the_balance_and_keeps_counts_equal()
    {
        var draft = Draft();

        draft.RemovePair(0);

        Assert.Single(draft.Pairs);
        Assert.Equal(1, draft.Balance.GiveCount);
        Assert.Equal(1, draft.Balance.ReceiveCount);
    }

    [Fact]
    public void Removing_the_last_pair_leaves_an_empty_draft()
    {
        var draft = Draft();
        draft.RemovePair(0);
        draft.RemovePair(0);

        Assert.Empty(draft.Pairs);
        Assert.Equal(0, draft.Balance.GiveCount);
        Assert.Equal("Noch keine Karten ausgewählt", TradeLabels.BalanceText(draft.Balance));
    }

    [Fact]
    public void Remove_with_invalid_index_is_ignored()
    {
        var draft = Draft();

        draft.RemovePair(7);
        draft.RemovePair(-1);

        Assert.Equal(2, draft.Pairs.Count);
    }

    [Fact]
    public void Swapping_in_a_legendary_card_flags_the_pair_and_updates_the_indicator()
    {
        var draft = Draft();

        Assert.True(draft.SwapGive(0, "g3"));

        Assert.True(draft.Pairs[0].Unausgewogen);
        Assert.False(draft.Balance.IsBalanced);
        Assert.Equal(1, draft.Balance.UnausgewogenCount);
        Assert.Equal(9 + 1, draft.Balance.GiveWeight);
        Assert.StartsWith("Unausgewogen", TradeLabels.BalanceText(draft.Balance));
    }

    [Fact]
    public void Swapping_the_receive_side_to_the_same_tier_restores_balance()
    {
        var draft = Draft();
        draft.SwapGive(0, "g3");

        // Only a limited card exists on the other side: still unbalanced, but weight shrinks.
        Assert.True(draft.SwapReceive(0, "r3"));

        Assert.True(draft.Pairs[0].Unausgewogen);
        Assert.Equal(9 - 3, draft.Pairs[0].WeightDifference);
    }

    [Fact]
    public void Swap_to_a_card_already_used_or_unknown_is_rejected()
    {
        var draft = Draft();

        Assert.False(draft.SwapGive(0, "g2"));
        Assert.False(draft.SwapGive(0, "nope"));
        Assert.False(draft.SwapReceive(1, "r1"));
        Assert.False(draft.SwapGive(5, "g3"));
        Assert.Equal(["g1", "g2"], draft.GivePhotoIds);
    }

    [Fact]
    public void Removed_cards_become_available_to_swap_in_again()
    {
        var draft = Draft();
        draft.RemovePair(1);

        Assert.Contains(draft.UnusedGive, c => c.PhotoId == "g2");
        Assert.True(draft.SwapGive(0, "g2"));
    }

    [Fact]
    public void Adding_a_pair_picks_the_closest_rarity_and_keeps_counts_equal()
    {
        var draft = new TradeProposalDraft(TradeSuggestion.Empty, Give, Receive);

        Assert.True(draft.AddPair());

        Assert.Equal(("g1", "r1"), (draft.GivePhotoIds[0], draft.ReceivePhotoIds[0]));
        Assert.True(draft.Balance.IsBalanced);
    }

    [Fact]
    public void Cannot_add_pair_when_one_side_is_exhausted()
    {
        var draft = new TradeProposalDraft(TradeSuggestion.Empty, Give, []);

        Assert.False(draft.CanAddPair);
        Assert.False(draft.AddPair());
    }

    [Theory]
    [InlineData(0, "Gleicher Wert")]
    [InlineData(6, "Du gibst 6 Wert mehr")]
    [InlineData(-2, "Du erhältst 2 Wert mehr")]
    public void Weight_difference_text_is_german(int difference, string expected) =>
        Assert.Equal(expected, TradeLabels.WeightText(difference));

    [Theory]
    [InlineData(Data.TradeStatus.Completed, "Abgeschlossen")]
    [InlineData(Data.TradeStatus.Declined, "Abgelehnt")]
    [InlineData(Data.TradeStatus.Cancelled, "Zurückgezogen")]
    [InlineData(Data.TradeStatus.Failed, "Fehlgeschlagen")]
    public void Outcome_text_is_german(Data.TradeStatus status, string expected) =>
        Assert.Equal(expected, TradeLabels.Outcome(status));

    [Fact]
    public void Untouched_draft_is_outdated_when_fresh_suggestion_differs()
    {
        var draft = Draft();
        var same = TradeMatchingService.Pair([Give[0], Give[1]], [Receive[0], Receive[1]]);
        var changed = TradeMatchingService.Pair([Give[0]], [Receive[0]]);

        Assert.False(draft.IsOutdatedAgainst(same, Give, Receive));
        Assert.True(draft.IsOutdatedAgainst(changed, Give, Receive));
    }

    [Fact]
    public void Edited_draft_is_outdated_only_when_a_used_card_is_gone()
    {
        var draft = Draft();
        draft.SwapReceive(0, "r3");
        var fresh = TradeMatchingService.Pair([Give[0]], [Receive[0]]);

        Assert.True(draft.IsEdited);
        Assert.False(draft.IsOutdatedAgainst(fresh, Give, Receive));
        Assert.True(draft.IsOutdatedAgainst(fresh, Give, [Receive[0], Receive[1]]));
    }
}
