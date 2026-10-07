using NinjagoScanner.Web.Data;
using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Services;

public class FriendAccessServiceTests
{
    private static async Task MakeFriendsAsync(TestAppDb db, string a, string b)
    {
        var service = new FriendService(db.DbContext);
        await service.SendRequestAsync(a, b);
        await service.AcceptAsync(b, Assert.Single((await service.ListAsync(b)).Incoming).FriendshipId);
    }

    [Fact]
    public async Task Default_visibility_is_friends_without_settings_row()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (owner, collection) = await db.AddUserAsync("A");
        var service = new FriendAccessService(db.DbContext);

        Assert.Equal(CollectionVisibility.Friends, await service.GetVisibilityAsync(collection));
        Assert.Equal(CollectionVisibility.Friends, await service.GetOwnVisibilityAsync(owner));
    }

    [Fact]
    public async Task Friend_resolves_visible_collection_id()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, collectionA) = await db.AddUserAsync("Alice");
        var (b, _) = await db.AddUserAsync("Bob");
        await MakeFriendsAsync(db, a, b);
        var service = new FriendAccessService(db.DbContext);

        var access = await service.ResolveVisibleCollectionAsync(b, "alice"); // case-insensitive

        Assert.Equal(FriendAccessStatus.Visible, access.Status);
        Assert.Equal(collectionA, access.CollectionId);
        Assert.Equal(a, access.OwnerUserId);
        Assert.Equal("Alice", access.OwnerUserName);
    }

    [Fact]
    public async Task Non_friend_is_not_found_and_gets_no_collection_id()
    {
        await using var db = await TestAppDb.CreateAsync();
        await db.AddUserAsync("Alice");
        var (c, _) = await db.AddUserAsync("Carol");
        var service = new FriendAccessService(db.DbContext);

        var access = await service.ResolveVisibleCollectionAsync(c, "Alice");

        Assert.Equal(FriendAccessStatus.NotFound, access.Status);
        Assert.Null(access.CollectionId);
        Assert.Null(access.OwnerUserId);
    }

    [Fact]
    public async Task Pending_request_does_not_grant_access()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, _) = await db.AddUserAsync("Alice");
        var (b, _) = await db.AddUserAsync("Bob");
        await new FriendService(db.DbContext).SendRequestAsync(b, a);
        var service = new FriendAccessService(db.DbContext);

        Assert.Equal(FriendAccessStatus.NotFound, (await service.ResolveVisibleCollectionAsync(b, "Alice")).Status);
        Assert.Equal(FriendAccessStatus.NotFound, (await service.ResolveVisibleCollectionAsync(a, "Bob")).Status);
    }

    [Fact]
    public async Task Private_collection_reports_private_without_collection_id()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, _) = await db.AddUserAsync("Alice");
        var (b, _) = await db.AddUserAsync("Bob");
        await MakeFriendsAsync(db, a, b);
        var service = new FriendAccessService(db.DbContext);
        Assert.True(await service.SetOwnVisibilityAsync(a, CollectionVisibility.Private));

        var access = await service.ResolveVisibleCollectionAsync(b, "Alice");

        Assert.Equal(FriendAccessStatus.Private, access.Status);
        Assert.Null(access.CollectionId);
        // still friends
        Assert.True(await new FriendService(db.DbContext).AreFriendsAsync(a, b));
    }

    [Fact]
    public async Task Non_friend_of_private_collection_cannot_learn_it_is_private()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, _) = await db.AddUserAsync("Alice");
        var (c, _) = await db.AddUserAsync("Carol");
        var service = new FriendAccessService(db.DbContext);
        await service.SetOwnVisibilityAsync(a, CollectionVisibility.Private);

        Assert.Equal(FriendAccessStatus.NotFound, (await service.ResolveVisibleCollectionAsync(c, "Alice")).Status);
    }

    [Fact]
    public async Task Owner_can_switch_visibility_back_to_friends()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, _) = await db.AddUserAsync("Alice");
        var (b, _) = await db.AddUserAsync("Bob");
        await MakeFriendsAsync(db, a, b);
        var service = new FriendAccessService(db.DbContext);
        await service.SetOwnVisibilityAsync(a, CollectionVisibility.Private);
        await service.SetOwnVisibilityAsync(a, CollectionVisibility.Friends);

        Assert.Equal(CollectionVisibility.Friends, await service.GetOwnVisibilityAsync(a));
        Assert.Equal(FriendAccessStatus.Visible, (await service.ResolveVisibleCollectionAsync(b, "Alice")).Status);
        Assert.Equal(1, db.DbContext.CollectionSharingSettings.Count());
    }

    [Fact]
    public async Task Removed_friend_loses_access_immediately()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, _) = await db.AddUserAsync("Alice");
        var (b, _) = await db.AddUserAsync("Bob");
        await MakeFriendsAsync(db, a, b);
        var service = new FriendAccessService(db.DbContext);
        Assert.Equal(FriendAccessStatus.Visible, (await service.ResolveVisibleCollectionAsync(b, "Alice")).Status);

        await new FriendService(db.DbContext).RemoveAsync(a, b);

        Assert.Equal(FriendAccessStatus.NotFound, (await service.ResolveVisibleCollectionAsync(b, "Alice")).Status);
        Assert.Equal(FriendAccessStatus.NotFound, (await service.ResolveVisibleCollectionAsync(a, "Bob")).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nobody")]
    [InlineData("Alice' OR '1'='1")]
    [InlineData("%")]
    public async Task Forged_or_unknown_username_is_not_found(string? username)
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, _) = await db.AddUserAsync("Alice");
        var (b, _) = await db.AddUserAsync("Bob");
        await MakeFriendsAsync(db, a, b);
        var service = new FriendAccessService(db.DbContext);

        var access = await service.ResolveVisibleCollectionAsync(b, username);

        Assert.Equal(FriendAccessStatus.NotFound, access.Status);
        Assert.Null(access.CollectionId);
    }

    [Fact]
    public async Task Collection_id_passed_as_username_does_not_resolve()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, collectionA) = await db.AddUserAsync("Alice");
        var (b, _) = await db.AddUserAsync("Bob");
        await MakeFriendsAsync(db, a, b);
        var service = new FriendAccessService(db.DbContext);

        Assert.Equal(FriendAccessStatus.NotFound, (await service.ResolveVisibleCollectionAsync(b, collectionA)).Status);
        Assert.Equal(FriendAccessStatus.NotFound, (await service.ResolveVisibleCollectionAsync(b, a)).Status);
    }

    [Fact]
    public async Task Empty_viewer_id_and_own_username_are_not_found()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, _) = await db.AddUserAsync("Alice");
        var service = new FriendAccessService(db.DbContext);

        Assert.Equal(FriendAccessStatus.NotFound, (await service.ResolveVisibleCollectionAsync("", "Alice")).Status);
        Assert.Equal(FriendAccessStatus.NotFound, (await service.ResolveVisibleCollectionAsync(a, "Alice")).Status);
    }

    [Fact]
    public async Task Visibility_of_one_owner_does_not_affect_another()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, _) = await db.AddUserAsync("Alice");
        var (b, _) = await db.AddUserAsync("Bob");
        await MakeFriendsAsync(db, a, b);
        var service = new FriendAccessService(db.DbContext);
        await service.SetOwnVisibilityAsync(a, CollectionVisibility.Private);

        Assert.Equal(FriendAccessStatus.Visible, (await service.ResolveVisibleCollectionAsync(a, "Bob")).Status);
    }

    [Fact]
    public async Task Setting_visibility_for_user_without_collection_fails()
    {
        await using var db = await TestAppDb.CreateAsync();
        var service = new FriendAccessService(db.DbContext);

        Assert.False(await service.SetOwnVisibilityAsync("ghost", CollectionVisibility.Private));
    }

    [Fact]
    public async Task Reader_membership_does_not_allow_changing_visibility()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (_, collectionA) = await db.AddUserAsync("Alice");
        var (b, _) = await db.AddUserAsync("Bob");
        db.DbContext.CollectionMemberships.Add(new CollectionMembership
        {
            CollectionId = collectionA, UserId = b, Role = CollectionRole.Reader
        });
        await db.DbContext.SaveChangesAsync();
        var service = new FriendAccessService(db.DbContext);

        await service.SetOwnVisibilityAsync(b, CollectionVisibility.Private);

        // Bob's own collection changed; Alice's did not.
        Assert.Equal(CollectionVisibility.Friends, await service.GetVisibilityAsync(collectionA));
    }
}
