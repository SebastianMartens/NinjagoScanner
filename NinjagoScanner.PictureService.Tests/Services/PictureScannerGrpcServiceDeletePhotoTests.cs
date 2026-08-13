using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NinjagoScanner.PictureService;
using NinjagoScanner.PictureService.Protos;
using NinjagoScanner.PictureService.Services;
using NinjagoScanner.PictureService.Tests.Fixtures;

namespace NinjagoScanner.PictureService.Tests.Services;

public sealed class PictureScannerGrpcServiceDeletePhotoTests : IDisposable
{
    private readonly string cardPhotosDirectory = Path.Combine(
        Path.GetTempPath(),
        $"NinjagoScannerPictureServiceTests_{Guid.NewGuid():N}");

    public PictureScannerGrpcServiceDeletePhotoTests()
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

    private static PictureScannerGrpcService CreateService(SidecarCache sidecarCache)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new PictureScannerGrpcService(configuration, NullLogger<PictureScannerGrpcService>.Instance, sidecarCache);
    }

    [Fact]
    public async Task DeletePhoto_RemovesImageAndSidecar_WhenBothExist()
    {
        var imagePath = Path.Combine(cardPhotosDirectory, "card-1.jpg");
        var sidecarPath = imagePath + ".json";
        await File.WriteAllBytesAsync(imagePath, [0xFF, 0xD8, 0xFF, 0xD9]);
        await File.WriteAllTextAsync(sidecarPath, """{ "AnalysisStatus": "ok", "CardName": "Kai" }""");

        var service = CreateService(new SidecarCache());

        var response = await service.DeletePhoto(
            new DeletePhotoRequest { ImageFileName = "card-1.jpg", CardPhotosDirectory = cardPhotosDirectory },
            new FakeServerCallContext());

        Assert.True(response.Success);
        Assert.False(File.Exists(imagePath));
        Assert.False(File.Exists(sidecarPath));
    }

    [Fact]
    public async Task DeletePhoto_RemovesImage_WhenNoSidecarExists()
    {
        var imagePath = Path.Combine(cardPhotosDirectory, "card-2.jpg");
        await File.WriteAllBytesAsync(imagePath, [0xFF, 0xD8, 0xFF, 0xD9]);

        var service = CreateService(new SidecarCache());

        var response = await service.DeletePhoto(
            new DeletePhotoRequest { ImageFileName = "card-2.jpg", CardPhotosDirectory = cardPhotosDirectory },
            new FakeServerCallContext());

        Assert.True(response.Success);
        Assert.False(File.Exists(imagePath));
    }

    [Fact]
    public async Task DeletePhoto_FailsWithNotFound_WhenImageDoesNotExist()
    {
        var service = CreateService(new SidecarCache());

        var exception = await Assert.ThrowsAsync<RpcException>(() => service.DeletePhoto(
            new DeletePhotoRequest { ImageFileName = "missing.jpg", CardPhotosDirectory = cardPhotosDirectory },
            new FakeServerCallContext()));

        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task DeletePhoto_DoesNotTouchOtherFiles_WhenImageDoesNotExist()
    {
        var untouchedImagePath = Path.Combine(cardPhotosDirectory, "keep-me.jpg");
        await File.WriteAllBytesAsync(untouchedImagePath, [0xFF, 0xD8, 0xFF, 0xD9]);

        var service = CreateService(new SidecarCache());

        await Assert.ThrowsAsync<RpcException>(() => service.DeletePhoto(
            new DeletePhotoRequest { ImageFileName = "missing.jpg", CardPhotosDirectory = cardPhotosDirectory },
            new FakeServerCallContext()));

        Assert.True(File.Exists(untouchedImagePath));
    }

    [Fact]
    public async Task DeletePhoto_EvictsSidecarFromCache()
    {
        var imagePath = Path.Combine(cardPhotosDirectory, "card-3.jpg");
        var sidecarPath = imagePath + ".json";
        await File.WriteAllBytesAsync(imagePath, [0xFF, 0xD8, 0xFF, 0xD9]);
        await File.WriteAllTextAsync(sidecarPath, """{ "AnalysisStatus": "ok", "CardName": "Nya" }""");

        var sidecarCache = new SidecarCache();
        // Prime the cache the same way ListCards would, so a stale cached entry would survive
        // deletion if DeletePhoto forgot to evict it.
        var cachedBeforeDelete = await sidecarCache.GetAsync(sidecarPath, CancellationToken.None);
        Assert.Equal("Nya", cachedBeforeDelete!.CardName);

        var service = CreateService(sidecarCache);
        await service.DeletePhoto(
            new DeletePhotoRequest { ImageFileName = "card-3.jpg", CardPhotosDirectory = cardPhotosDirectory },
            new FakeServerCallContext());

        // The file is gone, so a cache implementation that still serves the old cached record
        // would diverge from disk; reading it now must fail rather than resurrect stale data.
        await Assert.ThrowsAnyAsync<Exception>(() => sidecarCache.GetAsync(sidecarPath, CancellationToken.None));
    }
}
