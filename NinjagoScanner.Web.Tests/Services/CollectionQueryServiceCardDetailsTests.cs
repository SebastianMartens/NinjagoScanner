using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Services;

/// <summary>
/// Covers the Collection page's per-card photo list resolving download URLs through the bounded
/// call, scoped to just that card's photos.
/// </summary>
public sealed class CollectionQueryServiceCardDetailsTests : IAsyncLifetime
{
    private readonly CatalogServiceTestHost catalogHost = new();
    private readonly PictureServiceTestHost pictureHost = new();
    private CollectionQueryService collectionQueryService = null!;

    static CollectionQueryServiceCardDetailsTests()
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
              "Good_Guys": { "Class": "character", "Karten": [
                {"Karten-Nr.": 4, "Name": {"de": "Cole"}},
                {"Karten-Nr.": 5, "Name": {"de": "Zane"}}
              ] }
            }
          }
        }
        """);

        await catalogHost.StartAsync();
        await pictureHost.StartAsync();

        pictureHost.WritePhoto("photo-cole-b", Sidecar(setName: "Serie 2", cardNumber: "4"));
        pictureHost.WritePhoto("photo-cole-a", Sidecar(setName: "Serie 2", cardNumber: "4"));
        pictureHost.WritePhoto("photo-zane", Sidecar(setName: "Serie 2", cardNumber: "5"));
        pictureHost.WritePhoto("photo-stray", Sidecar(setName: "Unknown Series", cardNumber: "1"));

        var catalogServiceClient = new CatalogServiceClient(catalogHost.Address);
        var pictureServiceClient = TestPictureServiceClientFactory.Create(
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

    private static string Sidecar(string setName, string cardNumber)
    {
        return $$"""
        {
          "AnalysisStatus": "ok",
          "CardName": "irrelevant",
          "CardNumber": "{{cardNumber}}",
          "SetName": "{{setName}}",
          "Rarity": "Common",
          "ReviewStatus": "unreviewed"
        }
        """;
    }

    [Fact]
    public async Task GetCollectionCardDetailsAsync_ResolvesUrlsOnlyForThatCardsPhotos_InOneCall()
    {
        var details = await collectionQueryService.GetCollectionCardDetailsAsync("Serie 2", "4");

        Assert.NotNull(details);
        Assert.Equal(["photo-cole-a", "photo-cole-b"], details.Photos.Select(photo => photo.PhotoId));
        Assert.All(details.Photos, photo => Assert.Equal($"https://fake-photo-store.test/{photo.PhotoId}", photo.ImageUrl));

        var call = Assert.Single(pictureHost.DownloadUrlsRequests);
        Assert.Equal(["photo-cole-a", "photo-cole-b"], call.Order());
    }

    [Fact]
    public async Task GetCollectionCardDetailsAsync_CardWithNoPhotos_MakesNoDownloadUrlRequest()
    {
        var details = await collectionQueryService.GetCollectionCardDetailsAsync("Serie 2", "99");

        Assert.Null(details);
        Assert.Empty(pictureHost.DownloadUrlsRequests);
    }
}
