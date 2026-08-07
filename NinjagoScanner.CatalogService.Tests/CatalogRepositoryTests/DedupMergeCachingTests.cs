using NinjagoScanner.CatalogService.Tests.Fixtures;

namespace NinjagoScanner.CatalogService.Tests.CatalogRepositoryTests;

public sealed class DedupMergeCachingTests : IDisposable
{
    private readonly TempCatalogDirectory directory = new();

    [Fact]
    public void GetSnapshot_CollapsesIdenticalCardEntries_IntoOne()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Kategorien": {
              "Good_Guys": [
                {"Karten-Nr.": "1", "Name": "Kai"},
                {"Karten-Nr.": "01", "Name": "Kai"}
              ]
            }
          }
        }
        """);

        var repository = directory.CreateRepository();

        Assert.Single(repository.GetSnapshot().Cards);
    }

    [Fact]
    public void GetSnapshot_MergesSeries_PresentOnlyInMainCatalog()
    {
        directory.WriteFile("series.json", """
        {
          "Ninjago_Sammelkarten_Serien": [
            {"Serie": "Serie 1", "Jahr": 2016}
          ]
        }
        """);

        var repository = directory.CreateRepository();
        var series = Assert.Single(repository.GetSnapshot().Series);

        Assert.Equal("Serie 1", series.SeriesName);
        Assert.Equal(2016, series.Year);
    }

    [Fact]
    public void GetSnapshot_MergesSeries_PresentOnlyInDetailFile()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Jahr": 2016,
            "Kategorien": { "Good_Guys": [ {"Karten-Nr.": "1", "Name": "Kai"} ] }
          }
        }
        """);

        var repository = directory.CreateRepository();
        var series = Assert.Single(repository.GetSnapshot().Series);

        Assert.Equal("Serie 1", series.SeriesName);
        Assert.Equal(2016, series.Year);
        Assert.Equal(["Kai"], series.KnownCardNames);
    }

    [Fact]
    public void GetSnapshot_MergesSeries_PresentInBoth_MainCatalogTakesPrecedenceForYearAndFeatures()
    {
        directory.WriteFile("series.json", """
        {
          "Ninjago_Sammelkarten_Serien": [
            {"Serie": "Serie 1", "Jahr": 2099, "Besonderheiten": ["From main catalog"]}
          ]
        }
        """);
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Jahr": 2016,
            "Kategorien": { "Good_Guys": [ {"Karten-Nr.": "1", "Name": "Kai"} ] }
          }
        }
        """);

        var repository = directory.CreateRepository();
        var series = Assert.Single(repository.GetSnapshot().Series);

        Assert.Equal("Serie 1", series.SeriesName);
        Assert.Equal(2099, series.Year);
        Assert.Equal(["From main catalog"], series.SpecialFeatures);
        Assert.Equal(["Kai"], series.KnownCardNames);
    }

    [Fact]
    public void GetSnapshot_ReturnsCachedSnapshot_WhenNoFileHasChanged()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Kategorien": { "Good_Guys": [ {"Karten-Nr.": "1", "Name": "Kai"} ] }
          }
        }
        """);

        var repository = directory.CreateRepository();

        var first = repository.GetSnapshot();
        var second = repository.GetSnapshot();

        Assert.Same(first, second);
    }

    [Fact]
    public void GetSnapshot_Reloads_WhenAFileTimestampChanges()
    {
        var filePath = directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Kategorien": { "Good_Guys": [ {"Karten-Nr.": "1", "Name": "Kai"} ] }
          }
        }
        """);

        var repository = directory.CreateRepository();
        var first = repository.GetSnapshot();

        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddMinutes(5));
        var second = repository.GetSnapshot();

        Assert.NotSame(first, second);
    }

    [Fact]
    public void GetSnapshot_FallsBackToEmptySnapshot_WhenMainCatalogIsMalformed()
    {
        directory.WriteFile("series.json", "{ not valid json !!!");

        var repository = directory.CreateRepository();
        var snapshot = repository.GetSnapshot();

        Assert.Empty(snapshot.Series);
        Assert.Empty(snapshot.Cards);
        Assert.Equal(directory.Path, snapshot.DataDirectory);
    }

    public void Dispose() => directory.Dispose();
}
