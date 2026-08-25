using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Services;

public sealed class CollectionQueryServiceAnalysisStatusCountsTests : IAsyncLifetime
{
    private readonly CatalogServiceTestHost catalogHost = new();
    private readonly PictureServiceTestHost pictureHost = new();
    private CollectionQueryService collectionQueryService = null!;

    static CollectionQueryServiceAnalysisStatusCountsTests()
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

        pictureHost.WritePhoto("ok-photo", Sidecar("ok"));
        pictureHost.WritePhoto("uncertain-photo", Sidecar("uncertain"));
        pictureHost.WritePhoto("failed-photo", Sidecar("failed"));
        // No sidecar record at all: photo was uploaded but never scanned by Gemini.
        pictureHost.WritePhoto("not-analyzed-photo", sidecarJson: null);

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
        var summary = await collectionQueryService.GetSeriesSummaryAsync();

        Assert.Equal(1, summary.AnalysisStatusCounts.Ok);
        Assert.Equal(1, summary.AnalysisStatusCounts.Uncertain);
        Assert.Equal(1, summary.AnalysisStatusCounts.Failed);
        Assert.Equal(1, summary.AnalysisStatusCounts.NotAnalyzed);
    }

    [Fact]
    public async Task GetSeriesSummaryAsync_AnalysisStatusCounts_SumToTotalPhotos()
    {
        var summary = await collectionQueryService.GetSeriesSummaryAsync();

        var sum = summary.AnalysisStatusCounts.Ok
            + summary.AnalysisStatusCounts.Uncertain
            + summary.AnalysisStatusCounts.Failed
            + summary.AnalysisStatusCounts.NotAnalyzed;

        Assert.Equal(summary.TotalPhotos, sum);
    }
}
