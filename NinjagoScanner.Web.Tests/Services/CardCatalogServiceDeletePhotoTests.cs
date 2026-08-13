using Grpc.Core;
using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Services;

public sealed class CardCatalogServiceDeletePhotoTests : IAsyncLifetime
{
    private readonly CatalogServiceTestHost catalogHost = new();
    private readonly PictureServiceTestHost pictureHost = new();
    private CardCatalogService cardCatalogService = null!;

    static CardCatalogServiceDeletePhotoTests()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    public async Task InitializeAsync()
    {
        catalogHost.WriteCatalogFile("series_test.json", """
        {
          "Serie_2": {
            "SortOrder": 2,
            "Kategorien": {
              "Good_Guys": [
                {"Karten-Nr.": 4, "Name": "Cole"}
              ]
            }
          }
        }
        """);

        await catalogHost.StartAsync();
        await pictureHost.StartAsync();

        pictureHost.WritePhoto("photo1.jpg", """
        {
          "AnalysisStatus": "ok",
          "CardName": "Cole",
          "CardNumber": "4",
          "SetName": "Serie 2",
          "Rarity": "Common",
          "Confidence": 0.9,
          "ReviewStatus": "unreviewed"
        }
        """);

        cardCatalogService = new CardCatalogService(
            cardPhotosDirectory: pictureHost.CardPhotosDirectory,
            maxUploadBytes: 10 * 1024 * 1024,
            catalogServiceAddress: catalogHost.Address,
            pictureServiceAddress: pictureHost.Address);
    }

    public async Task DisposeAsync()
    {
        await catalogHost.DisposeAsync();
        await pictureHost.DisposeAsync();
    }

    [Fact]
    public async Task DeletePhotoAsync_RemovesFilesFromDisk_AndCardNoLongerAppearsInLists()
    {
        var imagePath = Path.Combine(pictureHost.CardPhotosDirectory, "photo1.jpg");
        var sidecarPath = imagePath + ".json";
        Assert.True(File.Exists(imagePath));
        Assert.True(File.Exists(sidecarPath));

        await cardCatalogService.DeletePhotoAsync("photo1.jpg");

        Assert.False(File.Exists(imagePath));
        Assert.False(File.Exists(sidecarPath));

        var cards = await cardCatalogService.GetCardsAsync();
        Assert.DoesNotContain(cards, card => card.ImageFileName == "photo1.jpg");

        var galleryCards = await cardCatalogService.GetGalleryCardsAsync("Serie 2");
        var cole = galleryCards.Single(card => card.CardName == "Cole");
        Assert.Null(cole.ImageUrl);
        Assert.Null(cole.ImageFileName);
    }

    [Fact]
    public async Task DeletePhotoAsync_ThrowsRpcException_WhenPhotoDoesNotExist()
    {
        var exception = await Assert.ThrowsAsync<RpcException>(
            () => cardCatalogService.DeletePhotoAsync("does-not-exist.jpg"));

        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
    }
}
