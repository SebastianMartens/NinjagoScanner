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
    private static PictureScannerGrpcService CreateService(FakePhotoStore photoStore)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new PictureScannerGrpcService(
            configuration,
            NullLogger<PictureScannerGrpcService>.Instance,
            new SidecarCache(new FakeSidecarStore()),
            photoStore);
    }

    [Fact]
    public async Task GetPhotoDownloadUrl_ReturnsUrl_WhenPhotoExists()
    {
        var photoStore = new FakePhotoStore();
        photoStore.Seed("card-1", [0xFF, 0xD8, 0xFF, 0xD9]);
        var service = CreateService(photoStore);

        var response = await service.GetPhotoDownloadUrl(
            new GetPhotoDownloadUrlRequest { PhotoId = "card-1" },
            new FakeServerCallContext());

        Assert.False(string.IsNullOrWhiteSpace(response.DownloadUrl));
    }

    [Fact]
    public async Task GetPhotoDownloadUrl_FailsWithNotFound_WhenPhotoDoesNotExist()
    {
        var service = CreateService(new FakePhotoStore());

        var exception = await Assert.ThrowsAsync<RpcException>(() => service.GetPhotoDownloadUrl(
            new GetPhotoDownloadUrlRequest { PhotoId = "missing" },
            new FakeServerCallContext()));

        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task GetPhotoDownloadUrl_FailsWithInvalidArgument_WhenPhotoIdMissing()
    {
        var service = CreateService(new FakePhotoStore());

        var exception = await Assert.ThrowsAsync<RpcException>(() => service.GetPhotoDownloadUrl(
            new GetPhotoDownloadUrlRequest { PhotoId = "" },
            new FakeServerCallContext()));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Fact]
    public async Task GetPhotoDownloadUrls_ReturnsUrlForEveryExistingPhoto()
    {
        var photoStore = new FakePhotoStore();
        photoStore.Seed("card-1", [0xFF, 0xD8, 0xFF, 0xD9]);
        photoStore.Seed("card-2", [0xFF, 0xD8, 0xFF, 0xD9]);
        var service = CreateService(photoStore);

        var request = new GetPhotoDownloadUrlsRequest();
        request.PhotoIds.AddRange(["card-1", "card-2"]);

        var response = await service.GetPhotoDownloadUrls(request, new FakeServerCallContext());

        Assert.Equal(2, response.DownloadUrlsByPhotoId.Count);
        Assert.False(string.IsNullOrWhiteSpace(response.DownloadUrlsByPhotoId["card-1"]));
        Assert.False(string.IsNullOrWhiteSpace(response.DownloadUrlsByPhotoId["card-2"]));
    }

    [Fact]
    public async Task GetPhotoDownloadUrls_ReturnsEmptyResponse_WhenRequestListIsEmpty()
    {
        var service = CreateService(new FakePhotoStore());

        var response = await service.GetPhotoDownloadUrls(new GetPhotoDownloadUrlsRequest(), new FakeServerCallContext());

        Assert.Empty(response.DownloadUrlsByPhotoId);
    }

    [Fact]
    public async Task GetPhotoDownloadUrls_OmitsMissingPhoto_ButReturnsTheRest()
    {
        var photoStore = new FakePhotoStore();
        photoStore.Seed("card-1", [0xFF, 0xD8, 0xFF, 0xD9]);
        var service = CreateService(photoStore);

        var request = new GetPhotoDownloadUrlsRequest();
        request.PhotoIds.AddRange(["card-1", "missing"]);

        var response = await service.GetPhotoDownloadUrls(request, new FakeServerCallContext());

        Assert.Single(response.DownloadUrlsByPhotoId);
        Assert.True(response.DownloadUrlsByPhotoId.ContainsKey("card-1"));
        Assert.False(response.DownloadUrlsByPhotoId.ContainsKey("missing"));
    }
}
