using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Services;

public sealed class CardCatalogServiceReviewGroupsTests : IAsyncLifetime
{
    private readonly CatalogServiceTestHost catalogHost = new();
    private readonly PictureServiceTestHost pictureHost = new();
    private CardCatalogService cardCatalogService = null!;

    static CardCatalogServiceReviewGroupsTests()
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
              "Good_Guys": [
                {"Karten-Nr.": 4, "Name": "Cole"},
                {"Karten-Nr.": 5, "Name": "Zane"}
              ]
            }
          },
          "Serie_10": {
            "SortOrder": 10,
            "Kategorien": {
              "Good_Guys": [
                {"Karten-Nr.": 1, "Name": "Kai"}
              ]
            }
          }
        }
        """);

        await catalogHost.StartAsync();
        await pictureHost.StartAsync();

        // Raw match: same SetName/CardNumber spelling as the catalog entry.
        pictureHost.WritePhoto("photo1.jpg", Sidecar(setName: "Serie 2", cardNumber: "4"));
        // Normalizes to the same catalog card as photo1 despite different spelling/formatting.
        pictureHost.WritePhoto("photo2.jpg", Sidecar(setName: "Serie_2", cardNumber: "04"));
        // A distinct card in the same series.
        pictureHost.WritePhoto("photo3.jpg", Sidecar(setName: "Serie 2", cardNumber: "5"));
        // Known series, but a card number the catalog doesn't have -> catch-all.
        pictureHost.WritePhoto("photo4.jpg", Sidecar(setName: "Serie 2", cardNumber: "999"));
        // Unknown series entirely -> catch-all.
        pictureHost.WritePhoto("photo5.jpg", Sidecar(setName: "Unknown Series", cardNumber: "1"));
        // Blank series/number -> catch-all.
        pictureHost.WritePhoto("photo6.jpg", Sidecar(setName: "", cardNumber: ""));
        // A card in a series with a higher SortOrder, to verify group ordering.
        pictureHost.WritePhoto("photo7.jpg", Sidecar(setName: "Serie 10", cardNumber: "1"));

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
    public async Task GetReviewGroupsAsync_GroupsByNormalizedCatalogIdentity_AndAttachesCardName()
    {
        var groups = await cardCatalogService.GetReviewGroupsAsync();

        Assert.Equal(4, groups.Count);

        var cole = groups[0];
        Assert.False(cole.IsCatchAll);
        Assert.Equal("Serie 2", cole.SeriesName);
        Assert.Equal("4", cole.CardNumber);
        Assert.Equal("Cole", cole.CardName);
        Assert.Equal(2, cole.Photos.Count);
        Assert.Contains(cole.Photos, photo => photo.ImageFileName == "photo1.jpg");
        Assert.Contains(cole.Photos, photo => photo.ImageFileName == "photo2.jpg");

        var zane = groups[1];
        Assert.False(zane.IsCatchAll);
        Assert.Equal("Serie 2", zane.SeriesName);
        Assert.Equal("5", zane.CardNumber);
        Assert.Equal("Zane", zane.CardName);
        Assert.Single(zane.Photos);

        var kai = groups[2];
        Assert.False(kai.IsCatchAll);
        Assert.Equal("Serie 10", kai.SeriesName);
        Assert.Equal("Kai", kai.CardName);
        Assert.Single(kai.Photos);

        var catchAll = groups[3];
        Assert.True(catchAll.IsCatchAll);
        Assert.Null(catchAll.CardName);
        Assert.Equal(3, catchAll.Photos.Count);
        Assert.Contains(catchAll.Photos, photo => photo.ImageFileName == "photo4.jpg");
        Assert.Contains(catchAll.Photos, photo => photo.ImageFileName == "photo5.jpg");
        Assert.Contains(catchAll.Photos, photo => photo.ImageFileName == "photo6.jpg");
    }
}
