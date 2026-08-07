using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using NinjagoScanner.CatalogService.Protos;
using NinjagoScanner.CatalogService.Services;
using NinjagoScanner.CatalogService.Tests.Fixtures;

namespace NinjagoScanner.CatalogService.Tests.Services;

public sealed class CardCatalogGrpcServiceTests : IDisposable
{
    private readonly TempCatalogDirectory directory = new();

    private CardCatalogGrpcService CreateService()
    {
        return new CardCatalogGrpcService(directory.CreateRepository());
    }

    private void WriteSingleSeriesWithOneCard()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Jahr": 2016,
            "Logo": "Some Logo",
            "Thema": "Some Theme",
            "Besonderheiten": ["Highlight A"],
            "Sondereditionen": ["Edition A"],
            "Kategorien": {
              "Good_Guys": [ {"Karten-Nr.": "1", "Name": "Kai"} ]
            }
          }
        }
        """);
    }

    [Fact]
    public async Task ListSeries_MapsEverySeries_ExcludingKnownCardNames_WhenNotRequested()
    {
        WriteSingleSeriesWithOneCard();
        var service = CreateService();

        var response = await service.ListSeries(new ListSeriesRequest { IncludeKnownCardNames = false }, null!);

        var entry = Assert.Single(response.Series);
        Assert.Equal("Serie 1", entry.SeriesName);
        Assert.Equal(2016, entry.Year);
        Assert.Equal(["Highlight A"], entry.SpecialFeatures);
        Assert.Equal(["Edition A"], entry.SpecialEditions);
        Assert.Empty(entry.KnownCardNames);
    }

    [Fact]
    public async Task ListSeries_IncludesKnownCardNames_WhenRequested()
    {
        WriteSingleSeriesWithOneCard();
        var service = CreateService();

        var response = await service.ListSeries(new ListSeriesRequest { IncludeKnownCardNames = true }, null!);

        var entry = Assert.Single(response.Series);
        Assert.Equal(["Kai"], entry.KnownCardNames);
    }

    [Fact]
    public async Task GetSeries_ReturnsFoundTrueWithMappedSeries_ForExistingDifferentlyFormattedName()
    {
        WriteSingleSeriesWithOneCard();
        var service = CreateService();

        var response = await service.GetSeries(
            new GetSeriesRequest { SeriesName = "serie_1", IncludeKnownCardNames = false },
            null!);

        Assert.True(response.Found);
        Assert.Equal("Serie 1", response.Series.SeriesName);
    }

    [Fact]
    public async Task GetSeries_ReturnsFoundFalse_ForUnknownName()
    {
        WriteSingleSeriesWithOneCard();
        var service = CreateService();

        var response = await service.GetSeries(
            new GetSeriesRequest { SeriesName = "Nonexistent Series", IncludeKnownCardNames = false },
            null!);

        Assert.False(response.Found);
    }

    [Fact]
    public async Task ListAllCards_MapsEveryCard()
    {
        WriteSingleSeriesWithOneCard();
        var service = CreateService();

        var response = await service.ListAllCards(new Empty(), null!);

        var card = Assert.Single(response.Cards);
        Assert.Equal("Serie 1", card.SeriesName);
        Assert.Equal("Good Guys", card.Category);
        Assert.Equal("1", card.CardNumber);
        Assert.Equal("Kai", card.CardName);
    }

    [Fact]
    public async Task GetSeriesMetadata_ReturnsFoundTrueWithMappedMetadata_ForKnownSeries()
    {
        WriteSingleSeriesWithOneCard();
        var service = CreateService();

        var response = await service.GetSeriesMetadata(
            new GetSeriesMetadataRequest { SeriesName = "Serie 1" },
            null!);

        Assert.True(response.Found);
        Assert.Equal("Serie 1", response.Metadata.SeriesName);
        Assert.Equal(2016, response.Metadata.Year);
        Assert.Equal("Some Logo", response.Metadata.Logo);
        Assert.Equal("Some Theme", response.Metadata.Theme);
        Assert.Equal(["Highlight A"], response.Metadata.Highlights);
    }

    [Fact]
    public async Task GetSeriesMetadata_DefaultsMissingFieldsToZeroValues()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Kategorien": { "Good_Guys": [ {"Karten-Nr.": "1", "Name": "Kai"} ] }
          }
        }
        """);
        var service = CreateService();

        var response = await service.GetSeriesMetadata(
            new GetSeriesMetadataRequest { SeriesName = "Serie 1" },
            null!);

        Assert.True(response.Found);
        Assert.Equal(0, response.Metadata.Year);
        Assert.Equal(string.Empty, response.Metadata.Logo);
        Assert.Equal(string.Empty, response.Metadata.Theme);
        Assert.Empty(response.Metadata.Highlights);
    }

    [Fact]
    public async Task GetSeriesMetadata_ReturnsFoundFalse_ForUnknownSeries()
    {
        WriteSingleSeriesWithOneCard();
        var service = CreateService();

        var response = await service.GetSeriesMetadata(
            new GetSeriesMetadataRequest { SeriesName = "Nonexistent Series" },
            null!);

        Assert.False(response.Found);
    }

    [Fact]
    public async Task GetServiceInfo_ReturnsDataDirectorySeriesCountAndIsoLoadedAtUtc()
    {
        WriteSingleSeriesWithOneCard();
        var service = CreateService();

        var response = await service.GetServiceInfo(new Empty(), null!);

        Assert.Equal(directory.Path, response.DataDirectory);
        Assert.Equal(1, response.SeriesCount);
        Assert.True(DateTimeOffset.TryParse(
            response.LoadedAtUtc,
            null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out _));
    }

    public void Dispose() => directory.Dispose();
}
