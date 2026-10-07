using Microsoft.EntityFrameworkCore;
using NinjagoScanner.Web.Data;
using NinjagoScanner.Web.Models;
using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Services;

/// <summary>
/// web-collection-sharing: read-only friend view, server-side access enforcement, comparison.
/// TradeTestEnv seeds (all catalog cards "Serie 2" #4-6 and "Serie 10" #1-2):
///   alice(A): #4 (x2), #5, Serie 10 #2 (x2, legendary)   bob(B): #6 (x3), #4, one unmapped photo
///   carol(C): no friendship, no photos.
/// </summary>
public class FriendCollectionServiceTests
{
    private static FriendCollectionService CreateService(TradeTestEnv env, IForeignCollectionReader? reader = null) =>
        new(new FriendAccessService(env.Db), env.CatalogClient,
            reader ?? new PictureServiceForeignCollectionReader(env.PictureClient), env.Db);

    // --- Scenario: Friend views collection

    [Fact]
    public async Task Friend_sees_overview_rank_xp_and_achievements_read_only()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        var view = await CreateService(env).GetViewAsync(env.B, "Alice");

        Assert.Equal(FriendAccessStatus.Visible, view.Status);
        Assert.Equal("alice", view.OwnerUserName);
        Assert.NotNull(view.Summary);
        Assert.Equal(3, view.Summary!.OwnedCatalogCards);
        Assert.Equal(5, view.Summary.TotalCatalogCards);
        Assert.True(view.Xp > 0);
        Assert.NotNull(view.Rank);
        Assert.Equal(RankDefinitions.ForXp(view.Xp).Level, view.Rank!.Level);
        var unlocked = view.Achievements.Where(a => a.IsUnlocked).Select(a => a.Achievement.Id).ToList();
        Assert.Contains("first-scan", unlocked);
        Assert.Contains("first-legendary", unlocked);
        Assert.Equal(AchievementDefinitions.All.Count, view.Achievements.Count);
        Assert.Contains("Serie 2", view.SeriesNames);
    }

    [Fact]
    public async Task Friend_view_uses_the_friends_bonus_xp()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var service = CreateService(env);
        var before = (await service.GetViewAsync(env.B, "alice")).Xp;

        env.Db.GamificationProfiles.Add(new GamificationProfile { CollectionId = env.CollA, BonusXp = 40 });
        await env.Db.SaveChangesAsync();

        Assert.Equal(before + 40, (await service.GetViewAsync(env.B, "alice")).Xp);
    }

    [Fact]
    public async Task Friend_gallery_lists_only_owned_cards_with_download_urls()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        var gallery = await CreateService(env).GetOwnedGalleryAsync(env.B, "alice", "Serie 2");

        Assert.NotNull(gallery);
        Assert.Equal(["4", "5"], gallery!.Select(c => c.CardNumber).OrderBy(n => n, StringComparer.Ordinal));
        Assert.DoesNotContain(gallery, c => c.CardNumber == "6"); // Jay: in catalog, not owned by Alice
        Assert.All(gallery, c => Assert.False(string.IsNullOrEmpty(c.ImageUrl)));
        Assert.Equal(2, gallery!.Single(c => c.CardNumber == "4").PhotoCount);
    }

    [Fact]
    public async Task Friend_gallery_of_series_without_owned_cards_is_empty_not_null()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        var gallery = await CreateService(env).GetOwnedGalleryAsync(env.A, "bob", "Serie 10");

        Assert.NotNull(gallery);
        Assert.Empty(gallery!);
    }

    // --- Scenario: Non-friend denied / Forged collection id

    [Fact]
    public async Task Non_friend_gets_not_found_and_no_picture_service_call()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var reader = new RecordingReader(new PictureServiceForeignCollectionReader(env.PictureClient));
        var service = CreateService(env, reader);

        var view = await service.GetViewAsync(env.C, "alice");
        var gallery = await service.GetOwnedGalleryAsync(env.C, "alice", "Serie 2");

        Assert.Equal(FriendAccessStatus.NotFound, view.Status);
        Assert.Null(view.Summary);
        Assert.Null(view.OwnerUserName);
        Assert.Empty(view.Achievements);
        Assert.Null(gallery);
        Assert.Empty(reader.Calls);
        Assert.Equal(0, env.Pictures.DownloadUrlCallCount);
    }

    [Theory]
    [InlineData("does-not-exist")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Unknown_or_empty_username_is_not_found_without_picture_service_call(string? username)
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var reader = new RecordingReader(new PictureServiceForeignCollectionReader(env.PictureClient));

        var view = await CreateService(env, reader).GetViewAsync(env.A, username);

        Assert.Equal(FriendAccessStatus.NotFound, view.Status);
        Assert.Empty(reader.Calls);
    }

    [Fact]
    public async Task Forged_collection_id_in_place_of_username_is_not_found()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var reader = new RecordingReader(new PictureServiceForeignCollectionReader(env.PictureClient));
        var service = CreateService(env, reader);

        // A user in Alice's friend circle (Bob) supplies Carol's collection id instead of a username.
        var view = await service.GetViewAsync(env.B, env.CollC);

        Assert.Equal(FriendAccessStatus.NotFound, view.Status);
        Assert.Empty(reader.Calls);
    }

    [Fact]
    public async Task Pending_request_does_not_grant_view()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        await new FriendService(env.Db).SendRequestAsync(env.C, env.A);

        Assert.Equal(FriendAccessStatus.NotFound, (await CreateService(env).GetViewAsync(env.C, "alice")).Status);
    }

    [Fact]
    public async Task Viewing_own_collection_through_friend_route_is_not_found()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        Assert.Equal(FriendAccessStatus.NotFound, (await CreateService(env).GetViewAsync(env.A, "alice")).Status);
    }

    [Fact]
    public async Task Removed_friendship_revokes_view_immediately()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var service = CreateService(env);
        Assert.Equal(FriendAccessStatus.Visible, (await service.GetViewAsync(env.B, "alice")).Status);

        await new FriendService(env.Db).RemoveAsync(env.A, env.B);

        Assert.Equal(FriendAccessStatus.NotFound, (await service.GetViewAsync(env.B, "alice")).Status);
        Assert.Null(await service.GetOwnedGalleryAsync(env.B, "alice", "Serie 2"));
    }

    // --- Scenario: Private collection denied / Owner sets private

    [Fact]
    public async Task Private_collection_returns_private_notice_without_data_or_picture_calls()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        await new FriendAccessService(env.Db).SetOwnVisibilityAsync(env.A, CollectionVisibility.Private);
        var reader = new RecordingReader(new PictureServiceForeignCollectionReader(env.PictureClient));
        var service = CreateService(env, reader);

        var view = await service.GetViewAsync(env.B, "alice");
        var gallery = await service.GetOwnedGalleryAsync(env.B, "alice", "Serie 2");

        Assert.Equal(FriendAccessStatus.Private, view.Status);
        Assert.Equal("alice", view.OwnerUserName);
        Assert.Null(view.Summary);
        Assert.Null(view.Rank);
        Assert.Empty(view.Achievements);
        Assert.Null(view.Comparison);
        Assert.Null(gallery);
        Assert.Empty(reader.Calls);
        Assert.True(await new FriendService(env.Db).AreFriendsAsync(env.A, env.B)); // still friends
    }

    [Fact]
    public async Task Setting_visibility_back_to_friends_restores_the_view()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var access = new FriendAccessService(env.Db);
        await access.SetOwnVisibilityAsync(env.A, CollectionVisibility.Private);
        await access.SetOwnVisibilityAsync(env.A, CollectionVisibility.Friends);

        Assert.Equal(FriendAccessStatus.Visible, (await CreateService(env).GetViewAsync(env.B, "alice")).Status);
    }

    // --- Scenario: Comparison counts

    [Fact]
    public async Task Comparison_counts_shared_friend_only_and_viewer_only()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        // Viewer Bob owns #6 and #4 of Serie 2; friend Alice owns #4, #5, and Serie 10 #2.
        var view = await CreateService(env).GetViewAsync(env.B, "alice");

        Assert.NotNull(view.Comparison);
        Assert.Equal(new FriendComparison(Shared: 1, FriendOnly: 2, ViewerOnly: 1), view.Comparison);
    }

    [Fact]
    public async Task Comparison_is_mirrored_from_the_other_side()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        var view = await CreateService(env).GetViewAsync(env.A, "bob");

        Assert.Equal(new FriendComparison(Shared: 1, FriendOnly: 1, ViewerOnly: 2), view.Comparison);
    }

    [Fact]
    public async Task Comparison_counts_distinct_cards_not_copies_and_ignores_unmapped_photos()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        // Bob has 3x #6 plus an unmapped photo: still only 2 distinct catalog cards.
        var view = await CreateService(env).GetViewAsync(env.A, "bob");

        var comparison = view.Comparison!;
        Assert.Equal(2, comparison.Shared + comparison.FriendOnly);
    }

    [Fact]
    public async Task Viewer_without_cards_sees_everything_as_friend_only()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        await env.MakeFriendsAsync(env.C, env.A);

        var view = await CreateService(env).GetViewAsync(env.C, "alice");

        Assert.Equal(new FriendComparison(0, 3, 0), view.Comparison);
    }

    [Fact]
    public void Compare_handles_empty_collections()
    {
        Assert.Equal(new FriendComparison(0, 0, 0), FriendCollectionService.Compare([], [], []));
    }

    // --- Scenario: No mutating actions

    [Fact]
    public async Task Viewing_a_friend_writes_nothing_and_never_celebrates()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var unlocksBefore = await env.Db.AchievementUnlocks.CountAsync();
        var profilesBefore = await env.Db.GamificationProfiles.CountAsync();
        var photosA = env.Pictures.PhotoIds(env.CollA).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var photosB = env.Pictures.PhotoIds(env.CollB).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var service = CreateService(env);

        await service.GetViewAsync(env.B, "alice");
        await service.GetOwnedGalleryAsync(env.B, "alice", "Serie 2");

        Assert.Equal(unlocksBefore, await env.Db.AchievementUnlocks.CountAsync());
        Assert.Equal(profilesBefore, await env.Db.GamificationProfiles.CountAsync());
        Assert.Equal(photosA, env.Pictures.PhotoIds(env.CollA).OrderBy(x => x, StringComparer.Ordinal).ToList());
        Assert.Equal(photosB, env.Pictures.PhotoIds(env.CollB).OrderBy(x => x, StringComparer.Ordinal).ToList());
        Assert.Empty(env.Pictures.Transfers);
        Assert.Empty(env.Pictures.Uploads);
    }

    [Fact]
    public void Foreign_collection_reader_exposes_only_read_members()
    {
        var members = typeof(IForeignCollectionReader).GetMethods().Select(m => m.Name).OrderBy(n => n).ToArray();

        Assert.Equal(["GetDownloadUrlsAsync", "ListCardEntriesAsync"], members);
    }

    [Fact]
    public void Friend_collection_service_depends_on_no_write_capable_service()
    {
        var ctor = typeof(FriendCollectionService).GetConstructors().Single();
        var dependencies = ctor.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.DoesNotContain(typeof(PictureServiceClient), dependencies);
        Assert.DoesNotContain(typeof(GamificationService), dependencies);
        Assert.DoesNotContain(typeof(GamificationCelebrationCenter), dependencies);
        Assert.DoesNotContain(typeof(TradeService), dependencies);
    }

    [Fact]
    public void Friend_pages_do_not_inject_write_capable_services()
    {
        var root = FindWebProjectDirectory();
        foreach (var page in new[] { "Friends.razor", "FriendCollection.razor" })
        {
            var source = File.ReadAllText(Path.Combine(root, "Components", "Pages", page));
            Assert.DoesNotContain("@inject PictureServiceClient", source);
            Assert.DoesNotContain("@inject GamificationService", source);
            Assert.DoesNotContain("@inject GamificationCelebrationCenter", source);
            Assert.DoesNotContain("@inject CollectionQueryService", source);
            Assert.Contains("@attribute [Authorize]", source); // anonymous access redirects to sign-in
        }
    }

    private static string FindWebProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "NinjagoScanner.Web", "Components")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("repo root not found"), "NinjagoScanner.Web");
    }

    private sealed class RecordingReader(IForeignCollectionReader inner) : IForeignCollectionReader
    {
        public List<string> Calls { get; } = [];

        public Task<IReadOnlyList<NinjagoScanner.PictureService.Protos.CardEntry>> ListCardEntriesAsync(
            string collectionId, CancellationToken cancellationToken = default)
        {
            Calls.Add($"list:{collectionId}");
            return inner.ListCardEntriesAsync(collectionId, cancellationToken);
        }

        public Task<IReadOnlyDictionary<string, string>> GetDownloadUrlsAsync(
            string collectionId, IEnumerable<string> photoIds, CancellationToken cancellationToken = default)
        {
            Calls.Add($"urls:{collectionId}");
            return inner.GetDownloadUrlsAsync(collectionId, photoIds, cancellationToken);
        }
    }
}
