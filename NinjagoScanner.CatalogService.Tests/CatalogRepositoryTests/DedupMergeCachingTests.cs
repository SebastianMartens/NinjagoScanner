using NinjagoScanner.CatalogService.Catalog;
using NinjagoScanner.CatalogService.Tests.Fixtures;

namespace NinjagoScanner.CatalogService.Tests.CatalogRepositoryTests;

public sealed class DedupMergeCachingTests : IDisposable
{
    private readonly TempCatalogDirectory directory = new();

    [Fact]
    public void GetSnapshot_Throws_WhenTwoFilesDeclareTheSameSeriesName()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Kategorien": { "Heroes": { "Class": "character", "Karten": [ {"Karten-Nr.": 1, "Name": {"en": "Kai"}} ] } }
          }
        }
        """);
        directory.WriteFile("series_1_copy.json", """
        {
          "Serie 1": {
            "Kategorien": { "Heroes": { "Class": "character", "Karten": [ {"Karten-Nr.": 1, "Name": {"en": "Zane"}} ] } }
          }
        }
        """);
        var repository = directory.CreateRepository();

        var exception = Assert.Throws<CatalogDataException>(() => repository.GetSnapshot());

        Assert.Contains("Serie 1", exception.Message);
        Assert.Contains("series_1", exception.Message);
    }

    [Fact]
    public void GetSnapshot_ListsSeriesFromSeparateFilesSideBySide_WithSeriesZeroFirst()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "SortOrder": 10,
            "Kategorien": { "Heroes": { "Class": "character", "Karten": [ {"Karten-Nr.": 1, "Name": {"en": "Kai"}} ] } }
          }
        }
        """);
        directory.WriteFile("series_0_spinner.json", """
        {
          "Serie_0": {
            "SortOrder": 0,
            "Kategorien": { "Heroes": { "Class": "character", "Karten": [ {"Karten-Nr.": 1, "Name": {"en": "Spinner Kai"}} ] } }
          }
        }
        """);
        var repository = directory.CreateRepository();

        var snapshot = repository.GetSnapshot();

        Assert.Equal(["Serie 0", "Serie 1"], snapshot.Series.Select(series => series.SeriesName));
        Assert.Equal("Spinner Kai", Assert.Single(snapshot.Cards, card => card.SeriesName == "Serie 0").CardName);
        Assert.Equal("Kai", Assert.Single(snapshot.Cards, card => card.SeriesName == "Serie 1").CardName);
    }

    [Fact]
    public void GetSnapshot_CollapsesIdenticalCardEntries_IntoOne()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Kategorien": {
              "Good_Guys": { "Class": "character", "Karten": [
                {"Karten-Nr.": "1", "Name": {"de": "Kai"}},
                {"Karten-Nr.": "01", "Name": {"de": "Kai"}}
              ] }
            }
          }
        }
        """);

        var repository = directory.CreateRepository();

        Assert.Single(repository.GetSnapshot().Cards);
    }

    [Fact]
    public void GetSnapshot_CollapsesEntries_WhenNameLanguageDiffersButResolvedNameMatches()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Kategorien": {
              "Good_Guys": { "Class": "character", "Karten": [
                {"Karten-Nr.": "1", "Name": {"de": "Kai"}},
                {"Karten-Nr.": "01", "Name": {"en": "Kai"}}
              ] }
            }
          }
        }
        """);

        var repository = directory.CreateRepository();
        var card = Assert.Single(repository.GetSnapshot().Cards);

        Assert.Equal("Kai", card.CardName);
    }

    [Fact]
    public void GetSnapshot_BuildsSeriesEntry_FromDetailFile()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Jahr": 2016,
            "Kategorien": { "Good_Guys": { "Class": "character", "Karten": [ {"Karten-Nr.": "1", "Name": {"de": "Kai"}} ] } }
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
    public void GetSnapshot_PopulatesYearFeaturesAndEditions_FromDetailFileMetadata()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Jahr": 2016,
            "Besonderheiten": ["Feature A"],
            "Sondereditionen": ["Edition A"],
            "Kategorien": { "Good_Guys": { "Class": "character", "Karten": [ {"Karten-Nr.": "1", "Name": {"de": "Kai"}} ] } }
          }
        }
        """);

        var repository = directory.CreateRepository();
        var series = Assert.Single(repository.GetSnapshot().Series);

        Assert.Equal("Serie 1", series.SeriesName);
        Assert.Equal(2016, series.Year);
        Assert.Equal(["Feature A"], series.SpecialFeatures);
        Assert.Equal(["Edition A"], series.SpecialEditions);
        Assert.Equal(["Kai"], series.KnownCardNames);
    }

    [Fact]
    public void GetSnapshot_ReturnsCachedSnapshot_WhenNoFileHasChanged()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Kategorien": { "Good_Guys": { "Class": "character", "Karten": [ {"Karten-Nr.": "1", "Name": {"de": "Kai"}} ] } }
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
            "Kategorien": { "Good_Guys": { "Class": "character", "Karten": [ {"Karten-Nr.": "1", "Name": {"de": "Kai"}} ] } }
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
    public void GetSnapshot_ReturnsEmptySnapshot_WhenDataDirectoryHasNoDetailFiles()
    {
        var repository = directory.CreateRepository();
        var snapshot = repository.GetSnapshot();

        Assert.Empty(snapshot.Series);
        Assert.Empty(snapshot.Cards);
        Assert.Equal(directory.Path, snapshot.DataDirectory);
    }

    public void Dispose() => directory.Dispose();
}
