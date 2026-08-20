using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NinjagoScanner.PictureService;
using NinjagoScanner.PictureService.Protos;
using NinjagoScanner.PictureService.Services;
using NinjagoScanner.PictureService.Tests.Fixtures;

namespace NinjagoScanner.PictureService.Tests.Services;

public sealed class PictureScannerGrpcServiceUpdateCardNumberTests
{
    private static PictureScannerGrpcService CreateService(FakeSidecarStore? store = null)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new PictureScannerGrpcService(
            configuration,
            NullLogger<PictureScannerGrpcService>.Instance,
            new SidecarCache(store ?? new FakeSidecarStore()),
            new FakePhotoStore());
    }

    [Fact]
    public async Task UpdateCardNumber_CreatesPendingSidecar_WhenNoneExists()
    {
        var store = new FakeSidecarStore();
        var service = CreateService(store);

        await service.UpdateCardNumber(
            new UpdateCardNumberRequest { PhotoId = "card-1", CardNumber = "17" },
            new FakeServerCallContext());

        var record = await store.GetAsync("card-1", CancellationToken.None);
        Assert.Equal("pending", record!.AnalysisStatus);
        Assert.Equal("17", record.CardNumber);
    }

    [Fact]
    public async Task UpdateCardNumber_OnlyChangesCardNumber_OnExistingSidecar()
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
            ReviewStatus = "verified"
        });

        var service = CreateService(store);

        await service.UpdateCardNumber(
            new UpdateCardNumberRequest { PhotoId = "card-2", CardNumber = "44" },
            new FakeServerCallContext());

        var record = await store.GetAsync("card-2", CancellationToken.None);
        Assert.Equal("44", record!.CardNumber);
        Assert.Equal("ok", record.AnalysisStatus);
        Assert.Equal("Kai", record.CardName);
        Assert.Equal("Serie 9", record.SetName);
        Assert.Equal("Common", record.Rarity);
        Assert.Equal(0.95, record.Confidence);
        Assert.Equal("verified", record.ReviewStatus);
    }

    [Fact]
    public async Task UpdateCardNumber_NormalizesBlankInput_ToAbsent()
    {
        var store = new FakeSidecarStore();
        store.Tamper("card-3", new SidecarRecord { AnalysisStatus = "ok", CardNumber = "43", SetName = "Serie 9" });

        var service = CreateService(store);

        await service.UpdateCardNumber(
            new UpdateCardNumberRequest { PhotoId = "card-3", CardNumber = "   " },
            new FakeServerCallContext());

        var record = await store.GetAsync("card-3", CancellationToken.None);
        Assert.Null(record!.CardNumber);
    }
}
