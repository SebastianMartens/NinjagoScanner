using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NinjagoScanner.PictureService;
using NinjagoScanner.PictureService.Protos;
using NinjagoScanner.PictureService.Services;
using NinjagoScanner.PictureService.Tests.Fixtures;

namespace NinjagoScanner.PictureService.Tests.Services;

public sealed class PictureScannerGrpcServiceUpdateReviewStatusTests
{
    private static PictureScannerGrpcService CreateService(FakeSidecarStore? store = null, FakePhotoStore? photoStore = null)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new PictureScannerGrpcService(
            configuration,
            NullLogger<PictureScannerGrpcService>.Instance,
            new SidecarCache(store ?? new FakeSidecarStore()),
            photoStore ?? new FakePhotoStore());
    }

    [Fact]
    public async Task UpdateReviewStatus_CreatesPendingSidecar_WhenNoneExists()
    {
        var store = new FakeSidecarStore();
        var service = CreateService(store);

        await service.UpdateReviewStatus(
            new UpdateReviewStatusRequest { PhotoId = "card-1", ReviewStatus = "verified" },
            new FakeServerCallContext());

        var record = await store.GetAsync("card-1", CancellationToken.None);
        Assert.Equal("pending", record!.AnalysisStatus);
        Assert.Equal("verified", record.ReviewStatus);
    }

    [Fact]
    public async Task UpdateReviewStatus_OnlyChangesReviewStatus_OnExistingSidecar()
    {
        var store = new FakeSidecarStore();
        store.Tamper("card-2", new SidecarRecord
        {
            AnalysisStatus = "ok",
            CardName = "Kai",
            CardNumber = "43",
            SetName = "Serie 9",
            Rarity = "Common",
            Confidence = 0.95,
            ReviewStatus = "unreviewed"
        });

        var service = CreateService(store);

        await service.UpdateReviewStatus(
            new UpdateReviewStatusRequest { PhotoId = "card-2", ReviewStatus = "incorrect" },
            new FakeServerCallContext());

        var record = await store.GetAsync("card-2", CancellationToken.None);
        Assert.Equal("incorrect", record!.ReviewStatus);
        Assert.Equal("ok", record.AnalysisStatus);
        Assert.Equal("Kai", record.CardName);
        Assert.Equal("43", record.CardNumber);
        Assert.Equal("Serie 9", record.SetName);
        Assert.Equal("Common", record.Rarity);
        Assert.Equal(0.95, record.Confidence);
    }
}
