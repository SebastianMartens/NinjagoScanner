using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NinjagoScanner.PictureService;
using NinjagoScanner.PictureService.Protos;
using NinjagoScanner.PictureService.Services;
using NinjagoScanner.PictureService.Tests.Fixtures;

namespace NinjagoScanner.PictureService.Tests.Services;

public sealed class PictureScannerGrpcServiceDeletePhotoTests
{
    private static PictureScannerGrpcService CreateService(FakeSidecarStore sidecarStore, FakePhotoStore photoStore)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new PictureScannerGrpcService(
            configuration,
            NullLogger<PictureScannerGrpcService>.Instance,
            new SidecarCache(sidecarStore),
            photoStore);
    }

    [Fact]
    public async Task DeletePhoto_RemovesPhotoAndSidecar_WhenBothExist()
    {
        var photoStore = new FakePhotoStore();
        photoStore.Seed(TestCollection.Id, "card-1", [0xFF, 0xD8, 0xFF, 0xD9]);
        var sidecarStore = new FakeSidecarStore();
        sidecarStore.Tamper(TestCollection.Id, "card-1", new SidecarRecord { AnalysisStatus = "ok", CardName = "Kai" });

        var service = CreateService(sidecarStore, photoStore);

        var response = await service.DeletePhoto(
            new DeletePhotoRequest { PhotoId = "card-1", CollectionId = TestCollection.Id },
            new FakeServerCallContext());

        Assert.True(response.Success);
        Assert.False(await photoStore.ExistsAsync(TestCollection.Id, "card-1", CancellationToken.None));
        Assert.False(sidecarStore.ContainsKey(TestCollection.Id, "card-1"));
    }

    [Fact]
    public async Task DeletePhoto_RemovesPhoto_WhenNoSidecarExists()
    {
        var photoStore = new FakePhotoStore();
        photoStore.Seed(TestCollection.Id, "card-2", [0xFF, 0xD8, 0xFF, 0xD9]);

        var service = CreateService(new FakeSidecarStore(), photoStore);

        var response = await service.DeletePhoto(
            new DeletePhotoRequest { PhotoId = "card-2", CollectionId = TestCollection.Id },
            new FakeServerCallContext());

        Assert.True(response.Success);
        Assert.False(await photoStore.ExistsAsync(TestCollection.Id, "card-2", CancellationToken.None));
    }

    [Fact]
    public async Task DeletePhoto_FailsWithNotFound_WhenPhotoDoesNotExist()
    {
        var service = CreateService(new FakeSidecarStore(), new FakePhotoStore());

        var exception = await Assert.ThrowsAsync<RpcException>(() => service.DeletePhoto(
            new DeletePhotoRequest { PhotoId = "missing", CollectionId = TestCollection.Id },
            new FakeServerCallContext()));

        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task DeletePhoto_FailsWithNotFound_WhenPhotoExistsOnlyInDifferentCollection()
    {
        var photoStore = new FakePhotoStore();
        photoStore.Seed("other-collection", "card-1", [0xFF, 0xD8, 0xFF, 0xD9]);

        var service = CreateService(new FakeSidecarStore(), photoStore);

        var exception = await Assert.ThrowsAsync<RpcException>(() => service.DeletePhoto(
            new DeletePhotoRequest { PhotoId = "card-1", CollectionId = TestCollection.Id },
            new FakeServerCallContext()));

        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
        Assert.True(await photoStore.ExistsAsync("other-collection", "card-1", CancellationToken.None));
    }

    [Fact]
    public async Task DeletePhoto_DoesNotTouchOtherPhotos_WhenPhotoDoesNotExist()
    {
        var photoStore = new FakePhotoStore();
        photoStore.Seed(TestCollection.Id, "keep-me", [0xFF, 0xD8, 0xFF, 0xD9]);

        var service = CreateService(new FakeSidecarStore(), photoStore);

        await Assert.ThrowsAsync<RpcException>(() => service.DeletePhoto(
            new DeletePhotoRequest { PhotoId = "missing", CollectionId = TestCollection.Id },
            new FakeServerCallContext()));

        Assert.True(await photoStore.ExistsAsync(TestCollection.Id, "keep-me", CancellationToken.None));
    }

    [Fact]
    public async Task DeletePhoto_EvictsSidecarFromCache()
    {
        var photoStore = new FakePhotoStore();
        photoStore.Seed(TestCollection.Id, "card-3", [0xFF, 0xD8, 0xFF, 0xD9]);
        var sidecarStore = new FakeSidecarStore();
        sidecarStore.Tamper(TestCollection.Id, "card-3", new SidecarRecord { AnalysisStatus = "ok", CardName = "Nya" });

        var cache = new SidecarCache(sidecarStore);
        // Prime the cache the same way ListCards would, so a stale cached entry would survive
        // deletion if DeletePhoto forgot to evict it.
        var cachedBeforeDelete = await cache.GetAsync(TestCollection.Id, "card-3", CancellationToken.None);
        Assert.Equal("Nya", cachedBeforeDelete!.CardName);

        var configuration = new ConfigurationBuilder().Build();
        var service = new PictureScannerGrpcService(configuration, NullLogger<PictureScannerGrpcService>.Instance, cache, photoStore);
        await service.DeletePhoto(
            new DeletePhotoRequest { PhotoId = "card-3", CollectionId = TestCollection.Id },
            new FakeServerCallContext());

        // The record is gone from the store; a cache implementation that still serves the old
        // cached record would return it instead of null.
        var afterDelete = await cache.GetAsync(TestCollection.Id, "card-3", CancellationToken.None);
        Assert.Null(afterDelete);
    }
}
