using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Services;

/// <summary>
/// The review page's series-assignment grid is filled from <see cref="CatalogServiceClient.GetKnownSeriesAsync"/>,
/// so this is the seam that decides which series buttons a photo tile offers.
/// </summary>
public sealed class CatalogServiceClientKnownSeriesTests : IAsyncLifetime
{
    private readonly CatalogServiceTestHost catalogHost = new();
    private CatalogServiceClient catalogServiceClient = null!;

    static CatalogServiceClientKnownSeriesTests()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    public async Task InitializeAsync()
    {
        catalogHost.WriteCatalogFile("series_1.json", """
        {
          "Serie_1": {
            "SortOrder": 10,
            "Kategorien": {
              "Heroes": { "Class": "character", "Karten": [ {"Karten-Nr.": 1, "Name": {"en": "Kai"}} ] }
            }
          }
        }
        """);
        catalogHost.WriteCatalogFile("series_0_spinner.json", """
        {
          "Serie_0": {
            "SortOrder": 0,
            "Kategorien": {
              "Character Cards Deck 1": { "Class": "character", "Karten": [ {"Karten-Nr.": 1, "Name": {"en": "Spinner Kai"}} ] }
            }
          }
        }
        """);

        await catalogHost.StartAsync();
        catalogServiceClient = new CatalogServiceClient(catalogHost.Address);
    }

    public async Task DisposeAsync() => await catalogHost.DisposeAsync();

    [Fact]
    public async Task GetKnownSeriesAsync_ReturnsSeriesZeroBeforeSeriesOne()
    {
        var series = await catalogServiceClient.GetKnownSeriesAsync();

        Assert.Equal(["Serie 0", "Serie 1"], series);
    }
}
