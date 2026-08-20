using NinjagoScanner.Web.Bff.Services;
using NinjagoScanner.Web.Bff.Tests.Fixtures;

namespace NinjagoScanner.Web.Bff.Tests.Services;

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
        pictureHost.WritePhoto("photo-1", Sidecar(setName: "Serie 2", cardNumber: "4"));
        // Normalizes to the same catalog card as photo-1 despite different spelling/formatting.
        pictureHost.WritePhoto("photo-2", Sidecar(setName: "Serie_2", cardNumber: "04"));
        // A distinct card in the same series.
        pictureHost.WritePhoto("photo-3", Sidecar(setName: "Serie 2", cardNumber: "5"));
        // Known series, but a card number the catalog doesn't have -> catch-all.
        pictureHost.WritePhoto("photo-4", Sidecar(setName: "Serie 2", cardNumber: "999"));
        // Unknown series entirely -> catch-all.
        pictureHost.WritePhoto("photo-5", Sidecar(setName: "Unknown Series", cardNumber: "1"));
        // Blank series/number -> catch-all.
        pictureHost.WritePhoto("photo-6", Sidecar(setName: "", cardNumber: ""));
        // A card in a series with a higher SortOrder, to verify group ordering.
        pictureHost.WritePhoto("photo-7", Sidecar(setName: "Serie 10", cardNumber: "1"));

        cardCatalogService = new CardCatalogService(
            catalogServiceAddress: catalogHost.Address,
            pictureServiceAddress: pictureHost.Address,
            uploadUrlIssuer: new FakeUploadUrlIssuer(),
            maxUploadBytes: 10 * 1024 * 1024);
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
        Assert.Contains(cole.Photos, photo => photo.PhotoId == "photo-1");
        Assert.Contains(cole.Photos, photo => photo.PhotoId == "photo-2");

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
        Assert.Contains(catchAll.Photos, photo => photo.PhotoId == "photo-4");
        Assert.Contains(catchAll.Photos, photo => photo.PhotoId == "photo-5");
        Assert.Contains(catchAll.Photos, photo => photo.PhotoId == "photo-6");
    }
}

public sealed class CardCatalogServiceReviewGroupsCardNumberOrderingTests : IAsyncLifetime
{
    private readonly CatalogServiceTestHost catalogHost = new();
    private readonly PictureServiceTestHost pictureHost = new();
    private CardCatalogService cardCatalogService = null!;

    static CardCatalogServiceReviewGroupsCardNumberOrderingTests()
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
                {"Karten-Nr.": 10, "Name": "Ten"},
                {"Karten-Nr.": 2, "Name": "Two"}
              ],
              "Limited_Edition_Cards": [
                {"Karten-Nr.": "LE1", "Name": "LE One"},
                {"Karten-Nr.": "XXL1", "Name": "XXL One"}
              ]
            }
          }
        }
        """);

        await catalogHost.StartAsync();
        await pictureHost.StartAsync();

        pictureHost.WritePhoto("photo-xxl1", Sidecar(setName: "Serie 2", cardNumber: "XXL1"));
        pictureHost.WritePhoto("photo-10", Sidecar(setName: "Serie 2", cardNumber: "10"));
        pictureHost.WritePhoto("photo-le1", Sidecar(setName: "Serie 2", cardNumber: "LE1"));
        pictureHost.WritePhoto("photo-2", Sidecar(setName: "Serie 2", cardNumber: "2"));

        cardCatalogService = new CardCatalogService(
            catalogServiceAddress: catalogHost.Address,
            pictureServiceAddress: pictureHost.Address,
            uploadUrlIssuer: new FakeUploadUrlIssuer(),
            maxUploadBytes: 10 * 1024 * 1024);
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
    public async Task GetReviewGroupsAsync_OrdersGroups_NumericFirstByValue_ThenAlphanumericByPrefix()
    {
        var groups = await cardCatalogService.GetReviewGroupsAsync();

        var cardNumbers = groups.Select(group => group.CardNumber).ToArray();

        string?[] expected = ["2", "10", "LE1", "XXL1"];
        Assert.Equal(expected, cardNumbers);
    }
}
