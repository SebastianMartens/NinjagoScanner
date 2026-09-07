using NinjagoScanner.PictureService;
using NinjagoScanner.PictureService.Tests.Fixtures;

namespace NinjagoScanner.PictureService.Tests;

public sealed class SidecarCacheTests
{
    private const string CollectionId = TestCollection.Id;
    private const string OtherCollectionId = "other-collection";

    [Fact]
    public async Task GetAsync_ReadsFromStore_OnFirstAccess()
    {
        var store = new FakeSidecarStore();
        store.Tamper(CollectionId, "card-1", new SidecarRecord { AnalysisStatus = "ok", CardName = "Kai" });

        var cache = new SidecarCache(store);
        var record = await cache.GetAsync(CollectionId, "card-1", CancellationToken.None);

        Assert.NotNull(record);
        Assert.Equal("ok", record!.AnalysisStatus);
        Assert.Equal("Kai", record.CardName);
    }

    [Fact]
    public async Task GetAsync_ServesFromCache_WithoutRereadingStore()
    {
        var store = new FakeSidecarStore();
        store.Tamper(CollectionId, "card-2", new SidecarRecord { AnalysisStatus = "ok", CardName = "Kai" });

        var cache = new SidecarCache(store);
        var first = await cache.GetAsync(CollectionId, "card-2", CancellationToken.None);

        // Change the record directly in the store, bypassing the cache, to prove a second read
        // doesn't go back to the store.
        store.Tamper(CollectionId, "card-2", new SidecarRecord { AnalysisStatus = "ok", CardName = "Zane" });

        var second = await cache.GetAsync(CollectionId, "card-2", CancellationToken.None);

        Assert.Equal("Kai", first!.CardName);
        Assert.Equal("Kai", second!.CardName);
    }

    [Fact]
    public async Task SetAsync_PopulatesCache_WithoutRequiringAStoreReadToServeIt()
    {
        var store = new FakeSidecarStore();
        var cache = new SidecarCache(store);

        var record = new SidecarRecord { AnalysisStatus = "ok", CardName = "Lloyd" };
        await cache.SetAsync(CollectionId, "card-3", record, CancellationToken.None);

        // Remove the record from the store; a cache implementation that re-reads on every
        // call would now return nothing here.
        await store.DeleteAsync(CollectionId, "card-3", CancellationToken.None);

        var cached = await cache.GetAsync(CollectionId, "card-3", CancellationToken.None);

        Assert.NotNull(cached);
        Assert.Equal("Lloyd", cached!.CardName);
    }

    [Fact]
    public async Task GetAsync_DoesNotCacheReadFailures_AndRetriesOnNextRead()
    {
        var store = new FakeSidecarStore();
        store.FailNextReadFor(CollectionId, "card-4");

        var cache = new SidecarCache(store);
        await Assert.ThrowsAnyAsync<Exception>(() => cache.GetAsync(CollectionId, "card-4", CancellationToken.None));

        // Fix the store; a correctly-behaving cache must retry rather than remember the failure.
        store.Tamper(CollectionId, "card-4", new SidecarRecord { AnalysisStatus = "ok", CardName = "Nya" });

        var record = await cache.GetAsync(CollectionId, "card-4", CancellationToken.None);

        Assert.NotNull(record);
        Assert.Equal("Nya", record!.CardName);
    }

    [Fact]
    public async Task GetAsync_CachesSamePhotoIdInDifferentCollectionsIndependently()
    {
        var store = new FakeSidecarStore();
        store.Tamper(CollectionId, "card-5", new SidecarRecord { AnalysisStatus = "ok", CardName = "Kai" });
        store.Tamper(OtherCollectionId, "card-5", new SidecarRecord { AnalysisStatus = "ok", CardName = "Zane" });

        var cache = new SidecarCache(store);
        var fromFirstCollection = await cache.GetAsync(CollectionId, "card-5", CancellationToken.None);
        var fromOtherCollection = await cache.GetAsync(OtherCollectionId, "card-5", CancellationToken.None);

        Assert.Equal("Kai", fromFirstCollection!.CardName);
        Assert.Equal("Zane", fromOtherCollection!.CardName);
    }
}
