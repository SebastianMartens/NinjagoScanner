using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NinjagoScanner.PictureService;
using NinjagoScanner.PictureService.Protos;
using NinjagoScanner.PictureService.Services;
using NinjagoScanner.PictureService.Tests.Fixtures;

namespace NinjagoScanner.PictureService.Tests.Services;

public sealed class PictureScannerGrpcServiceUploadPhotoTests
{
    private static PictureScannerGrpcService CreateService(FakePhotoStore photoStore, IConfiguration? configuration = null)
    {
        return new PictureScannerGrpcService(
            configuration ?? new ConfigurationBuilder().Build(),
            NullLogger<PictureScannerGrpcService>.Instance,
            new SidecarCache(new FakeSidecarStore()),
            photoStore);
    }

    private static UploadPhotoRequest MetadataMessage(string sourceFileName) => new()
    {
        Metadata = new UploadPhotoMetadata { SourceFileName = sourceFileName }
    };

    private static UploadPhotoRequest ChunkMessage(params byte[] bytes) => new()
    {
        Chunk = ByteString.CopyFrom(bytes)
    };

    [Fact]
    public async Task UploadPhoto_RejectsStream_WhenFirstMessageIsNotMetadata()
    {
        var service = CreateService(new FakePhotoStore());
        var requestStream = new FakeAsyncStreamReader<UploadPhotoRequest>([ChunkMessage(1, 2, 3)]);

        var exception = await Assert.ThrowsAsync<RpcException>(() =>
            service.UploadPhoto(requestStream, new FakeServerCallContext()));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Fact]
    public async Task UploadPhoto_RejectsStream_WhenEmptyStream()
    {
        var service = CreateService(new FakePhotoStore());
        var requestStream = new FakeAsyncStreamReader<UploadPhotoRequest>([]);

        var exception = await Assert.ThrowsAsync<RpcException>(() =>
            service.UploadPhoto(requestStream, new FakeServerCallContext()));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Fact]
    public async Task UploadPhoto_RejectsUnsupportedFileType()
    {
        var service = CreateService(new FakePhotoStore());
        var requestStream = new FakeAsyncStreamReader<UploadPhotoRequest>([
            MetadataMessage("card.txt"),
            ChunkMessage(1, 2, 3)
        ]);

        var exception = await Assert.ThrowsAsync<RpcException>(() =>
            service.UploadPhoto(requestStream, new FakeServerCallContext()));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Fact]
    public async Task UploadPhoto_RejectsEmptyFileContent()
    {
        var service = CreateService(new FakePhotoStore());
        var requestStream = new FakeAsyncStreamReader<UploadPhotoRequest>([MetadataMessage("card.jpg")]);

        var exception = await Assert.ThrowsAsync<RpcException>(() =>
            service.UploadPhoto(requestStream, new FakeServerCallContext()));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Fact]
    public async Task UploadPhoto_StoresBytesUnderGeneratedId_BeforeRunningAnalysis()
    {
        // Analysis itself needs a reachable Gemini API/CatalogService, which this unit test can't
        // provide — whatever config the environment supplies (or lacks), the RPC is expected to
        // fail past this point. What this test verifies is that storage happens first regardless.
        var photoStore = new FakePhotoStore();
        var service = CreateService(photoStore);
        var requestStream = new FakeAsyncStreamReader<UploadPhotoRequest>([
            MetadataMessage("card.jpg"),
            ChunkMessage(0xFF, 0xD8, 0xFF, 0xD9)
        ]);

        await Assert.ThrowsAsync<RpcException>(() =>
            service.UploadPhoto(requestStream, new FakeServerCallContext()));

        var storedIds = new List<string>();
        await foreach (var id in photoStore.ListPhotoIdsAsync(CancellationToken.None))
        {
            storedIds.Add(id);
        }

        var storedId = Assert.Single(storedIds);
        Assert.Equal([0xFF, 0xD8, 0xFF, 0xD9], await photoStore.GetBytesAsync(storedId, CancellationToken.None));
    }

    [Fact]
    public async Task UploadPhoto_AssignsDistinctIds_ForSameSourceFileName()
    {
        var photoStore1 = new FakePhotoStore();
        var photoStore2 = new FakePhotoStore();
        var service1 = CreateService(photoStore1);
        var service2 = CreateService(photoStore2);

        var stream1 = new FakeAsyncStreamReader<UploadPhotoRequest>([MetadataMessage("same.jpg"), ChunkMessage(1)]);
        var stream2 = new FakeAsyncStreamReader<UploadPhotoRequest>([MetadataMessage("same.jpg"), ChunkMessage(1)]);

        await Assert.ThrowsAsync<RpcException>(() => service1.UploadPhoto(stream1, new FakeServerCallContext()));
        await Assert.ThrowsAsync<RpcException>(() => service2.UploadPhoto(stream2, new FakeServerCallContext()));

        var ids1 = new List<string>();
        await foreach (var id in photoStore1.ListPhotoIdsAsync(CancellationToken.None))
        {
            ids1.Add(id);
        }

        var ids2 = new List<string>();
        await foreach (var id in photoStore2.ListPhotoIdsAsync(CancellationToken.None))
        {
            ids2.Add(id);
        }

        Assert.Single(ids1);
        Assert.Single(ids2);
        Assert.NotEqual(ids1[0], ids2[0]);
    }
}
