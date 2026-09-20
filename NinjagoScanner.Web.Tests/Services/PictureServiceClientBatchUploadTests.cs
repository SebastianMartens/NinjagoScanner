using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Services;

public sealed class PictureServiceClientBatchUploadTests : IAsyncLifetime
{
    private readonly PictureServiceTestHost pictureHost = new();
    private PictureServiceClient pictureServiceClient = null!;

    static PictureServiceClientBatchUploadTests()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    public async Task InitializeAsync()
    {
        await pictureHost.StartAsync();

        pictureServiceClient = TestPictureServiceClientFactory.Create(
            pictureServiceAddress: pictureHost.Address,
            catalogServiceAddress: "http://localhost:0",
            maxUploadBytes: 1024);
    }

    public async Task DisposeAsync()
    {
        await pictureHost.DisposeAsync();
    }

    [Fact]
    public async Task ListSourceFileNamesAsync_ReturnsTheCollectionsNamesAsAnOrdinalSet()
    {
        pictureHost.WritePhoto("p1", """{ "SourceFileName": "IMG_0001.jpg" }""");
        pictureHost.WritePhoto("p2", """{ "SourceFileName": "IMG_0001.jpg" }""");
        pictureHost.WritePhoto("p3", """{ "SourceFileName": "b.png" }""");
        pictureHost.WritePhoto("p4", """{ "AnalysisStatus": "ok" }""");
        pictureHost.WritePhoto("p5", """{ "SourceFileName": "other.jpg" }""", collectionId: "another-collection");

        var names = await pictureServiceClient.ListSourceFileNamesAsync();

        Assert.Equal(["IMG_0001.jpg", "b.png"], names.Order(StringComparer.Ordinal));
        Assert.Contains("IMG_0001.jpg", names);
        Assert.DoesNotContain("img_0001.jpg", names);
    }

    [Fact]
    public async Task ListSourceFileNamesAsync_EmptyCollection_ReturnsAnEmptySet()
    {
        var names = await pictureServiceClient.ListSourceFileNamesAsync();

        Assert.Empty(names);
    }

    [Fact]
    public async Task UploadPhotoForBatchAsync_SetsSkipAnalysis_AndFetchesNoDownloadUrl()
    {
        await using var content = new MemoryStream([1, 2, 3]);

        await pictureServiceClient.UploadPhotoForBatchAsync("card.jpg", content.Length, content);

        Assert.Equal([new PictureServiceTestHost.RecordedUpload("card.jpg", SkipAnalysis: true)], pictureHost.Uploads);
        Assert.Equal(0, pictureHost.DownloadUrlCallCount);
        var names = await pictureServiceClient.ListSourceFileNamesAsync();
        Assert.Contains("card.jpg", names);
    }

    [Fact]
    public async Task UploadPhotoForBatchAsync_StoresThePhotoAsNotAnalyzed()
    {
        await using var content = new MemoryStream([1, 2, 3]);

        await pictureServiceClient.UploadPhotoForBatchAsync("card.jpg", content.Length, content);

        var card = Assert.Single(await pictureServiceClient.GetCardsAsync());
        Assert.Equal("notAnalyzed", card.AnalysisStatus);
        Assert.Equal("card.jpg", card.SourceFileName);
    }

    [Theory]
    [InlineData("card.jpg", 2048, "zu gross")]
    [InlineData("card.gif", 512, "Dateityp")]
    [InlineData("card.jpg", 0, "leer")]
    public async Task UploadPhotoForBatchAsync_InvalidFile_ThrowsBeforeUploading(string fileName, long size, string expectedMessagePart)
    {
        await using var content = new MemoryStream([1, 2, 3]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pictureServiceClient.UploadPhotoForBatchAsync(fileName, size, content));

        Assert.Contains(expectedMessagePart, exception.Message);
        Assert.Empty(pictureHost.Uploads);
    }

    [Fact]
    public async Task UploadPhotoAsync_SinglePhoto_KeepsAnalyzingInlineAndFetchesTheDownloadUrl()
    {
        await using var content = new MemoryStream([1, 2, 3]);

        var card = await pictureServiceClient.UploadPhotoAsync("card.jpg", content.Length, content);

        Assert.Equal([new PictureServiceTestHost.RecordedUpload("card.jpg", SkipAnalysis: false)], pictureHost.Uploads);
        Assert.Equal(1, pictureHost.DownloadUrlCallCount);
        Assert.False(string.IsNullOrEmpty(card.ImageUrl));
    }
}
