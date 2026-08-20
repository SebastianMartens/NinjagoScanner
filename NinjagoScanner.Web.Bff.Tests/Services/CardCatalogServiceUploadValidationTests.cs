using NinjagoScanner.Web.Bff.Services;
using NinjagoScanner.Web.Bff.Tests.Fixtures;

namespace NinjagoScanner.Web.Bff.Tests.Services;

public sealed class CardCatalogServiceUploadValidationTests
{
    private readonly CardCatalogService cardCatalogService = new(
        catalogServiceAddress: "http://localhost:0",
        pictureServiceAddress: "http://localhost:0",
        uploadUrlIssuer: new FakeUploadUrlIssuer(),
        maxUploadBytes: 1024);

    [Fact]
    public void ValidateUpload_AcceptsSupportedTypeWithinSizeLimit_AndReturnsGeneratedPhotoId()
    {
        var (photoId, contentType) = cardCatalogService.ValidateUpload("card.jpg", 512, "image/jpeg");

        Assert.False(string.IsNullOrWhiteSpace(photoId));
        Assert.Equal("image/jpeg", contentType);
    }

    [Fact]
    public void ValidateUpload_TwoCallsWithSameFileName_ReturnDifferentPhotoIds()
    {
        var (firstPhotoId, _) = cardCatalogService.ValidateUpload("card.jpg", 512, "image/jpeg");
        var (secondPhotoId, _) = cardCatalogService.ValidateUpload("card.jpg", 512, "image/jpeg");

        Assert.NotEqual(firstPhotoId, secondPhotoId);
    }

    [Fact]
    public void ValidateUpload_OversizedFile_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => cardCatalogService.ValidateUpload("card.jpg", 2048, "image/jpeg"));

        Assert.Contains("zu gross", exception.Message);
    }

    [Fact]
    public void ValidateUpload_UnsupportedExtension_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => cardCatalogService.ValidateUpload("card.gif", 512, "image/gif"));

        Assert.Contains("Dateityp", exception.Message);
    }

    [Fact]
    public void ValidateUpload_EmptyFile_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => cardCatalogService.ValidateUpload("card.jpg", 0, "image/jpeg"));
    }

    [Fact]
    public void ValidateUpload_MissingContentType_DefaultsToOctetStream()
    {
        var (_, contentType) = cardCatalogService.ValidateUpload("card.png", 512, contentType: null);

        Assert.Equal("application/octet-stream", contentType);
    }
}
