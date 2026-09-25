using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Services;

/// <summary>
/// Covers <see cref="PictureServiceClient.GetCardsAsync"/> resolving no download URLs (listing a
/// collection must not scale with its size) and <see cref="PictureServiceClient.GetDownloadUrlsAsync"/>
/// resolving exactly the requested photos in one call.
/// </summary>
public sealed class PictureServiceClientGetCardsAsyncTests : IAsyncLifetime
{
    private readonly PictureServiceTestHost pictureHost = new();
    private PictureServiceClient pictureServiceClient = null!;

    static PictureServiceClientGetCardsAsyncTests()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    public async Task InitializeAsync()
    {
        await pictureHost.StartAsync();

        pictureHost.WritePhoto("photo-1", Sidecar(setName: "Serie 2", cardNumber: "1"));
        pictureHost.WritePhoto("photo-2", Sidecar(setName: "Serie 2", cardNumber: "2"));
        pictureHost.WritePhoto("photo-3", Sidecar(setName: "Serie 2", cardNumber: "3"));

        pictureServiceClient = TestPictureServiceClientFactory.Create(
            pictureServiceAddress: pictureHost.Address,
            catalogServiceAddress: "http://localhost:0",
            maxUploadBytes: 10 * 1024 * 1024);
    }

    public async Task DisposeAsync()
    {
        await pictureHost.DisposeAsync();
    }

    private static string Sidecar(string setName, string cardNumber)
    {
        return $$"""
        {
          "AnalysisStatus": "ok",
          "CardName": "irrelevant",
          "CardNumber": "{{cardNumber}}",
          "SetName": "{{setName}}",
          "Rarity": "Common",
          "Confidence": 0.9,
          "ReviewStatus": "unreviewed"
        }
        """;
    }

    [Fact]
    public async Task GetCardsAsync_ResolvesNoImageUrls_AndMakesNoDownloadUrlRequest()
    {
        var cards = await pictureServiceClient.GetCardsAsync();

        Assert.Equal(3, cards.Count);
        Assert.All(cards, card => Assert.Equal(string.Empty, card.ImageUrl));
        Assert.Empty(pictureHost.DownloadUrlsRequests);
        Assert.Equal(0, pictureHost.DownloadUrlCallCount);
    }

    [Fact]
    public async Task GetDownloadUrlsAsync_ResolvesOnlyTheRequestedPhotos_InOneCall()
    {
        var urls = await pictureServiceClient.GetDownloadUrlsAsync(["photo-1", "photo-3"]);

        Assert.Equal(2, urls.Count);
        Assert.Equal("https://fake-photo-store.test/photo-1", urls["photo-1"]);
        Assert.Equal("https://fake-photo-store.test/photo-3", urls["photo-3"]);
        Assert.DoesNotContain("photo-2", urls.Keys);
        Assert.Equal(["photo-1", "photo-3"], Assert.Single(pictureHost.DownloadUrlsRequests));
    }

    [Fact]
    public async Task GetDownloadUrlsAsync_OmitsAPhotoThatDoesNotExist_WithoutFailing()
    {
        var urls = await pictureServiceClient.GetDownloadUrlsAsync(["photo-1", "gone"]);

        Assert.Equal(["photo-1"], urls.Keys);
    }

    [Fact]
    public async Task GetDownloadUrlsAsync_WithNoPhotoIds_MakesNoCall()
    {
        var urls = await pictureServiceClient.GetDownloadUrlsAsync([]);

        Assert.Empty(urls);
        Assert.Empty(pictureHost.DownloadUrlsRequests);
    }
}
