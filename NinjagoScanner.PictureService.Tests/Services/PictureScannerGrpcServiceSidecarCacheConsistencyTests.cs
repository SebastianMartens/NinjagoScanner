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
/// instance. Each test tampers with the record in the underlying store directly (bypassing the
/// cache) after writing through it, then asserts ListCards still returns the cached value rather
/// than the tampered-with store content — the only way that can happen is if the read is actually
/// served from the cache.
/// </summary>
public sealed class PictureScannerGrpcServiceSidecarCacheConsistencyTests
{
    private readonly FakeSidecarStore sidecarStore = new();
    private readonly FakePhotoStore photoStore = new();
    private readonly SidecarCache sidecarCache;

    public PictureScannerGrpcServiceSidecarCacheConsistencyTests()
    {
        sidecarCache = new SidecarCache(sidecarStore);
    }

    private PictureScannerGrpcService CreateService()
    {
        var configuration = new ConfigurationBuilder().Build();
        return new PictureScannerGrpcService(configuration, NullLogger<PictureScannerGrpcService>.Instance, sidecarCache, photoStore);
    }

    [Fact]
    public async Task UpdateReviewStatus_IsVisibleInListCards_EvenWhenStoreContentDivergesAfterward()
    {
        photoStore.Seed("card-1", [0x01]);

        // Two separate service instances, mirroring production where each RPC call gets a new
        // scoped PictureScannerGrpcService but shares the same singleton SidecarCache.
        var writer = CreateService();
        var reader = CreateService();

        await writer.UpdateReviewStatus(
            new UpdateReviewStatusRequest { PhotoId = "card-1", ReviewStatus = "verified" },
            new FakeServerCallContext());

        // Tamper with the store directly, bypassing the cache. If ListCards fell back to a fresh
        // store read, it would now report "incorrect" instead of "verified".
        sidecarStore.Tamper("card-1", new SidecarRecord { AnalysisStatus = "pending", ReviewStatus = "incorrect" });

        var response = await reader.ListCards(new ListCardsRequest(), new FakeServerCallContext());

        var entry = Assert.Single(response.Cards);
        Assert.Equal("verified", entry.ReviewStatus);
    }

    [Fact]
    public async Task UpdateSetName_ThenUpdateReviewStatus_BothReflectedInListCards_EvenWhenStoreContentDivergesAfterward()
    {
        photoStore.Seed("card-2", [0x01]);

        var writer = CreateService();
        var reader = CreateService();

        await writer.UpdateSetName(
            new UpdateSetNameRequest { PhotoId = "card-2", SetName = "Serie 9" },
            new FakeServerCallContext());

        await writer.UpdateReviewStatus(
            new UpdateReviewStatusRequest { PhotoId = "card-2", ReviewStatus = "verified" },
            new FakeServerCallContext());

        // Tamper with the store directly; only the cache can still answer correctly.
        sidecarStore.Tamper("card-2", new SidecarRecord { AnalysisStatus = "pending", ReviewStatus = "incorrect", SetName = "Serie 1" });

        var response = await reader.ListCards(new ListCardsRequest(), new FakeServerCallContext());

        var entry = Assert.Single(response.Cards);
        Assert.Equal("Serie 9", entry.SetName);
        Assert.Equal("verified", entry.ReviewStatus);
    }
}
