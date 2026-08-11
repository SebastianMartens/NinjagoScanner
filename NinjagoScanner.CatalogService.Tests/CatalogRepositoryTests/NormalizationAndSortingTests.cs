using NinjagoScanner.CatalogService.Tests.Fixtures;

namespace NinjagoScanner.CatalogService.Tests.CatalogRepositoryTests;

public sealed class NormalizationAndSortingTests : IDisposable
{
    private readonly TempCatalogDirectory directory = new();

    [Theory]
    [InlineData("007", "7")]
    [InlineData("42", "42")]
    [InlineData("le5", "LE5")]
    [InlineData("xxl-3", "XXL3")]
    [InlineData(" 12 ", "12")]
    public void GetSnapshot_NormalizesCardNumber(string rawNumber, string expectedNumber)
    {
        directory.WriteFile("series_1.json", $$"""
        {
          "Serie_1": {
            "Kategorien": {
              "Good_Guys": [
                {"Karten-Nr.": "{{rawNumber}}", "Name": "Kai"}
              ]
            }
          }
        }
        """);

        var repository = directory.CreateRepository();
        var card = Assert.Single(repository.GetSnapshot().Cards);

        Assert.Equal(expectedNumber, card.CardNumber);
    }

    [Fact]
    public void GetSnapshot_OrdersCards_NumericFirst_ThenAlphanumericByPrefixThenNumber_ThenOther()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Kategorien": {
              "Good_Guys": [
                {"Karten-Nr.": "XXL2", "Name": "XXL Two"},
                {"Karten-Nr.": "OTHER1", "Name": "Other One"},
                {"Karten-Nr.": "10", "Name": "Ten"},
                {"Karten-Nr.": "LE3", "Name": "LE Three"},
                {"Karten-Nr.": "2", "Name": "Two"},
                {"Karten-Nr.": "LE1", "Name": "LE One"},
                {"Karten-Nr.": "XXL1", "Name": "XXL One"}
              ]
            }
          }
        }
        """);

        var repository = directory.CreateRepository();
        var cardNumbers = repository.GetSnapshot().Cards.Select(card => card.CardNumber).ToArray();

        // "OTHER1" is a valid alphabetic-prefix-plus-number card number, so it sorts into the
        // alphanumeric group by its own prefix ("OTHER" falls between "LE" and "XXL"
        // alphabetically) instead of a separate catch-all group.
        Assert.Equal(["2", "10", "LE1", "LE3", "OTHER1", "XXL1", "XXL2"], cardNumbers);
    }

    [Fact]
    public void GetSnapshot_OrdersCards_NovelPrefix_SortsByItsOwnPrefixAmongKnownPrefixes()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Kategorien": {
              "Good_Guys": [
                {"Karten-Nr.": "XXL1", "Name": "XXL One"},
                {"Karten-Nr.": "AB1", "Name": "AB One"},
                {"Karten-Nr.": "LE1", "Name": "LE One"},
                {"Karten-Nr.": "1", "Name": "One"}
              ]
            }
          }
        }
        """);

        var repository = directory.CreateRepository();
        var cardNumbers = repository.GetSnapshot().Cards.Select(card => card.CardNumber).ToArray();

        // "AB" is a prefix the app has never hardcoded for; it must still sort correctly among
        // the others purely by alphabetical prefix comparison ("AB" before "LE" before "XXL").
        Assert.Equal(["1", "AB1", "LE1", "XXL1"], cardNumbers);
    }

    [Fact]
    public void GetSnapshot_OrdersCards_NonConformingCardNumber_SortsAfterAlphanumericGroup()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Kategorien": {
              "Good_Guys": [
                {"Karten-Nr.": "XXL1", "Name": "XXL One"},
                {"Karten-Nr.": "1A2B", "Name": "Non Conforming"},
                {"Karten-Nr.": "1", "Name": "One"}
              ]
            }
          }
        }
        """);

        var repository = directory.CreateRepository();
        var cardNumbers = repository.GetSnapshot().Cards.Select(card => card.CardNumber).ToArray();

        // "1A2B" matches neither the purely-numeric nor the prefix-plus-number pattern, so it
        // falls into the trailing catch-all group, ordered by raw text.
        Assert.Equal(["1", "XXL1", "1A2B"], cardNumbers);
    }

    [Fact]
    public void GetSnapshot_OrdersCards_BySortOrderThenCardNumberThenName_CategoryIsNotASortKey()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_2": {
            "SortOrder": 20,
            "Kategorien": { "Good_Guys": [ {"Karten-Nr.": "1", "Name": "B"} ] }
          },
          "Serie_1": {
            "SortOrder": 10,
            "Kategorien": {
              "Villains": [ {"Karten-Nr.": "1", "Name": "V-One"} ],
              "Good_Guys": [
                {"Karten-Nr.": "2", "Name": "G-Two"},
                {"Karten-Nr.": "1", "Name": "A"}
              ]
            }
          }
        }
        """);

        var repository = directory.CreateRepository();
        var ordered = repository.GetSnapshot().Cards
            .Select(card => (card.SeriesName, card.Category, card.CardName))
            .ToArray();

        // Within Serie 1, "Villains" card #1 sorts ahead of "Good Guys" card #2 - card number
        // wins over category, even though "Good Guys" sorts alphabetically before "Villains".
        // The two cards tied on card number #1 ("Good Guys"/A vs "Villains"/V-One) fall back to
        // card name.
        Assert.Equal(
        [
            ("Serie 1", "Good Guys", "A"),
            ("Serie 1", "Villains", "V-One"),
            ("Serie 1", "Good Guys", "G-Two"),
            ("Serie 2", "Good Guys", "B"),
        ], ordered);
    }

    [Fact]
    public void GetSnapshot_OrdersCards_CardNumberWinsOverCategory_EvenWhenAnEarlierCategoryStartsLater()
    {
        // Mirrors the real catalog shape: "Action Cards" sorts alphabetically before "Heroes",
        // but its card numbers start at 101 while "Heroes" starts at 1.
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Kategorien": {
              "Action_Cards": [
                {"Karten-Nr.": 101, "Name": "First Action Card"}
              ],
              "Heroes": [
                {"Karten-Nr.": 1, "Name": "First Hero"},
                {"Karten-Nr.": 2, "Name": "Second Hero"}
              ]
            }
          }
        }
        """);

        var repository = directory.CreateRepository();
        var cardNumbers = repository.GetSnapshot().Cards.Select(card => card.CardNumber).ToArray();

        Assert.Equal(["1", "2", "101"], cardNumbers);
    }

    [Fact]
    public void GetSnapshot_OrdersCardsBySortOrder_NotAlphabeticalSeriesName()
    {
        // "Serie 10" sorts before "Serie 2" alphabetically, but its SortOrder (100) places it after
        // "Serie 2" (SortOrder 20) - the catalog's curated order must win over the name string.
        directory.WriteFile("series_1.json", """
        {
          "Serie_10": {
            "SortOrder": 100,
            "Kategorien": { "Good_Guys": [ {"Karten-Nr.": "1", "Name": "Ten"} ] }
          },
          "Serie_2": {
            "SortOrder": 20,
            "Kategorien": { "Good_Guys": [ {"Karten-Nr.": "1", "Name": "Two"} ] }
          }
        }
        """);

        var repository = directory.CreateRepository();
        var orderedSeriesNames = repository.GetSnapshot().Cards
            .Select(card => card.SeriesName)
            .ToArray();

        Assert.Equal(["Serie 2", "Serie 10"], orderedSeriesNames);
    }

    [Fact]
    public void GetSnapshot_OrdersSeriesList_BySortOrder_NotAlphabeticalSeriesName()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_10": {
            "SortOrder": 100,
            "Kategorien": { "Good_Guys": [ {"Karten-Nr.": "1", "Name": "Ten"} ] }
          },
          "Serie_2": {
            "SortOrder": 20,
            "Kategorien": { "Good_Guys": [ {"Karten-Nr.": "1", "Name": "Two"} ] }
          }
        }
        """);

        var repository = directory.CreateRepository();
        var orderedSeriesNames = repository.GetSnapshot().Series
            .Select(series => series.SeriesName)
            .ToArray();

        Assert.Equal(["Serie 2", "Serie 10"], orderedSeriesNames);
    }

    [Fact]
    public void GetSnapshot_SeriesSortOrderDefaultsToZero_WhenFieldOmitted()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Kategorien": { "Good_Guys": [ {"Karten-Nr.": "1", "Name": "Kai"} ] }
          }
        }
        """);

        var repository = directory.CreateRepository();
        var series = Assert.Single(repository.GetSnapshot().Series);
        var card = Assert.Single(repository.GetSnapshot().Cards);

        Assert.Equal(0, series.SortOrder);
        Assert.Equal(0, card.SortOrder);
    }

    [Theory]
    [InlineData("Serie 1", "serie 1")]
    [InlineData("Serie 1", "SERIE 1")]
    [InlineData("Serie_1", "Serie 1")]
    [InlineData("Serie-1", "Serie 1")]
    [InlineData("Serie   1", "Serie 1")]
    public void FindByName_MatchesRegardlessOfCaseUnderscoreHyphenOrWhitespace(string writtenName, string lookupName)
    {
        directory.WriteFile("series_1.json", $$"""
        {
          "{{writtenName.Replace(' ', '_')}}": {
            "Kategorien": { "Good_Guys": [ {"Karten-Nr.": "1", "Name": "Kai"} ] }
          }
        }
        """);

        var repository = directory.CreateRepository();

        Assert.NotNull(repository.FindByName(lookupName));
        Assert.NotNull(repository.FindSeriesMetadata(lookupName));
    }

    public void Dispose() => directory.Dispose();
}
