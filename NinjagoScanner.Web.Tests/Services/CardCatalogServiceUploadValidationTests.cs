using NinjagoScanner.Web.Services;

namespace NinjagoScanner.Web.Tests.Services;

/// <summary>
/// Upload validation (file type/size) happens before <see cref="CardCatalogService.UploadPhotoAsync"/>
/// opens the gRPC stream to PictureService, so these tests use unreachable addresses — a valid
/// call never gets far enough to need them.
/// </summary>
public sealed class CardCatalogServiceUploadValidationTests
{
    private readonly CardCatalogService cardCatalogService = new(
        catalogServiceAddress: "http://localhost:0",
        pictureServiceAddress: "http://localhost:0",
        maxUploadBytes: 1024);

    [Fact]
    public async Task UploadPhotoAsync_OversizedFile_Throws()
    {
        await using var content = new MemoryStream([1, 2, 3]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => cardCatalogService.UploadPhotoAsync("card.jpg", 2048, content));

        Assert.Contains("zu gross", exception.Message);
    }

    [Fact]
    public async Task UploadPhotoAsync_UnsupportedExtension_Throws()
    {
        await using var content = new MemoryStream([1, 2, 3]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => cardCatalogService.UploadPhotoAsync("card.gif", 512, content));

        Assert.Contains("Dateityp", exception.Message);
    }

    [Fact]
    public async Task UploadPhotoAsync_EmptyFile_Throws()
    {
        await using var content = new MemoryStream();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => cardCatalogService.UploadPhotoAsync("card.jpg", 0, content));
    }
}
