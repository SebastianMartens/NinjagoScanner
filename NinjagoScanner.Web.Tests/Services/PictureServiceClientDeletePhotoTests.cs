using Grpc.Core;
using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Services;

public sealed class PictureServiceClientDeletePhotoTests : IAsyncLifetime
{
    private readonly CatalogServiceTestHost catalogHost = new();
    private readonly PictureServiceTestHost pictureHost = new();
    private PictureServiceClient pictureServiceClient = null!;
    private CollectionQueryService collectionQueryService = null!;

    static PictureServiceClientDeletePhotoTests()
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

        var catalogServiceClient = new CatalogServiceClient(catalogHost.Address);
        pictureServiceClient = new PictureServiceClient(
            pictureServiceAddress: pictureHost.Address,
            catalogServiceAddress: catalogHost.Address,
            maxUploadBytes: 10 * 1024 * 1024);
        collectionQueryService = new CollectionQueryService(catalogServiceClient, pictureServiceClient);
    }

    public async Task DisposeAsync()
    {
        await catalogHost.DisposeAsync();
        await pictureHost.DisposeAsync();
    }

    [Fact]
    public async Task DeletePhotoAsync_RemovesPhoto_AndCardNoLongerAppearsInLists()
    {
        await pictureServiceClient.DeletePhotoAsync("photo-1");

        var cards = await pictureServiceClient.GetCardsAsync();
        Assert.DoesNotContain(cards, card => card.PhotoId == "photo-1");

        var galleryCards = await collectionQueryService.GetGalleryCardsAsync("Serie 2");
        var cole = galleryCards.Single(card => card.CardName == "Cole");
        Assert.Null(cole.ImageUrl);
        Assert.Null(cole.PhotoId);
    }

    [Fact]
    public async Task DeletePhotoAsync_ThrowsRpcException_WhenPhotoDoesNotExist()
    {
        var exception = await Assert.ThrowsAsync<RpcException>(
            () => pictureServiceClient.DeletePhotoAsync("does-not-exist"));

        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
    }
}
