using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Services;

public sealed class CollectionQueryServiceGalleryTests : IAsyncLifetime
{
    private readonly CatalogServiceTestHost catalogHost = new();
    private readonly PictureServiceTestHost pictureHost = new();
    private CollectionQueryService collectionQueryService = null!;

    static CollectionQueryServiceGalleryTests()
    {
        // The test hosts serve gRPC over cleartext (non-TLS) HTTP/2, same as the real
        // services in this app's default local configuration.
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
              ] },
              "Puzzle_Cards": {
                "Class": "puzzle-piece",
                "Day_of_the_Departed": [
                  {"Karten-Nr.": 6, "Name": {"de": "Puzzle1"}}
                ]
              }
            }
          },
          "Serie_10": {
            "SortOrder": 10,
            "Kategorien": {
              "Good_Guys": { "Class": "character", "Karten": [
                {"Karten-Nr.": 1, "Name": {"de": "Kai"}}
              ] }
            }
          }
        }
        """);

        await catalogHost.StartAsync();
        await pictureHost.StartAsync();

        // Cole: exactly one matched photo.
        pictureHost.WritePhoto("photo-1", Sidecar(setName: "Serie 2", cardNumber: "4"));
        // Zane: two matched photos -> the pick must be deterministic (lowest photo ID).
        pictureHost.WritePhoto("photo-3", Sidecar(setName: "Serie 2", cardNumber: "5"));
        pictureHost.WritePhoto("photo-2", Sidecar(setName: "Serie 2", cardNumber: "5"));
        // Puzzle1 (card 6) intentionally has no photo.
        // Kai belongs to a different series and must not leak into a Serie 2 query.
        pictureHost.WritePhoto("photo-4", Sidecar(setName: "Serie 10", cardNumber: "1"));

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
          "Confidence": 0.9,
          "ReviewStatus": "unreviewed"
        }
        """;
    }

    [Fact]
    public async Task GetGalleryCardsAsync_ScopesToRequestedSeriesOnly()
    {
        var cards = await collectionQueryService.GetGalleryCardsAsync("Serie 2");

        Assert.Equal(3, cards.Count);
        Assert.All(cards, card => Assert.Equal("Serie 2", card.Series));
        Assert.DoesNotContain(cards, card => card.CardName == "Kai");
    }

    [Fact]
    public async Task GetGalleryCardsAsync_CardWithOneMatchedPhoto_HasImageUrl()
    {
        var cards = await collectionQueryService.GetGalleryCardsAsync("Serie 2");

        var cole = cards.Single(card => card.CardName == "Cole");
        Assert.NotNull(cole.ImageUrl);
        Assert.Contains("photo-1", cole.ImageUrl);
    }

    [Fact]
    public async Task GetGalleryCardsAsync_CardWithNoMatchedPhoto_HasNullImageUrl()
    {
        var cards = await collectionQueryService.GetGalleryCardsAsync("Serie 2");

        var puzzle = cards.Single(card => card.CardName == "Puzzle1");
        Assert.Null(puzzle.ImageUrl);
    }

    [Fact]
    public async Task GetGalleryCardsAsync_CardWithMultipleMatchedPhotos_PicksDeterministically()
    {
        var firstCall = await collectionQueryService.GetGalleryCardsAsync("Serie 2");
        var secondCall = await collectionQueryService.GetGalleryCardsAsync("Serie 2");

        var zaneFirst = firstCall.Single(card => card.CardName == "Zane");
        var zaneSecond = secondCall.Single(card => card.CardName == "Zane");

        Assert.NotNull(zaneFirst.ImageUrl);
        Assert.Contains("photo-2", zaneFirst.ImageUrl);
        Assert.Equal(zaneFirst.ImageUrl, zaneSecond.ImageUrl);
    }

    [Fact]
    public async Task GetGalleryCardsAsync_ResolvesUrlsOnlyForTheMatchedPhotoOfEachCard_InOneCall()
    {
        await collectionQueryService.GetGalleryCardsAsync("Serie 2");

        // Cole -> photo-1, Zane -> photo-2 (lowest of its two photos). Not photo-3 (Zane's other
        // photo) and not photo-4 (belongs to Serie 10).
        var call = Assert.Single(pictureHost.DownloadUrlsRequests);
        Assert.Equal(["photo-1", "photo-2"], call.Order());
    }

    [Fact]
    public async Task GetGalleryCardsAsync_SwitchingSeries_ResolvesOnlyTheNewlySelectedSeriesPhotos()
    {
        await collectionQueryService.GetGalleryCardsAsync("Serie 2");
        await collectionQueryService.GetGalleryCardsAsync("Serie 10");

        Assert.Equal(2, pictureHost.DownloadUrlsRequests.Count);
        Assert.Equal(["photo-4"], pictureHost.DownloadUrlsRequests[1]);
    }

    [Fact]
    public async Task GetGalleryCardsAsync_SeriesWithNoPhotos_MakesNoDownloadUrlRequest()
    {
        await collectionQueryService.GetGalleryCardsAsync("Serie 5");

        Assert.Empty(pictureHost.DownloadUrlsRequests);
    }

    [Fact]
    public async Task GetGalleryCardsAsync_PuzzleSubGroup_CategoryLabelIsParentSlashChildName()
    {
        var cards = await collectionQueryService.GetGalleryCardsAsync("Serie 2");

        var puzzle = cards.Single(card => card.CardName == "Puzzle1");
        Assert.Equal("Puzzle Cards / Day of the Departed", puzzle.Category);
    }

    [Fact]
    public async Task GetGalleryCardsAsync_CardWithMatchedPhoto_ExposesPhotoIdAndReviewStatus()
    {
        var cards = await collectionQueryService.GetGalleryCardsAsync("Serie 2");

        var cole = cards.Single(card => card.CardName == "Cole");
        Assert.Equal("photo-1", cole.PhotoId);
        Assert.Equal("unreviewed", cole.ReviewStatus);
    }

    [Fact]
    public async Task GetGalleryCardsAsync_CardWithNoMatchedPhoto_HasNullPhotoIdAndReviewStatus()
    {
        var cards = await collectionQueryService.GetGalleryCardsAsync("Serie 2");

        var puzzle = cards.Single(card => card.CardName == "Puzzle1");
        Assert.Null(puzzle.PhotoId);
        Assert.Null(puzzle.ReviewStatus);
    }
}
