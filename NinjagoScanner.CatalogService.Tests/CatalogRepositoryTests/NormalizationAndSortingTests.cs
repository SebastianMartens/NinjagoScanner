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
    public void GetSnapshot_OrdersCards_NumericFirst_ThenLE_ThenXXL_ThenOther_ByCardNumberOrName()
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

        Assert.Equal(["2", "10", "LE1", "LE3", "XXL1", "XXL2", "OTHER1"], cardNumbers);
    }

    [Fact]
    public void GetSnapshot_OrdersCards_BySeriesThenCategoryThenNumberThenName()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_2": {
            "Kategorien": { "Good_Guys": [ {"Karten-Nr.": "1", "Name": "B"} ] }
          },
          "Serie_1": {
            "Kategorien": {
              "Villains": [ {"Karten-Nr.": "1", "Name": "A"} ],
              "Good_Guys": [
                {"Karten-Nr.": "1", "Name": "Z"},
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

        Assert.Equal(
        [
            ("Serie 1", "Good Guys", "A"),
            ("Serie 1", "Good Guys", "Z"),
            ("Serie 1", "Villains", "A"),
            ("Serie 2", "Good Guys", "B"),
        ], ordered);
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
