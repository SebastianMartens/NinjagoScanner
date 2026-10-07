using NinjagoScanner.CatalogService.Catalog;
using NinjagoScanner.CatalogService.Tests.Fixtures;

namespace NinjagoScanner.CatalogService.Tests.CatalogRepositoryTests;

public sealed class RarityTests : IDisposable
{
    private readonly TempCatalogDirectory directory = new();

    [Fact]
    public void GetSnapshot_GivesCardsTheirCategoryRarity()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Kategorien": {
              "Heroes": { "Class": "character", "Rarity": "common", "Karten": [
                {"Karten-Nr.": 1, "Name": {"de": "Kai"}}
              ] },
              "Special": { "Class": "limited edition", "Rarity": "limited", "Karten": [
                {"Karten-Nr.": "LE1", "Name": {"de": "Sensei Wu"}}
              ] }
            }
          }
        }
        """);

        var cards = directory.CreateRepository().GetSnapshot().Cards;

        Assert.Equal("common", cards.Single(card => card.CardNumber == "1").Rarity);
        Assert.Equal("limited", cards.Single(card => card.CardNumber == "LE1").Rarity);
    }

    [Fact]
    public void GetSnapshot_LetsACardEntryOverrideItsCategoryRarity()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Kategorien": {
              "Heroes": { "Class": "character", "Rarity": "common", "Karten": [
                {"Karten-Nr.": 1, "Name": {"de": "Kai"}},
                {"Karten-Nr.": 2, "Rarity": "legendary", "Name": {"de": "Zane"}},
                {"Karten-Nr.": 3, "Name": {"de": "Jay"}}
              ] }
            }
          }
        }
        """);

        var cards = directory.CreateRepository().GetSnapshot().Cards;

        Assert.Equal("common", cards.Single(card => card.CardNumber == "1").Rarity);
        Assert.Equal("legendary", cards.Single(card => card.CardNumber == "2").Rarity);
        Assert.Equal("common", cards.Single(card => card.CardNumber == "3").Rarity);
    }

    [Fact]
    public void GetSnapshot_FailsFast_WhenACardHasNoRarity()
    {
        directory.WriteFile("series_1.json", """
        {
          "Serie_1": {
            "Kategorien": {
              "Heroes": { "Class": "character", "Karten": [ {"Karten-Nr.": 1, "Name": {"de": "Kai"}} ] }
            }
          }
        }
        """);

        var exception = Assert.Throws<CatalogDataException>(() => directory.CreateRepository().GetSnapshot());

        Assert.Contains("series_1.json", exception.Message);
        Assert.Contains("Rarity", exception.Message);
    }

    [Theory]
    [InlineData("rare")]
    [InlineData("ultra rare")]
    public void GetSnapshot_FailsFast_WhenARarityIsOutsideTheFixedSet(string rarity)
    {
        directory.WriteFile("series_1.json", $$$"""
        {
          "Serie_1": {
            "Kategorien": {
              "Heroes": { "Class": "character", "Rarity": "{{{rarity}}}", "Karten": [ {"Karten-Nr.": 1, "Name": {"de": "Kai"}} ] }
            }
          }
        }
        """);

        var exception = Assert.Throws<CatalogDataException>(() => directory.CreateRepository().GetSnapshot());

        Assert.Contains("Rarity", exception.Message);
        Assert.Contains(rarity, exception.Message);
    }

    [Fact]
    public void GetSnapshot_SeedsShippedRarityFromClass_AcrossShippedCatalogData()
    {
        var cards = ShippedCatalogData.CreateRepository().GetSnapshot().Cards;

        Assert.NotEmpty(cards);
        Assert.All(cards, card => Assert.Equal(
            card.Class == "limited edition" ? CatalogRarities.Limited : CatalogRarities.Common,
            card.Rarity));
        Assert.Contains(cards, card => card.Rarity == CatalogRarities.Limited);
    }

    public void Dispose() => directory.Dispose();
}
