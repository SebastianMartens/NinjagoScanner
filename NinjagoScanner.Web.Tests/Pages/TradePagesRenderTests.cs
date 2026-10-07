using System.Security.Claims;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using NinjagoScanner.Web.Components.Pages;
using TradePage = NinjagoScanner.Web.Components.Pages.Trade;
using NinjagoScanner.Web.Data;
using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Pages;

/// <summary>
/// Renders /trade and /trade/log through bUnit against the in-process catalog, the fake
/// PictureService and a migrated SQLite database (never real AWS).
/// </summary>
public class TradePagesRenderTests
{
    private sealed class FixedUserAuth(string userId) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))));
    }

    private static TradeFinderService CreateFinder(TradeTestEnv env)
    {
        var loader = new TradeInventoryLoader(env.CatalogClient, env.PictureClient);
        var access = new FriendAccessService(env.Db);
        return new TradeFinderService(
            env.Db, loader, env.PictureClient, access,
            new TradePartnerService(new FriendService(env.Db), access, loader, env.Db));
    }

    private static BunitContext NewContext(TradeTestEnv env, string userId)
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<AuthenticationStateProvider>(new FixedUserAuth(userId));
        ctx.Services.AddSingleton(CreateFinder(env));
        ctx.Services.AddSingleton(env.Service);
        ctx.Services.AddSingleton(new TradeLogService(env.Db));
        return ctx;
    }

    private static IRenderedComponent<TComponent> RenderLoaded<TComponent>(BunitContext ctx)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        var cut = ctx.Render<TComponent>();
        cut.WaitForAssertion(() => Assert.DoesNotContain("Lade ", cut.Markup), TimeSpan.FromSeconds(15));
        return cut;
    }

    /// <summary>Alice gets a second common #5 so she has a common duplicate Bob lacks (balanced against Jay).</summary>
    private static void AddBalancedCards(TradeTestEnv env) =>
        env.Pictures.WritePhoto("a5b", TradeTestEnv.Sidecar("Serie 2", "5", "Zane"), env.CollA);

    [Fact]
    public async Task Trade_page_ranks_partners_by_exchangeable_cards_and_marks_private_ones_unavailable()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        AddBalancedCards(env);
        var (dave, collDave) = await env.AddUserAsync("dave");
        await env.MakeFriendsAsync(env.A, dave);
        env.Pictures.WritePhoto("d1a", TradeTestEnv.Sidecar("Serie 10", "1", "Wu"), collDave);
        env.Pictures.WritePhoto("d1b", TradeTestEnv.Sidecar("Serie 10", "1", "Wu"), collDave);
        env.Pictures.WritePhoto("d6a", TradeTestEnv.Sidecar("Serie 2", "6", "Jay"), collDave);
        env.Pictures.WritePhoto("d6b", TradeTestEnv.Sidecar("Serie 2", "6", "Jay"), collDave);
        await env.MakeFriendsAsync(env.A, env.C);
        Assert.True(await new FriendAccessService(env.Db).SetOwnVisibilityAsync(env.C, CollectionVisibility.Private));

        using var ctx = NewContext(env, env.A);
        var cut = RenderLoaded<TradePage>(ctx);

        var names = cut.FindAll(".trade-partner-name").Select(e => e.TextContent.Trim()).ToList();
        Assert.Equal(["dave", "bob", "carol"], names);
        var carol = cut.FindAll(".trade-partner").Last();
        Assert.Contains("Nicht verfügbar", carol.TextContent);
        Assert.Empty(carol.QuerySelectorAll("button"));
        Assert.Matches(@"Du kannst\s+3\s+anbieten", cut.FindAll(".trade-partner").First().TextContent);
    }

    [Fact]
    public async Task Trade_page_shows_empty_state_without_friends()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        using var ctx = NewContext(env, env.C);

        var cut = RenderLoaded<TradePage>(ctx);

        Assert.Contains("Noch keine Freunde", cut.Markup);
    }

    [Fact]
    public async Task Selecting_a_partner_shows_balanced_suggestion_images_and_rarity_tags()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        AddBalancedCards(env);
        using var ctx = NewContext(env, env.A);
        var cut = RenderLoaded<TradePage>(ctx);

        cut.Find(".trade-partner button").Click();
        cut.WaitForElement("#trade-suggestion");

        Assert.Contains("Vorschlag für bob", cut.Markup);
        Assert.Single(cut.FindAll(".trade-pair"));
        Assert.Empty(cut.FindAll(".is-unausgewogen"));
        Assert.Equal("Ausgewogen", cut.Find("#trade-balance .cv-chip").TextContent.Trim());
        Assert.Equal(2, cut.FindAll("img.trade-card-image").Count);
        Assert.Contains("Zane", cut.Find(".trade-pair").TextContent);
        Assert.Contains("Jay", cut.Find(".trade-pair").TextContent);
        Assert.NotEmpty(cut.FindAll(".trade-pair .trade-card-tags .cv-chip"));
    }

    [Fact]
    public async Task Without_possible_trade_the_propose_and_add_pair_buttons_are_hidden()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        using var ctx = NewContext(env, env.A);
        var cut = RenderLoaded<TradePage>(ctx);
        cut.Find(".trade-partner button").Click();
        cut.WaitForElement("#trade-suggestion");

        while (cut.FindAll(".trade-pair").Count > 0)
        {
            cut.Find(".trade-pair-middle button").Click();
        }

        Assert.Empty(cut.FindAll(".trade-pair"));
        Assert.Contains("Kein Tausch möglich", cut.Markup);
        Assert.Empty(cut.FindAll("#trade-propose"));
        Assert.DoesNotContain("Tausch vorschlagen", cut.Find("#trade-suggestion").TextContent);
        Assert.DoesNotContain("Weiteres Kartenpaar hinzufügen", cut.Find("#trade-suggestion").TextContent);
        Assert.NotEmpty(cut.FindAll("#trade-partners .trade-section-head #trade-refresh"));
    }

    [Fact]
    public async Task Unausgewogen_flag_and_balance_indicator_update_after_swap_and_remove()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        AddBalancedCards(env);
        using var ctx = NewContext(env, env.A);
        var cut = RenderLoaded<TradePage>(ctx);
        cut.Find(".trade-partner button").Click();
        cut.WaitForElement("#trade-suggestion");
        Assert.Empty(cut.FindAll(".is-unausgewogen"));

        var giveSelect = cut.Find("select[aria-label='Eigene Karte ersetzen']");
        var legendary = giveSelect.QuerySelectorAll("option").Single(o => o.TextContent.Contains("Golden Dragon"));
        giveSelect.Change(legendary.GetAttribute("value")!);

        Assert.Single(cut.FindAll(".is-unausgewogen"));
        Assert.Contains("Unausgewogen", cut.Find(".trade-pair-middle").TextContent);
        Assert.Contains("Unausgewogen (1 von 1 Paaren)", cut.Find("#trade-balance").TextContent);
        Assert.Contains("is-unbalanced", cut.Find("#trade-balance").ClassName);

        var again = cut.Find("select[aria-label='Eigene Karte ersetzen']");
        again.Change(again.QuerySelectorAll("option").Single(o => o.TextContent.Contains("Zane")).GetAttribute("value")!);
        Assert.Empty(cut.FindAll(".is-unausgewogen"));
        Assert.Contains("is-balanced", cut.Find("#trade-balance").ClassName);

        cut.Find(".trade-pair-middle button").Click();
        Assert.Empty(cut.FindAll(".trade-pair"));
        Assert.Contains("Noch keine Karten ausgewählt", cut.Find("#trade-balance").TextContent);
        Assert.Empty(cut.FindAll("#trade-propose"));
    }

    [Fact]
    public async Task Propose_creates_pending_trade_and_shows_it_as_outgoing()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        AddBalancedCards(env);
        using var ctx = NewContext(env, env.A);
        var cut = RenderLoaded<TradePage>(ctx);
        cut.Find(".trade-partner button").Click();
        cut.WaitForElement("#trade-suggestion");

        cut.Find("#trade-propose").Click();

        cut.WaitForAssertion(() => Assert.Contains("Ausgehend", cut.Markup));
        Assert.Contains("Der Tauschvorschlag wurde gesendet.", cut.Find(".trade-message").TextContent);
        Assert.Contains("is-ok", cut.Find(".trade-message").ClassName);
        var trade = Assert.Single(env.Db.Trades.Where(t => t.ProposerUserId == env.A).ToList());
        Assert.Equal(TradeStatus.Pending, trade.Status);
        Assert.Contains("Wartet auf Antwort", cut.Find("#trade-inbox").TextContent);
    }

    [Fact]
    public async Task Cancel_outgoing_proposal_withdraws_it()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        using var ctx = NewContext(env, env.A);
        var cut = RenderLoaded<TradePage>(ctx);

        cut.Find("[data-trade-id] button").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-trade-id]")));
        Assert.Equal(TradeStatus.Cancelled, env.Db.Trades.AsNoTracking().Single(t => t.Id == tradeId).Status);
    }

    [Fact]
    public async Task Incoming_proposal_can_be_accepted()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        using var ctx = NewContext(env, env.B);
        var cut = RenderLoaded<TradePage>(ctx);
        Assert.Contains("Eingehend", cut.Markup);
        Assert.Contains("möchte tauschen", cut.Find("[data-trade-id]").TextContent);

        cut.FindAll("[data-trade-id] button").Single(b => b.TextContent == "Annehmen").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-trade-id]")));
        Assert.Equal(TradeStatus.Completed, env.Db.Trades.AsNoTracking().Single(t => t.Id == tradeId).Status);
        Assert.Contains("is-ok", cut.Find(".trade-message").ClassName);
    }

    [Fact]
    public async Task Incoming_proposal_can_be_declined()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        using var ctx = NewContext(env, env.B);
        var cut = RenderLoaded<TradePage>(ctx);

        cut.FindAll("[data-trade-id] button").Single(b => b.TextContent == "Ablehnen").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-trade-id]")));
        Assert.Equal(TradeStatus.Declined, env.Db.Trades.AsNoTracking().Single(t => t.Id == tradeId).Status);
    }

    [Fact]
    public async Task Executing_incoming_trade_stays_visible_in_inbox()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        env.Db.Trades.Single(t => t.Id == tradeId).Status = TradeStatus.Executing;
        await env.Db.SaveChangesAsync();
        using var ctx = NewContext(env, env.B);

        var cut = RenderLoaded<TradePage>(ctx);

        Assert.Single(cut.FindAll("[data-trade-id]"));
    }

    [Fact]
    public async Task Accepting_a_stale_trade_shows_german_error_and_marks_it_failed()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        using var ctx = NewContext(env, env.B);
        var cut = RenderLoaded<TradePage>(ctx);
        await env.DeletePhotoAsync(env.CollB, "b6a");

        cut.FindAll("[data-trade-id] button").Single(b => b.TextContent == "Annehmen").Click();

        cut.WaitForAssertion(() => Assert.Contains("is-error", cut.Find(".trade-message").ClassName));
        Assert.StartsWith("Der Tausch ist nicht mehr gültig und wurde abgebrochen.", cut.Find(".trade-message").TextContent);
        Assert.Equal(TradeStatus.Failed, env.Db.Trades.AsNoTracking().Single(t => t.Id == tradeId).Status);
        Assert.Empty(cut.FindAll("[data-trade-id]"));
    }

    [Fact]
    public async Task Accepting_a_trade_already_handled_elsewhere_shows_german_error()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        using var ctx = NewContext(env, env.B);
        var cut = RenderLoaded<TradePage>(ctx);
        Assert.True((await env.Service.CancelAsync(env.A, tradeId)).Success);

        cut.FindAll("[data-trade-id] button").Single(b => b.TextContent == "Annehmen").Click();

        cut.WaitForAssertion(() => Assert.Contains("is-error", cut.Find(".trade-message").ClassName));
        Assert.Equal("Dieser Tausch wurde bereits bearbeitet.", cut.Find(".trade-message").TextContent.Trim());
    }

    [Fact]
    public async Task Proposing_a_card_that_got_reserved_meanwhile_shows_german_error()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        AddBalancedCards(env);
        using var ctx = NewContext(env, env.A);
        var cut = RenderLoaded<TradePage>(ctx);
        cut.Find(".trade-partner button").Click();
        cut.WaitForElement("#trade-suggestion");
        Assert.True((await env.Service.ProposeAsync(env.A, env.B, ["a5"], ["b6a"])).Success);

        cut.Find("#trade-propose").Click();

        cut.WaitForAssertion(() => Assert.Contains("is-error", cut.Find(".trade-message").ClassName));
        Assert.Contains("Der Vorschlag hat sich geändert", cut.Find(".trade-message").TextContent);
        Assert.Single(env.Db.Trades.Where(t => t.ProposerUserId == env.A).ToList());
        Assert.NotNull(cut.Find("#trade-suggestion"));
    }

    [Fact]
    public async Task Trade_log_page_lists_only_own_trades_newest_first_with_outcome()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var first = await env.ProposeStandardAsync();
        Assert.True((await env.Service.AcceptAsync(env.B, first)).Success);
        var second = (await env.Service.ProposeAsync(env.A, env.B, ["la1"], ["b6b"])).TradeId!;
        Assert.True((await env.Service.DeclineAsync(env.B, second)).Success);
        var entry = env.Db.TradeLogEntries.Single(e => e.TradeId == first);
        entry.CompletedAtUtc = entry.CompletedAtUtc.AddDays(-2);
        await env.Db.SaveChangesAsync();

        using var ctx = NewContext(env, env.A);
        var cut = RenderLoaded<TradeLog>(ctx);

        var rows = cut.FindAll(".trade-log-entry");
        Assert.Equal(2, rows.Count);
        Assert.Equal(second, rows[0].GetAttribute("data-trade-id"));
        Assert.Contains("Abgelehnt", rows[0].TextContent);
        Assert.Contains("Abgeschlossen", rows[1].TextContent);
        Assert.Contains("bob", rows[1].TextContent);
        Assert.Contains("Gegeben", rows[1].TextContent);
        Assert.Contains("Cole", rows[1].TextContent);
        Assert.Contains("Jay", rows[1].TextContent);
        Assert.Empty(cut.FindAll("button"));

        using var otherCtx = NewContext(env, env.C);
        var other = RenderLoaded<TradeLog>(otherCtx);
        Assert.Empty(other.FindAll(".trade-log-entry"));
        Assert.Contains("Noch keine Tauschvorgänge", other.Markup);
    }

    [Fact]
    public async Task Trade_log_keeps_partner_name_after_the_partner_account_loses_its_name()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        Assert.True((await env.Service.DeclineAsync(env.B, tradeId)).Success);
        env.Db.Users.Single(u => u.Id == env.B).UserName = null;
        await env.Db.SaveChangesAsync();

        var rows = await new TradeLogService(env.Db).ListAsync(env.A);

        Assert.Equal("bob", Assert.Single(rows).PartnerUserName);
    }
}
