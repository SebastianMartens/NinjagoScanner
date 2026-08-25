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

        pictureHost.WritePhoto("photo-1", """
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
            catalogServiceAddress: catalogHost.Address,
            pictureServiceAddress: pictureHost.Address,
            maxUploadBytes: 10 * 1024 * 1024);
    }

    public async Task DisposeAsync()
    {
        await catalogHost.DisposeAsync();
        await pictureHost.DisposeAsync();
    }

    [Fact]
    public async Task DeletePhotoAsync_RemovesPhoto_AndCardNoLongerAppearsInLists()
    {
        await cardCatalogService.DeletePhotoAsync("photo-1");

        var cards = await cardCatalogService.GetCardsAsync();
        Assert.DoesNotContain(cards, card => card.PhotoId == "photo-1");

        var galleryCards = await cardCatalogService.GetGalleryCardsAsync("Serie 2");
        var cole = galleryCards.Single(card => card.CardName == "Cole");
        Assert.Null(cole.ImageUrl);
        Assert.Null(cole.PhotoId);
    }

    [Fact]
    public async Task DeletePhotoAsync_ThrowsRpcException_WhenPhotoDoesNotExist()
    {
        var exception = await Assert.ThrowsAsync<RpcException>(
            () => cardCatalogService.DeletePhotoAsync("does-not-exist"));

        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
    }
}
