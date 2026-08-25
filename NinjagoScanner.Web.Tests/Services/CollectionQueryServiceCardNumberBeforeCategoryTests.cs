using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Services;

public sealed class CollectionQueryServiceCardNumberBeforeCategoryTests : IAsyncLifetime
{
    private readonly CatalogServiceTestHost catalogHost = new();
    private readonly PictureServiceTestHost pictureHost = new();
    private CollectionQueryService collectionQueryService = null!;

    static CollectionQueryServiceCardNumberBeforeCategoryTests()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    public async Task InitializeAsync()
    {
        // Mirrors the real catalog shape: "Action Cards" sorts alphabetically before "Heroes",
        // but its card numbers start at 101 while "Heroes" starts at 1.
        catalogHost.WriteCatalogFile("series_test.json", """
        {
          "Serie_1": {
            "SortOrder": 1,
            "Kategorien": {
              "Action_Cards": [
                {"Karten-Nr.": 101, "Name": "First Action Card"}
              ],
              "Heroes": [
                {"Karten-Nr.": 1, "Name": "First Hero"},
                {"Karten-Nr.": 2, "Name": "Second Hero"}
              ]
            }
          }
        }
        """);

        await catalogHost.StartAsync();
        await pictureHost.StartAsync();

        var catalogServiceClient = new CatalogServiceClient(catalogHost.Address);
        var pictureServiceClient = new PictureServiceClient(
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
    public async Task GetCollectionOverviewAsync_OrdersByCardNumber_NotByCategory()
    {
        var overview = await collectionQueryService.GetCollectionOverviewAsync();

        var cardNumbers = overview.Cards.Select(card => card.CardNumber).ToArray();

        Assert.Equal(["1", "2", "101"], cardNumbers);
    }

    [Fact]
    public async Task GetGalleryCardsAsync_OrdersByCardNumber_NotByCategory()
    {
        var cards = await collectionQueryService.GetGalleryCardsAsync("Serie 1");

        var cardNumbers = cards.Select(card => card.CardNumber).ToArray();

        Assert.Equal(["1", "2", "101"], cardNumbers);
    }
}
