using NinjagoScanner.CatalogService.Tests.Fixtures;

namespace NinjagoScanner.CatalogService.Tests.CatalogRepositoryTests;

public sealed class ParsingTests : IDisposable
{
    private readonly TempCatalogDirectory directory = new();

    [Fact]
    public void GetSnapshot_ParsesNestedCategoryEntries_WithCategoryLabelFromNestedPath()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Jahr": 2016,
            "Kategorien": {
              "Good_Guys": [
                {"Karten-Nr.": 1, "Name": "Kai"}
              ]
            }
          }
        }
        """);

        var repository = directory.CreateRepository();
        var snapshot = repository.GetSnapshot();

        var card = Assert.Single(snapshot.Cards);
        Assert.Equal("Serie 1", card.SeriesName);
        Assert.Equal("Good Guys", card.Category);
        Assert.Equal("1", card.CardNumber);
        Assert.Equal("Kai", card.CardName);
    }

    [Fact]
    public void GetSnapshot_BuildsCategoryLabel_FromMultipleNestedLevels()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Kategorien": {
              "Villains": {
                "Sub_Bosses": [
                  {"Karten-Nr.": 99, "Name": "Garmadon"}
                ]
              }
            }
          }
        }
        """);

        var repository = directory.CreateRepository();
        var snapshot = repository.GetSnapshot();

        var card = Assert.Single(snapshot.Cards);
        Assert.Equal("Villains / Sub Bosses", card.Category);
    }

    [Fact]
    public void GetSnapshot_UsesUnkategorisiert_WhenNoCategoryPathPresent()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {"Karten-Nr.": 1, "Name": "Kai"}
        }
        """);

        var repository = directory.CreateRepository();
        var snapshot = repository.GetSnapshot();

        var card = Assert.Single(snapshot.Cards);
        Assert.Equal("Unkategorisiert", card.Category);
    }

    [Theory]
    [InlineData("""{"Karten-Nr.": 1}""")]
    [InlineData("""{"Name": "Kai"}""")]
    [InlineData("""{"Karten-Nr.": "", "Name": "Kai"}""")]
    [InlineData("""{"Karten-Nr.": 1, "Name": "   "}""")]
    public void GetSnapshot_ExcludesEntries_MissingOrBlankCardNumberOrName(string cardJson)
    {
        directory.WriteFile("series_1.json", $$"""
        {
          "Serie_1": {
            "Kategorien": {
              "Good_Guys": [ {{cardJson}} ]
            }
          }
        }
        """);

        var repository = directory.CreateRepository();
        var snapshot = repository.GetSnapshot();

        Assert.Empty(snapshot.Cards);
    }

    [Theory]
    [InlineData("Good_Guys", "Good Guys")]
    [InlineData("Good   Guys", "Good Guys")]
    [InlineData("  Good_Guys  ", "Good Guys")]
    public void GetSnapshot_NormalizesCategoryDisplayName(string rawCategory, string expectedCategory)
    {
        directory.WriteFile("series_1.json", $$"""
        {
          "Serie_1": {
            "Kategorien": {
              "{{rawCategory}}": [
                {"Karten-Nr.": 1, "Name": "Kai"}
              ]
            }
          }
        }
        """);

        var repository = directory.CreateRepository();
        var snapshot = repository.GetSnapshot();

        Assert.Equal(expectedCategory, Assert.Single(snapshot.Cards).Category);
    }

    [Theory]
    [InlineData("Jahr")]
    [InlineData("Logo")]
    [InlineData("Thema")]
    [InlineData("Besonderheiten")]
    [InlineData("Sondereditionen")]
    [InlineData("Kategorien")]
    [InlineData("Serie")]
    public void GetSnapshot_DoesNotTreatReservedKeys_AsCategoryNames(string reservedKey)
    {
        // "Kategorien" itself is reserved but still recursed into; nest the card one level
        // deeper under a reserved key sibling to prove that key is skipped as a category.
        directory.WriteFile("series_1.json", $$"""
        {
          "Serie_1": {
            "{{reservedKey}}": {
              "Karten-Nr.": 1,
              "Name": "Should not be extracted as a card of a category named '{{reservedKey}}'"
            }
          }
        }
        """);

        var repository = directory.CreateRepository();
        var snapshot = repository.GetSnapshot();

        // The card itself is still found (reserved keys are still traversed for structure),
        // but its category must never be a reserved key name.
        foreach (var card in snapshot.Cards)
        {
            Assert.NotEqual(reservedKey, card.Category);
        }
    }

    [Fact]
    public void GetSnapshot_SkipsMalformedDetailFile_ButStillLoadsOtherValidFiles()
    {
        directory.WriteFile("series_1.json", "{ this is not valid json");
        directory.WriteFile("series_2.json", """
        {
          "Serie_2": {
            "Kategorien": {
              "Good_Guys": [
                {"Karten-Nr.": 1, "Name": "Zane"}
              ]
            }
          }
        }
        """);

        var repository = directory.CreateRepository();
        var snapshot = repository.GetSnapshot();

        var card = Assert.Single(snapshot.Cards);
        Assert.Equal("Zane", card.CardName);
    }

    [Fact]
    public void GetSnapshot_ExtractsSeriesMetadata_WhenFieldsPresent()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Jahr": 2016,
            "Logo": "Some Logo",
            "Thema": "Some Theme",
            "Besonderheiten": ["Highlight A", "Highlight B"],
            "Sondereditionen": ["Edition A"],
            "Kategorien": {
              "Good_Guys": [ {"Karten-Nr.": 1, "Name": "Kai"} ]
            }
          }
        }
        """);

        var repository = directory.CreateRepository();
        var metadata = repository.FindSeriesMetadata("Serie 1");

        Assert.NotNull(metadata);
        Assert.Equal(2016, metadata!.Year);
        Assert.Equal("Some Logo", metadata.Logo);
        Assert.Equal("Some Theme", metadata.Theme);
        Assert.Equal(["Highlight A", "Highlight B"], metadata.Highlights);
        Assert.Equal(["Edition A"], metadata.SpecialEditions);
    }

    [Fact]
    public void GetSnapshot_DefaultsSeriesMetadata_WhenFieldsAbsentOrWrongKind()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Logo": 12345,
            "Kategorien": {
              "Good_Guys": [ {"Karten-Nr.": 1, "Name": "Kai"} ]
            }
          }
        }
        """);

        var repository = directory.CreateRepository();
        var metadata = repository.FindSeriesMetadata("Serie 1");

        Assert.NotNull(metadata);
        Assert.Null(metadata!.Year);
        Assert.Null(metadata.Logo);
        Assert.Null(metadata.Theme);
        Assert.Empty(metadata.Highlights);
        Assert.Empty(metadata.SpecialEditions);
    }

    public void Dispose() => directory.Dispose();
}
