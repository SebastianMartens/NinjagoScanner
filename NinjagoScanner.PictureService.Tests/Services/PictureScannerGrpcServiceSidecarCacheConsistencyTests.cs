using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NinjagoScanner.PictureService;
using NinjagoScanner.PictureService.Protos;
using NinjagoScanner.PictureService.Services;
using NinjagoScanner.PictureService.Tests.Fixtures;

namespace NinjagoScanner.PictureService.Tests.Services;

/// <summary>
/// Verifies that a value written by one RPC is immediately visible to a subsequent
/// <see cref="PictureScannerGrpcService.ListCards"/> call served by the same <see cref="SidecarCache"/>
/// instance. Each test tampers with the sidecar file's on-disk content (without deleting it, since
/// the "does a sidecar exist" check is intentionally not cached) after writing through the cache, then
/// asserts ListCards still returns the cached value rather than the tampered-with disk content - the
/// only way that can happen is if the read is actually served from the cache.
/// </summary>
public sealed class PictureScannerGrpcServiceSidecarCacheConsistencyTests : IDisposable
{
    private readonly string cardPhotosDirectory = Path.Combine(
        Path.GetTempPath(),
        $"NinjagoScannerPictureServiceSidecarCacheTests_{Guid.NewGuid():N}");

    private readonly SidecarCache sidecarCache = new();

    public PictureScannerGrpcServiceSidecarCacheConsistencyTests()
    {
        Directory.CreateDirectory(cardPhotosDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(cardPhotosDirectory))
        {
            Directory.Delete(cardPhotosDirectory, recursive: true);
        }
    }

    private PictureScannerGrpcService CreateService()
    {
        var configuration = new ConfigurationBuilder().Build();
        return new PictureScannerGrpcService(configuration, NullLogger<PictureScannerGrpcService>.Instance, sidecarCache);
    }

    private static void TamperSidecarOnDisk(string sidecarPath, string reviewStatus, string setName)
    {
        File.WriteAllText(sidecarPath, $$"""
        {
          "AnalysisStatus": "pending",
          "ReviewStatus": "{{reviewStatus}}",
          "SetName": "{{setName}}"
        }
        """);
    }

    [Fact]
    public async Task UpdateReviewStatus_IsVisibleInListCards_EvenWhenDiskContentDivergesAfterward()
    {
        var imagePath = Path.Combine(cardPhotosDirectory, "card-1.jpg");
        await File.WriteAllBytesAsync(imagePath, [0x01]);

        // Two separate service instances, mirroring production where each RPC call gets a new
        // scoped PictureScannerGrpcService but shares the same singleton SidecarCache.
        var writer = CreateService();
        var reader = CreateService();

        await writer.UpdateReviewStatus(
            new UpdateReviewStatusRequest
            {
                ImageFileName = "card-1.jpg",
                CardPhotosDirectory = cardPhotosDirectory,
                ReviewStatus = "verified"
            },
            new FakeServerCallContext());

        // Tamper with the sidecar file directly on disk, bypassing the cache. If ListCards
        // fell back to a fresh disk read, it would now report "incorrect" instead of "verified".
        TamperSidecarOnDisk(Path.Combine(cardPhotosDirectory, "card-1.jpg.json"), reviewStatus: "incorrect", setName: string.Empty);

        var response = await reader.ListCards(
            new ListCardsRequest { CardPhotosDirectory = cardPhotosDirectory },
            new FakeServerCallContext());

        var entry = Assert.Single(response.Cards);
        Assert.Equal("verified", entry.ReviewStatus);
    }

    [Fact]
    public async Task UpdateSetName_ThenUpdateReviewStatus_BothReflectedInListCards_EvenWhenDiskContentDivergesAfterward()
    {
        var imagePath = Path.Combine(cardPhotosDirectory, "card-2.jpg");
        await File.WriteAllBytesAsync(imagePath, [0x01]);

        var writer = CreateService();
        var reader = CreateService();

        await writer.UpdateSetName(
            new UpdateSetNameRequest
            {
                ImageFileName = "card-2.jpg",
                CardPhotosDirectory = cardPhotosDirectory,
                SetName = "Serie 9"
            },
            new FakeServerCallContext());

        await writer.UpdateReviewStatus(
            new UpdateReviewStatusRequest
            {
                ImageFileName = "card-2.jpg",
                CardPhotosDirectory = cardPhotosDirectory,
                ReviewStatus = "verified"
            },
            new FakeServerCallContext());

        // Tamper with the sidecar file both writes updated; only the cache can still answer correctly.
        TamperSidecarOnDisk(Path.Combine(cardPhotosDirectory, "card-2.jpg.json"), reviewStatus: "incorrect", setName: "Serie 1");

        var response = await reader.ListCards(
            new ListCardsRequest { CardPhotosDirectory = cardPhotosDirectory },
            new FakeServerCallContext());

        var entry = Assert.Single(response.Cards);
        Assert.Equal("Serie 9", entry.SetName);
        Assert.Equal("verified", entry.ReviewStatus);
    }
}
