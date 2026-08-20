using NinjagoScanner.PictureService;
using NinjagoScanner.PictureService.Tests.Fixtures;

namespace NinjagoScanner.PictureService.Tests;

public sealed class SidecarCacheTests
{
    [Fact]
    public async Task GetAsync_ReadsFromStore_OnFirstAccess()
    {
        var store = new FakeSidecarStore();
        store.Tamper("card-1", new SidecarRecord { AnalysisStatus = "ok", CardName = "Kai" });

        var cache = new SidecarCache(store);
        var record = await cache.GetAsync("card-1", CancellationToken.None);

        Assert.NotNull(record);
        Assert.Equal("ok", record!.AnalysisStatus);
        Assert.Equal("Kai", record.CardName);
    }

    [Fact]
    public async Task GetAsync_ServesFromCache_WithoutRereadingStore()
    {
        var store = new FakeSidecarStore();
        store.Tamper("card-2", new SidecarRecord { AnalysisStatus = "ok", CardName = "Kai" });

        var cache = new SidecarCache(store);
        var first = await cache.GetAsync("card-2", CancellationToken.None);

        // Change the record directly in the store, bypassing the cache, to prove a second read
        // doesn't go back to the store.
        store.Tamper("card-2", new SidecarRecord { AnalysisStatus = "ok", CardName = "Zane" });

        var second = await cache.GetAsync("card-2", CancellationToken.None);

        Assert.Equal("Kai", first!.CardName);
        Assert.Equal("Kai", second!.CardName);
    }

    [Fact]
    public async Task SetAsync_PopulatesCache_WithoutRequiringAStoreReadToServeIt()
    {
        var store = new FakeSidecarStore();
        var cache = new SidecarCache(store);

        var record = new SidecarRecord { AnalysisStatus = "ok", CardName = "Lloyd" };
        await cache.SetAsync("card-3", record, CancellationToken.None);

        // Remove the record from the store; a cache implementation that re-reads on every
        // call would now return nothing here.
        await store.DeleteAsync("card-3", CancellationToken.None);

        var cached = await cache.GetAsync("card-3", CancellationToken.None);

        Assert.NotNull(cached);
        Assert.Equal("Lloyd", cached!.CardName);
    }

    [Fact]
    public async Task GetAsync_DoesNotCacheReadFailures_AndRetriesOnNextRead()
    {
        var store = new FakeSidecarStore();
        store.FailNextReadFor("card-4");

        var cache = new SidecarCache(store);
        await Assert.ThrowsAnyAsync<Exception>(() => cache.GetAsync("card-4", CancellationToken.None));

        // Fix the store; a correctly-behaving cache must retry rather than remember the failure.
        store.Tamper("card-4", new SidecarRecord { AnalysisStatus = "ok", CardName = "Nya" });

        var record = await cache.GetAsync("card-4", CancellationToken.None);

        Assert.NotNull(record);
        Assert.Equal("Nya", record!.CardName);
    }
}
