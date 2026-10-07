using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using NinjagoScanner.Web.Components.Layout;
using NinjagoScanner.Web.Data;
using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Pages;

/// <summary>Renders the nav (top + bottom bar) and guards against raw Razor code leaking into link labels.</summary>
public class NavMenuRenderTests
{
    private sealed class FixedUserAuth(string userId) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Name, "tester")], "Test"))));
    }

    private sealed class AllowAllAuthorization : Microsoft.AspNetCore.Authorization.IAuthorizationService
    {
        public Task<Microsoft.AspNetCore.Authorization.AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user, object? resource, IEnumerable<Microsoft.AspNetCore.Authorization.IAuthorizationRequirement> requirements) =>
            Task.FromResult(Microsoft.AspNetCore.Authorization.AuthorizationResult.Success());

        public Task<Microsoft.AspNetCore.Authorization.AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user, object? resource, string policyName) =>
            Task.FromResult(Microsoft.AspNetCore.Authorization.AuthorizationResult.Success());
    }

    private sealed class NoCollectionContext : ICurrentCollectionContext
    {
        public Task<Collection?> GetOwnedCollectionAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default) =>
            Task.FromResult<Collection?>(null);
    }

    private static BunitContext NewContext(TradeTestEnv env, string userId)
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var auth = new FixedUserAuth(userId);
        var collections = new NoCollectionContext();
        ctx.Services.AddSingleton<AuthenticationStateProvider>(auth);
        ctx.Services.AddSingleton<ICurrentCollectionContext>(collections);
        ctx.Services.AddSingleton(new GamificationService(env.CatalogClient, env.PictureClient, env.Db, collections, auth));
        ctx.Services.AddSingleton(new FriendService(env.Db));
        ctx.Services.AddSingleton(env.Service);
        ctx.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationService>(new AllowAllAuthorization());
        ctx.Services.AddOptions();
        ctx.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider,
            Microsoft.AspNetCore.Authorization.DefaultAuthorizationPolicyProvider>();
        return ctx;
    }

    private static IRenderedComponent<NavMenu> RenderNav(BunitContext ctx) =>
        ctx.Render<NavMenu>(ps => ps.AddCascadingValue(
            ctx.Services.GetRequiredService<AuthenticationStateProvider>().GetAuthenticationStateAsync()));

    [Fact]
    public async Task Nav_renders_german_labels_without_leaked_razor_code_and_without_badges_when_nothing_is_pending()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        using var ctx = NewContext(env, env.C);

        var cut = RenderNav(ctx);

        Assert.DoesNotContain("@if", cut.Markup);
        Assert.DoesNotContain("_pending", cut.Markup);
        Assert.Empty(cut.FindAll(".cv-nav-badge"));
        foreach (var nav in new[] { ".cv-nav-links", ".cv-nav-bottom" })
        {
            var labels = cut.FindAll($"{nav} a").Select(a => a.TextContent.Trim()).ToList();
            Assert.Contains("Freunde", labels);
            Assert.Contains("Tauschen", labels);
            Assert.Contains("Erfolge", labels);
        }
    }

    [Fact]
    public async Task Nav_shows_pending_count_badges_for_friend_requests_and_trades()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        await env.ProposeStandardAsync(); // alice -> bob
        var (low, high) = Friendship.CanonicalPair(env.C, env.B);
        env.Db.Friendships.Add(new Friendship
        {
            RequesterUserId = env.C, AddresseeUserId = env.B, UserLowId = low, UserHighId = high, Status = FriendshipStatus.Pending
        });
        await env.Db.SaveChangesAsync();
        using var ctx = NewContext(env, env.B);

        var cut = RenderNav(ctx);

        Assert.DoesNotContain("@if", cut.Markup);
        foreach (var nav in new[] { ".cv-nav-links", ".cv-nav-bottom" })
        {
            var friends = cut.FindAll($"{nav} a").Single(a => a.GetAttribute("href") == "friends");
            Assert.Equal("Freunde1", friends.TextContent.Replace(" ", "").Trim());
            Assert.Equal("1", friends.QuerySelector(".cv-nav-badge")!.TextContent.Trim());
            Assert.Equal("1 offene Freundschaftsanfragen", friends.QuerySelector(".cv-nav-badge")!.GetAttribute("aria-label"));

            var trade = cut.FindAll($"{nav} a").Single(a => a.GetAttribute("href") == "trade");
            Assert.Equal("1 offene Tauschvorschläge", trade.QuerySelector(".cv-nav-badge")!.GetAttribute("aria-label"));
        }
    }
}
