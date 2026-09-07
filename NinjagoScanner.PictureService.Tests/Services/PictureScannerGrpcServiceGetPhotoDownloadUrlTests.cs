using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NinjagoScanner.PictureService;
using NinjagoScanner.PictureService.Protos;
using NinjagoScanner.PictureService.Services;
using NinjagoScanner.PictureService.Tests.Fixtures;

namespace NinjagoScanner.PictureService.Tests.Services;

public sealed class PictureScannerGrpcServiceGetPhotoDownloadUrlTests
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

    [Fact]
    public async Task GetPhotoDownloadUrl_ReturnsUrl_WhenPhotoExists()
    {
        var photoStore = new FakePhotoStore();
        photoStore.Seed(TestCollection.Id, "card-1", [0xFF, 0xD8, 0xFF, 0xD9]);
        var service = CreateService(photoStore);

        var response = await service.GetPhotoDownloadUrl(
            new GetPhotoDownloadUrlRequest { PhotoId = "card-1", CollectionId = TestCollection.Id },
            new FakeServerCallContext());

        Assert.False(string.IsNullOrWhiteSpace(response.DownloadUrl));
    }

    [Fact]
    public async Task GetPhotoDownloadUrl_FailsWithNotFound_WhenPhotoDoesNotExist()
    {
        var service = CreateService(new FakePhotoStore());

        var exception = await Assert.ThrowsAsync<RpcException>(() => service.GetPhotoDownloadUrl(
            new GetPhotoDownloadUrlRequest { PhotoId = "missing", CollectionId = TestCollection.Id },
            new FakeServerCallContext()));

        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task GetPhotoDownloadUrl_FailsWithInvalidArgument_WhenPhotoIdMissing()
    {
        var service = CreateService(new FakePhotoStore());

        var exception = await Assert.ThrowsAsync<RpcException>(() => service.GetPhotoDownloadUrl(
            new GetPhotoDownloadUrlRequest { PhotoId = "", CollectionId = TestCollection.Id },
            new FakeServerCallContext()));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Fact]
    public async Task GetPhotoDownloadUrl_FailsWithInvalidArgument_WhenCollectionIdMissing()
    {
        var photoStore = new FakePhotoStore();
        photoStore.Seed(TestCollection.Id, "card-1", [0xFF, 0xD8, 0xFF, 0xD9]);
        var service = CreateService(photoStore);

        var exception = await Assert.ThrowsAsync<RpcException>(() => service.GetPhotoDownloadUrl(
            new GetPhotoDownloadUrlRequest { PhotoId = "card-1", CollectionId = "" },
            new FakeServerCallContext()));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Fact]
    public async Task GetPhotoDownloadUrl_FailsWithNotFound_WhenPhotoExistsOnlyInDifferentCollection()
    {
        var photoStore = new FakePhotoStore();
        photoStore.Seed(TestCollection.Id, "card-1", [0xFF, 0xD8, 0xFF, 0xD9]);
        var service = CreateService(photoStore);

        var exception = await Assert.ThrowsAsync<RpcException>(() => service.GetPhotoDownloadUrl(
            new GetPhotoDownloadUrlRequest { PhotoId = "card-1", CollectionId = "other-collection" },
            new FakeServerCallContext()));

        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task ListCards_IncludesDownloadUrl_ForEveryEntry()
    {
        var photoStore = new FakePhotoStore();
        photoStore.Seed(TestCollection.Id, "card-1", [0xFF, 0xD8, 0xFF, 0xD9]);
        photoStore.Seed(TestCollection.Id, "card-2", [0xFF, 0xD8, 0xFF, 0xD9]);
        var service = CreateService(photoStore);

        var response = await service.ListCards(new ListCardsRequest { CollectionId = TestCollection.Id }, new FakeServerCallContext());

        Assert.Equal(2, response.Cards.Count);
        Assert.All(response.Cards, card => Assert.False(string.IsNullOrWhiteSpace(card.DownloadUrl)));
    }

    [Fact]
    public async Task ListCards_IncludesDownloadUrl_ForPhotoWithNoSidecarYet()
    {
        var photoStore = new FakePhotoStore();
        photoStore.Seed(TestCollection.Id, "card-1", [0xFF, 0xD8, 0xFF, 0xD9]);
        var service = CreateService(photoStore);

        var response = await service.ListCards(new ListCardsRequest { CollectionId = TestCollection.Id }, new FakeServerCallContext());

        var entry = Assert.Single(response.Cards);
        Assert.Equal("notAnalyzed", entry.AnalysisStatus);
        Assert.False(string.IsNullOrWhiteSpace(entry.DownloadUrl));
    }

    [Fact]
    public async Task ListCards_ReportsNotAnalyzed_ForLegacyOrUnrecognizedStoredStatus()
    {
        var photoStore = new FakePhotoStore();
        photoStore.Seed(TestCollection.Id, "card-1", [0xFF, 0xD8, 0xFF, 0xD9]);
        photoStore.Seed(TestCollection.Id, "card-2", [0xFF, 0xD8, 0xFF, 0xD9]);
        var sidecarStore = new FakeSidecarStore();
        sidecarStore.Tamper(TestCollection.Id, "card-1", new SidecarRecord { AnalysisStatus = "pending" });
        sidecarStore.Tamper(TestCollection.Id, "card-2", new SidecarRecord { AnalysisStatus = "some-unexpected-value" });
        var service = CreateService(photoStore, sidecarStore);

        var response = await service.ListCards(new ListCardsRequest { CollectionId = TestCollection.Id }, new FakeServerCallContext());

        Assert.Equal(2, response.Cards.Count);
        Assert.All(response.Cards, card => Assert.Equal("notAnalyzed", card.AnalysisStatus));
    }

    [Fact]
    public async Task ListCards_ResolvesManyPhotos_ViaBulkReadsRatherThanOnePerPhoto()
    {
        var photoStore = new FakePhotoStore();
        var sidecarStore = new FakeSidecarStore();
        for (var i = 0; i < 250; i++)
        {
            var photoId = $"card-{i}";
            photoStore.Seed(TestCollection.Id, photoId, [0xFF, 0xD8, 0xFF, 0xD9]);
            sidecarStore.Tamper(TestCollection.Id, photoId, new SidecarRecord { AnalysisStatus = "ok", ReviewStatus = "unreviewed" });
        }

        var service = CreateService(photoStore, sidecarStore);

        var response = await service.ListCards(new ListCardsRequest { CollectionId = TestCollection.Id }, new FakeServerCallContext());

        // One bulk query of the store (ListByCollectionAsync) resolves every entry's sidecar
        // data, so GetAsync's per-photo fallback path is never exercised for a store call.
        Assert.Equal(1, sidecarStore.ListByCollectionAsyncCallCount);
        Assert.Equal(0, sidecarStore.GetAsyncCallCount);
        Assert.Equal(250, response.Cards.Count);
        Assert.All(response.Cards, card => Assert.Equal("ok", card.AnalysisStatus));
    }

    [Fact]
    public async Task ListCards_ExcludesPhotosFromAnotherCollection()
    {
        var photoStore = new FakePhotoStore();
        photoStore.Seed(TestCollection.Id, "card-1", [0xFF, 0xD8, 0xFF, 0xD9]);
        photoStore.Seed("other-collection", "card-2", [0xFF, 0xD8, 0xFF, 0xD9]);
        var service = CreateService(photoStore);

        var response = await service.ListCards(new ListCardsRequest { CollectionId = TestCollection.Id }, new FakeServerCallContext());

        var entry = Assert.Single(response.Cards);
        Assert.Equal("card-1", entry.PhotoId);
    }
}
