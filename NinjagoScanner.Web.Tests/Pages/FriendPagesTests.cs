using System.Globalization;
using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NinjagoScanner.PictureService.Protos;
using NinjagoScanner.Web.Components.Pages;
using NinjagoScanner.Web.Data;
using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Pages;

/// <summary>
/// Renders the real /friends and /friends/{username} pages (bUnit) against the in-process fakes:
/// German text, visibility toggle, private notice, not-found without data leaks, read-only guarantee.
/// Seeds come from TradeTestEnv (alice/bob are friends, carol has no friends).
/// </summary>
public class FriendPagesTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    private sealed class FixedAuth(string userId) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))));
    }

    private sealed class RecordingReader(IForeignCollectionReader inner, bool failUrls = false) : IForeignCollectionReader
    {
        public List<string> Calls { get; } = [];

        public Task<IReadOnlyList<CardEntry>> ListCardEntriesAsync(string collectionId, CancellationToken cancellationToken = default)
        {
            Calls.Add($"list:{collectionId}");
            return inner.ListCardEntriesAsync(collectionId, cancellationToken);
        }

        public Task<IReadOnlyDictionary<string, string>> GetDownloadUrlsAsync(
            string collectionId, IEnumerable<string> photoIds, CancellationToken cancellationToken = default)
        {
            Calls.Add($"urls:{collectionId}");
            if (failUrls)
            {
                throw new Grpc.Core.RpcException(new Grpc.Core.Status(Grpc.Core.StatusCode.Unavailable, "urls down"));
            }

            return inner.GetDownloadUrlsAsync(collectionId, photoIds, cancellationToken);
        }
    }

    private static RecordingReader NewReader(TradeTestEnv env, bool failUrls = false) =>
        new(new PictureServiceForeignCollectionReader(env.PictureClient), failUrls);

    private static BunitContext NewContext(TradeTestEnv env, string viewerId, RecordingReader? reader = null)
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<AuthenticationStateProvider>(new FixedAuth(viewerId));
        ctx.Services.AddSingleton(env.Db);
        ctx.Services.AddSingleton(env.CatalogClient);
        ctx.Services.AddSingleton(new FriendService(env.Db));
        ctx.Services.AddSingleton(new FriendAccessService(env.Db));
        ctx.Services.AddSingleton<IForeignCollectionReader>(reader ?? NewReader(env));
        ctx.Services.AddSingleton<FriendCollectionService>();
        // Deliberately NOT registered: PictureServiceClient, GamificationCelebrationCenter, GamificationService.
        // If either page reached a write RPC or a celebration, resolving it would throw.
        return ctx;
    }

    private static IRenderedComponent<TComponent> RenderLoaded<TComponent>(BunitContext ctx, Action<ComponentParameterCollectionBuilder<TComponent>>? parameters = null)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        var cut = ctx.Render(parameters);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Lade ", cut.Markup), Wait);
        return cut;
    }

    // ---------- /friends/{username}

    [Fact]
    public async Task Friend_collection_renders_german_overview_rank_comparison_gallery_and_achievements()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        using var ctx = NewContext(env, env.B);

        var cut = RenderLoaded<FriendCollection>(ctx, p => p.Add(x => x.Username, "alice"));

        Assert.Contains("Sammlung von", cut.Markup);
        Assert.Contains("alice", cut.Find("h1").TextContent);
        Assert.Contains("Nur ansehen", cut.Markup);
        Assert.Contains("Rang & Erfahrung", cut.Find(".rank-status-block").TextContent);
        Assert.Contains("XP", cut.Find(".rank-status-meta").TextContent);
        Assert.Contains("Vergleich mit dir", cut.Markup);
        // alice owns Serie 2 #4,#5 and Serie 10 #2; bob owns Serie 2 #6,#4 -> shared 1, friend-only 2, viewer-only 1
        Assert.Equal("1", cut.Find("[data-compare=shared]").TextContent);
        Assert.Equal("2", cut.Find("[data-compare=friend-only]").TextContent);
        Assert.Equal("1", cut.Find("[data-compare=viewer-only]").TextContent);
        Assert.Contains("Freigeschaltete Erfolge", cut.Markup);
        Assert.NotEmpty(cut.FindAll("#friend-achievements .achievement-card.is-unlocked"));
        Assert.Contains("Freigeschaltet", cut.Find("#friend-achievements").TextContent);
        Assert.DoesNotContain("Gesperrt", cut.Find("#friend-achievements").TextContent);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#friend-gallery .gallery-tile")), Wait);
        var gallery = cut.Find("#friend-gallery").TextContent;
        Assert.Contains("Cole", gallery);
        Assert.Contains("Zane", gallery);
        Assert.DoesNotContain("Jay", gallery); // in catalog, not owned by alice
        Assert.Empty(cut.FindAll("#friend-private"));
        Assert.Empty(cut.FindAll("#friend-not-found"));
        Assert.Contains("friends", cut.Find("header a").GetAttribute("href"));
    }

    [Fact]
    public async Task Friend_collection_is_read_only_no_unlocks_no_bonus_xp_no_write_surface()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var reader = NewReader(env);
        var unlocksBefore = await env.Db.AchievementUnlocks.CountAsync();
        var profilesBefore = await env.Db.GamificationProfiles.CountAsync();
        using var ctx = NewContext(env, env.B, reader);

        var cut = RenderLoaded<FriendCollection>(ctx, p => p.Add(x => x.Username, "alice"));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#friend-gallery .gallery-tile")), Wait);

        // Nothing persisted for the viewer or the friend; the celebration service was never resolvable.
        Assert.Equal(unlocksBefore, await env.Db.AchievementUnlocks.CountAsync());
        Assert.Equal(profilesBefore, await env.Db.GamificationProfiles.CountAsync());
        Assert.All(reader.Calls, call => Assert.True(call.StartsWith("list:") || call.StartsWith("urls:")));
        Assert.Empty(cut.FindAll("button"));
        Assert.Empty(cut.FindAll("input, textarea"));
        Assert.DoesNotContain("Erfolg freigeschaltet", cut.Markup);
        Assert.Empty(env.Pictures.Transfers);
    }

    [Fact]
    public async Task Private_collection_shows_notice_and_leaks_no_collection_data()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        Assert.True(await new FriendAccessService(env.Db).SetOwnVisibilityAsync(env.A, CollectionVisibility.Private));
        var reader = NewReader(env);
        using var ctx = NewContext(env, env.B, reader);

        var cut = RenderLoaded<FriendCollection>(ctx, p => p.Add(x => x.Username, "alice"));

        Assert.Contains("Diese Sammlung ist privat", cut.Find("#friend-private").TextContent);
        Assert.Contains("Nur ich", cut.Find("#friend-private").TextContent);
        Assert.Contains("alice", cut.Find("h1").TextContent);
        Assert.Empty(cut.FindAll("#friend-gallery"));
        Assert.Empty(cut.FindAll("#friend-comparison"));
        Assert.Empty(cut.FindAll("#friend-achievements"));
        Assert.Empty(cut.FindAll(".rank-status-block"));
        Assert.Empty(cut.FindAll(".gallery-tile"));
        Assert.DoesNotContain("Cole", cut.Markup);
        Assert.DoesNotContain("XP", cut.Markup);
        Assert.Empty(reader.Calls);
        Assert.Equal(0, env.Pictures.DownloadUrlCallCount);
    }

    [Theory]
    [InlineData("alice", "carol")]  // carol is not a friend of alice
    [InlineData("nobody", "alice")] // unknown user
    [InlineData("alice", "alice")]  // own name
    public async Task Non_friend_unknown_or_self_shows_generic_not_found_without_data(string username, string viewer)
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var viewerId = viewer == "carol" ? env.C : env.A;
        var reader = NewReader(env);
        using var ctx = NewContext(env, viewerId, reader);

        var cut = RenderLoaded<FriendCollection>(ctx, p => p.Add(x => x.Username, username));

        Assert.Contains("Seite nicht gefunden", cut.Find("#friend-not-found").TextContent);
        // Indistinguishable from an unknown page: no name, no "privat", no data.
        Assert.DoesNotContain("privat", cut.Markup);
        Assert.DoesNotContain("alice", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(cut.FindAll("#friend-gallery"));
        Assert.Empty(cut.FindAll(".gallery-tile"));
        Assert.Empty(reader.Calls);
        Assert.Equal(0, env.Pictures.DownloadUrlCallCount);
    }

    [Fact]
    public async Task Gallery_rpc_failure_keeps_the_rest_of_the_page_and_shows_gallery_error()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        using var ctx = NewContext(env, env.B, NewReader(env, failUrls: true));

        var cut = RenderLoaded<FriendCollection>(ctx, p => p.Add(x => x.Username, "alice"));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#friend-gallery-error")), Wait);

        Assert.Contains("Die Galerie konnte nicht geladen werden", cut.Find("#friend-gallery-error").TextContent);
        Assert.Contains("Vergleich mit dir", cut.Markup);
        Assert.NotEmpty(cut.FindAll(".rank-status-block"));
        Assert.NotEmpty(cut.FindAll("#friend-achievements"));
        Assert.DoesNotContain("Sammlung konnte nicht geladen werden", cut.Markup);
    }

    [Fact]
    public async Task Unlock_date_is_formatted_with_german_culture_regardless_of_server_culture()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        env.Db.AchievementUnlocks.Add(new AchievementUnlock
        {
            CollectionId = env.CollA,
            AchievementId = "first-scan",
            UnlockedAtUtc = new DateTime(2026, 3, 12, 12, 0, 0, DateTimeKind.Utc)
        });
        await env.Db.SaveChangesAsync();
        using var ctx = NewContext(env, env.B);
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        try
        {
            var cut = RenderLoaded<FriendCollection>(ctx, p => p.Add(x => x.Username, "alice"));

            var text = System.Text.RegularExpressions.Regex.Replace(cut.Find("#friend-achievements").TextContent, @"\s+", " ");
            Assert.Matches(@"12 Mär\S* 2026", text);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ---------- /friends

    [Fact]
    public async Task Friends_page_lists_friends_requests_and_visibility_in_german()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        Assert.Equal(SendFriendRequestOutcome.Sent, await new FriendService(env.Db).SendRequestAsync(env.C, env.A));
        using var ctx = NewContext(env, env.A);

        var cut = RenderLoaded<Friends>(ctx);

        Assert.Contains("Freunde", cut.Find("h1").TextContent);
        Assert.Contains("Nur ich", cut.Find("#friends-visibility").TextContent);
        Assert.Contains("is-active", cut.FindAll("#friends-visibility button").Single(b => b.TextContent == "Freunde").ClassName);
        Assert.Contains("carol", cut.Find("#friends-incoming").TextContent);
        Assert.Contains("Annehmen", cut.Find("#friends-incoming").TextContent);
        Assert.Contains("Ablehnen", cut.Find("#friends-incoming").TextContent);
        Assert.Contains("Keine gesendeten Anfragen.", cut.Find("#friends-outgoing").TextContent);
        Assert.Contains("bob", cut.Find("#friends-list").TextContent);
        Assert.Contains("Entfernen", cut.Find("#friends-list").TextContent);
        Assert.Contains("friends/bob", cut.Find("#friends-list a").GetAttribute("href"));
    }

    [Fact]
    public async Task Visibility_toggle_persists_and_is_enforced_for_the_friend_view()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        using var ctx = NewContext(env, env.A);
        var cut = RenderLoaded<Friends>(ctx);

        cut.FindAll("#friends-visibility button").Single(b => b.TextContent == "Nur ich").Click();

        cut.WaitForAssertion(() => Assert.Contains("nur für dich", cut.Find(".friends-message").TextContent), Wait);
        Assert.Contains("is-active", cut.FindAll("#friends-visibility button").Single(b => b.TextContent == "Nur ich").ClassName);
        var access = new FriendAccessService(env.Db);
        Assert.Equal(CollectionVisibility.Private, await access.GetOwnVisibilityAsync(env.A));
        Assert.Equal(FriendAccessStatus.Private, (await access.ResolveVisibleCollectionAsync(env.B, "alice")).Status);

        cut.FindAll("#friends-visibility button").Single(b => b.TextContent == "Freunde").Click();
        cut.WaitForAssertion(() => Assert.Contains("für Freunde sichtbar", cut.Find(".friends-message").TextContent), Wait);
        Assert.Equal(CollectionVisibility.Friends, await access.GetOwnVisibilityAsync(env.A));
        Assert.Equal(FriendAccessStatus.Visible, (await access.ResolveVisibleCollectionAsync(env.B, "alice")).Status);
    }

    [Fact]
    public async Task Search_requires_two_characters_and_send_request_flow_updates_outgoing_list()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        using var ctx = NewContext(env, env.A);
        var cut = RenderLoaded<Friends>(ctx);

        cut.Find("#friends-search input").Input("c");
        cut.Find("#friends-search form").Submit();
        Assert.Contains("Bitte mindestens 2 Zeichen eingeben.", cut.Find("#friends-search").TextContent);

        cut.Find("#friends-search input").Input("caro");
        cut.Find("#friends-search form").Submit();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("#friends-search .friends-row")), Wait);
        Assert.Contains("Kein Kontakt", cut.Find("#friends-search .friends-row").TextContent);

        cut.Find("#friends-search .friends-row button").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("#friends-outgoing .friends-row")), Wait);
        Assert.Contains("Freundschaftsanfrage gesendet.", cut.Find(".friends-message").TextContent);
        Assert.Contains("carol", cut.Find("#friends-outgoing").TextContent);
        Assert.Contains("Anfrage gesendet", cut.Find("#friends-search .friends-row").TextContent);
    }

    [Fact]
    public async Task Search_without_hits_says_so()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        using var ctx = NewContext(env, env.A);
        var cut = RenderLoaded<Friends>(ctx);

        cut.Find("#friends-search input").Input("zzzz");
        cut.Find("#friends-search form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Keine Nutzer gefunden.", cut.Find("#friends-search").TextContent), Wait);
    }

    [Fact]
    public async Task Accept_decline_cancel_and_remove_with_confirmation()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var friends = new FriendService(env.Db);
        var (dave, _) = await env.AddUserAsync("dave");
        var (erin, _) = await env.AddUserAsync("erin");
        await friends.SendRequestAsync(env.C, env.A);   // incoming: carol
        await friends.SendRequestAsync(dave, env.A);    // incoming: dave
        await friends.SendRequestAsync(env.A, erin);    // outgoing: erin
        using var ctx = NewContext(env, env.A);
        var cut = RenderLoaded<Friends>(ctx);
        Assert.Equal(2, cut.FindAll("#friends-incoming .friends-row").Count);

        cut.Find("#friends-incoming [data-user=carol] button.cv-btn-primary").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("#friends-list [data-user=carol]")), Wait);
        Assert.Contains("Du bist jetzt mit carol befreundet.", cut.Find(".friends-message").TextContent);

        cut.Find("#friends-incoming [data-user=dave] button.cv-btn-secondary").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#friends-incoming .friends-row")), Wait);
        Assert.Contains("Anfrage von dave abgelehnt.", cut.Find(".friends-message").TextContent);
        Assert.Contains("Keine offenen Anfragen.", cut.Find("#friends-incoming").TextContent);

        cut.Find("#friends-outgoing [data-user=erin] button").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#friends-outgoing .friends-row")), Wait);
        Assert.Contains("Anfrage an erin zurückgezogen.", cut.Find(".friends-message").TextContent);

        // Remove needs confirmation; "Abbrechen" keeps the friend.
        cut.Find("#friends-list [data-user=bob] button").Click();
        Assert.Contains("Freundschaft wirklich beenden?", cut.Find("#friends-list [data-user=bob]").TextContent);
        cut.FindAll("#friends-list [data-user=bob] button").Single(b => b.TextContent == "Abbrechen").Click();
        Assert.Single(cut.FindAll("#friends-list [data-user=bob]"));
        Assert.True(await friends.AreFriendsAsync(env.A, env.B));

        cut.Find("#friends-list [data-user=bob] button").Click();
        cut.FindAll("#friends-list [data-user=bob] button").Single(b => b.TextContent == "Ja, entfernen").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#friends-list [data-user=bob]")), Wait);
        Assert.Contains("bob wurde als Freund entfernt.", cut.Find(".friends-message").TextContent);
        Assert.False(await friends.AreFriendsAsync(env.A, env.B));
    }

    [Fact]
    public async Task Friends_page_without_friends_shows_empty_state()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        using var ctx = NewContext(env, env.C);

        var cut = RenderLoaded<Friends>(ctx);

        Assert.Contains("Du hast noch keine Freunde.", cut.Find("#friends-list").TextContent);
        Assert.Contains("Keine offenen Anfragen.", cut.Find("#friends-incoming").TextContent);
    }
}
