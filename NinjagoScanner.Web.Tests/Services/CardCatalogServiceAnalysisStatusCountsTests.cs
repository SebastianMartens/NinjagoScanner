using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Services;

public sealed class CardCatalogServiceAnalysisStatusCountsTests : IAsyncLifetime
{
    private readonly CatalogServiceTestHost catalogHost = new();
    private readonly PictureServiceTestHost pictureHost = new();
    private CardCatalogService cardCatalogService = null!;

    static CardCatalogServiceAnalysisStatusCountsTests()
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
                {"Karten-Nr.": 4, "Name": "Cole"}
              ]
            }
          }
        }
        """);

        await catalogHost.StartAsync();
        await pictureHost.StartAsync();

        pictureHost.WritePhoto("ok.jpg", Sidecar("ok"));
        pictureHost.WritePhoto("uncertain.jpg", Sidecar("uncertain"));
        pictureHost.WritePhoto("failed.jpg", Sidecar("failed"));
        // No sidecar file at all: photo was uploaded/discovered but never scanned by Gemini.
        pictureHost.WritePhoto("not-analyzed.jpg", sidecarJson: null);

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

    private static string Sidecar(string analysisStatus)
    {
        return $$"""
        {
          "AnalysisStatus": "{{analysisStatus}}",
          "CardName": "irrelevant",
          "CardNumber": "4",
          "SetName": "Serie 2",
          "Rarity": "Common",
          "Confidence": 0.9,
          "ReviewStatus": "unreviewed"
        }
        """;
    }

    [Fact]
    public async Task GetSeriesSummaryAsync_PhotoWithoutSidecar_IsCountedAsNotAnalyzed()
    {
        var summary = await cardCatalogService.GetSeriesSummaryAsync();

        Assert.Equal(1, summary.AnalysisStatusCounts.Ok);
        Assert.Equal(1, summary.AnalysisStatusCounts.Uncertain);
        Assert.Equal(1, summary.AnalysisStatusCounts.Failed);
        Assert.Equal(1, summary.AnalysisStatusCounts.NotAnalyzed);
    }

    [Fact]
    public async Task GetSeriesSummaryAsync_AnalysisStatusCounts_SumToTotalPhotos()
    {
        var summary = await cardCatalogService.GetSeriesSummaryAsync();

        var sum = summary.AnalysisStatusCounts.Ok
            + summary.AnalysisStatusCounts.Uncertain
            + summary.AnalysisStatusCounts.Failed
            + summary.AnalysisStatusCounts.NotAnalyzed;

        Assert.Equal(summary.TotalPhotos, sum);
    }
}
