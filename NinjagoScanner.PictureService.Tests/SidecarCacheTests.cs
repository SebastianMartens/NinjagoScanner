using NinjagoScanner.PictureService;

namespace NinjagoScanner.PictureService.Tests;

public sealed class SidecarCacheTests : IDisposable
{
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"NinjagoScannerSidecarCacheTests_{Guid.NewGuid():N}");

    public SidecarCacheTests()
    {
        Directory.CreateDirectory(testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private string SidecarPath(string name) => Path.Combine(testDirectory, name);

    [Fact]
    public async Task GetAsync_ReadsFromDisk_OnFirstAccess()
    {
        var sidecarPath = SidecarPath("card-1.jpg.json");
        await File.WriteAllTextAsync(sidecarPath, """{ "AnalysisStatus": "ok", "CardName": "Kai" }""");

        var cache = new SidecarCache();
        var record = await cache.GetAsync(sidecarPath, CancellationToken.None);

        Assert.NotNull(record);
        Assert.Equal("ok", record!.AnalysisStatus);
        Assert.Equal("Kai", record.CardName);
    }

    [Fact]
    public async Task GetAsync_ServesFromCache_WithoutRereadingDisk()
    {
        var sidecarPath = SidecarPath("card-2.jpg.json");
        await File.WriteAllTextAsync(sidecarPath, """{ "AnalysisStatus": "ok", "CardName": "Kai" }""");

        var cache = new SidecarCache();
        var first = await cache.GetAsync(sidecarPath, CancellationToken.None);

        // Change the file on disk directly, bypassing the cache, to prove a second read
        // doesn't go back to disk.
        await File.WriteAllTextAsync(sidecarPath, """{ "AnalysisStatus": "ok", "CardName": "Zane" }""");

        var second = await cache.GetAsync(sidecarPath, CancellationToken.None);

        Assert.Equal("Kai", first!.CardName);
        Assert.Equal("Kai", second!.CardName);
    }

    [Fact]
    public async Task SetAsync_PopulatesCache_WithoutRequiringADiskReadToServeIt()
    {
        var sidecarPath = SidecarPath("card-3.jpg.json");
        var cache = new SidecarCache();

        var record = new SidecarRecord { AnalysisStatus = "ok", CardName = "Lloyd" };
        await cache.SetAsync(sidecarPath, record, CancellationToken.None);

        // Remove the file from disk; a cache implementation that re-reads on every
        // call would now fail or return nothing here.
        File.Delete(sidecarPath);

        var cached = await cache.GetAsync(sidecarPath, CancellationToken.None);

        Assert.NotNull(cached);
        Assert.Equal("Lloyd", cached!.CardName);
    }

    [Fact]
    public async Task GetAsync_DoesNotCacheReadFailures_AndRetriesOnNextRead()
    {
        var sidecarPath = SidecarPath("card-4.jpg.json");
        await File.WriteAllTextAsync(sidecarPath, "{ not valid json");

        var cache = new SidecarCache();
        await Assert.ThrowsAnyAsync<Exception>(() => cache.GetAsync(sidecarPath, CancellationToken.None));

        // Fix the file; a correctly-behaving cache must retry disk rather than remember the failure.
        await File.WriteAllTextAsync(sidecarPath, """{ "AnalysisStatus": "ok", "CardName": "Nya" }""");

        var record = await cache.GetAsync(sidecarPath, CancellationToken.None);

        Assert.NotNull(record);
        Assert.Equal("Nya", record!.CardName);
    }
}
