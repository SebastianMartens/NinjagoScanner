using NinjagoScanner.CatalogService.Tests.Fixtures;

namespace NinjagoScanner.CatalogService.Tests.CatalogRepositoryTests;

/// <summary>
/// Regression guard for the shipped <c>cardInfos/*.json</c> data itself (not fixture data):
/// series name + card number must stay unique catalog-wide, since several consumers key on
/// that pair alone (see openspec/GLOSSARY.md's Card entry), and every category name must map to
/// one class from the fixed set (see openspec/GLOSSARY.md's Card Class entry).
/// </summary>
public sealed class RealCatalogDataTests
{
    private static readonly string[] AllowedClasses =
        ["character", "action", "vehicle", "puzzle-piece", "trap", "limited edition", "art"];

    [Fact]
    public void GetSnapshot_HasNoDuplicateSeriesAndCardNumberPairs_AcrossShippedCatalogData()
    {
        var repository = ShippedCatalogData.CreateRepository();
        var cards = repository.GetSnapshot().Cards;

        Assert.NotEmpty(cards);

        var duplicates = cards
            .GroupBy(card => (card.SeriesName, card.CardNumber))
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.True(
            duplicates.Length == 0,
            $"Found catalog cards sharing (series, card number): {string.Join(", ", duplicates.Select(key => $"{key.SeriesName} #{key.CardNumber}"))}");
    }

    [Fact]
    public void GetSnapshot_ListsSeriesZeroBeforeSeriesOne_AndKeepsBothSeriesCards_AcrossShippedCatalogData()
    {
        var snapshot = ShippedCatalogData.CreateRepository().GetSnapshot();

        var seriesNames = snapshot.Series.Select(series => series.SeriesName).ToList();
        Assert.Contains("Serie 0", seriesNames);
        Assert.Contains("Serie 1", seriesNames);
        Assert.True(seriesNames.IndexOf("Serie 0") < seriesNames.IndexOf("Serie 1"));

        // Series 0 ships its real cards; Series 1 must not lose any to the Series 0 file.
        Assert.Equal(221, snapshot.Cards.Count(card => card.SeriesName == "Serie 0"));
        Assert.Equal(200, snapshot.Cards.Count(card => card.SeriesName == "Serie 1"));
    }

    [Fact]
    public void GetSnapshot_GivesEveryCardAClassFromTheFixedSet_AcrossShippedCatalogData()
    {
        var cards = ShippedCatalogData.CreateRepository().GetSnapshot().Cards;

        Assert.NotEmpty(cards);

        var unknown = cards
            .Where(card => !AllowedClasses.Contains(card.Class))
            .Select(card => $"{card.SeriesName} / {card.Category} => '{card.Class}'")
            .Distinct()
            .ToArray();

        Assert.True(unknown.Length == 0, $"Cards with a class outside the fixed set: {string.Join("; ", unknown)}");
    }

    [Fact]
    public void GetSnapshot_MapsEveryCategoryNameToOneClass_AcrossAllShippedSeriesFiles()
    {
        var cards = ShippedCatalogData.CreateRepository().GetSnapshot().Cards;

        // Nested categories (e.g. "Puzzle Cards / Puzzle One") belong to the class declared on
        // their top-level category, so group by the top-level segment of the label.
        var inconsistent = cards
            .GroupBy(card => card.Category.Split(" / ")[0])
            .Select(group => (Category: group.Key, Classes: group.Select(card => card.Class).Distinct().Order().ToArray()))
            .Where(group => group.Classes.Length > 1)
            .Select(group => $"{group.Category} => [{string.Join(", ", group.Classes)}]")
            .ToArray();

        Assert.True(inconsistent.Length == 0, $"Category names declared with different classes across series files: {string.Join("; ", inconsistent)}");
    }
}
