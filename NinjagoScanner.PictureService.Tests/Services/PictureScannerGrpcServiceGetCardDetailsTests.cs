using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NinjagoScanner.PictureService;
using NinjagoScanner.PictureService.Protos;
using NinjagoScanner.PictureService.Services;
using NinjagoScanner.PictureService.Tests.Fixtures;

namespace NinjagoScanner.PictureService.Tests.Services;

/// <summary>
/// Covers the ListCards/GetCardDetails split: ListCards only carries the fields every list/grid
/// row needs, and GetCardDetails resolves the rest (confidence, reasoning, detected text,
/// scanned-at timestamp, error message) for one photo_id on demand.
/// </summary>
public sealed class PictureScannerGrpcServiceGetCardDetailsTests
{
    private static PictureScannerGrpcService CreateService(FakePhotoStore photoStore, FakeSidecarStore? sidecarStore = null)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new PictureScannerGrpcService(
            configuration,
            NullLogger<PictureScannerGrpcService>.Instance,
            new SidecarCache(sidecarStore ?? new FakeSidecarStore()),
            photoStore);
    }

    // CardEntry (ListCards) no longer has Confidence/ReasoningSummary/DetectedText/ScannedAtUtc/
    // ErrorMessage properties at all - the proto split is enforced at compile time. Rarity is the
    // one detail-ish field that stays, since the Gallery page needs it on every tile.
    [Fact]
    public async Task ListCards_KeepsRarity()
    {
        var photoStore = new FakePhotoStore();
        photoStore.Seed("card-1", [0xFF, 0xD8, 0xFF, 0xD9]);
        var sidecarStore = new FakeSidecarStore();
        sidecarStore.Tamper("card-1", new SidecarRecord
        {
            AnalysisStatus = "ok",
            Rarity = "Legendary",
            Confidence = 0.42
        });
        var service = CreateService(photoStore, sidecarStore);

        var response = await service.ListCards(new ListCardsRequest(), new FakeServerCallContext());

        var entry = Assert.Single(response.Cards);
        Assert.Equal("Legendary", entry.Rarity);
    }

    [Fact]
    public async Task GetCardDetails_ResolvesDetailFields_ForGivenPhotoId()
    {
        var photoStore = new FakePhotoStore();
        photoStore.Seed("card-1", [0xFF, 0xD8, 0xFF, 0xD9]);
        var sidecarStore = new FakeSidecarStore();
        var scannedAt = DateTimeOffset.Parse("2026-08-27T10:00:00Z");
        sidecarStore.Tamper("card-1", new SidecarRecord
        {
            AnalysisStatus = "ok",
            Rarity = "Legendary",
            Confidence = 0.42,
            ReasoningSummary = "Sieht eindeutig aus.",
            DetectedText = ["Foo", "Bar"],
            ScannedAtUtc = scannedAt,
            ErrorMessage = "kein Fehler"
        });
        var service = CreateService(photoStore, sidecarStore);

        var response = await service.GetCardDetails(new GetCardDetailsRequest { PhotoId = "card-1" }, new FakeServerCallContext());

        Assert.Equal("card-1", response.Details.PhotoId);
        Assert.Equal(0.42, response.Details.Confidence);
        Assert.Equal("Sieht eindeutig aus.", response.Details.ReasoningSummary);
        Assert.Equal(["Foo", "Bar"], response.Details.DetectedText);
        Assert.Equal(scannedAt.ToString("o"), response.Details.ScannedAtUtc);
        Assert.Equal("kein Fehler", response.Details.ErrorMessage);
    }

    [Fact]
    public async Task GetCardDetails_ReturnsDefaults_ForPhotoWithNoSidecarYet()
    {
        var service = CreateService(new FakePhotoStore());

        var response = await service.GetCardDetails(new GetCardDetailsRequest { PhotoId = "card-1" }, new FakeServerCallContext());

        Assert.Equal("card-1", response.Details.PhotoId);
        Assert.Equal(0, response.Details.Confidence);
        Assert.Empty(response.Details.DetectedText);
    }

    [Fact]
    public async Task GetCardDetails_FailsWithInvalidArgument_WhenPhotoIdMissing()
    {
        var service = CreateService(new FakePhotoStore());

        var exception = await Assert.ThrowsAsync<RpcException>(() => service.GetCardDetails(
            new GetCardDetailsRequest { PhotoId = "" },
            new FakeServerCallContext()));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }
}
