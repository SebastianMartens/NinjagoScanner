using System.Runtime.CompilerServices;
using NinjagoScanner.CatalogService.Tests.Fixtures;

namespace NinjagoScanner.CatalogService.Tests.CatalogRepositoryTests;

/// <summary>
/// Guards the parser's category labelling and card extraction against the shipped
/// <c>cardInfos/*.json</c> data: every <c>(series, category, card number, card name, sort order)</c>
/// tuple the catalog produces must match the checked-in baseline, so a reshape of the JSON files
/// (or a parser change) that silently relabels or drops cards fails loudly.
/// <para>
/// When cards are legitimately added/renamed in the shipped data, regenerate the baseline by
/// running the tests once with the <c>UPDATE_CATALOG_SNAPSHOT=1</c> environment variable set,
/// then review the diff of <c>Fixtures/ShippedCatalogCards.snapshot.tsv</c>.
/// </para>
/// </summary>
public sealed class ShippedCatalogSnapshotTests
{
    [Fact]
    public void GetSnapshot_ProducesBaselineCardTuples_ForShippedCatalogData()
    {
        var snapshotPath = SnapshotPath();

        var actualLines = ShippedCatalogData.CreateRepository().GetSnapshot().Cards
            .Select(card => string.Join('\t', card.SeriesName, card.Category, card.CardNumber, card.CardName, card.SortOrder))
            .ToArray();

        Assert.NotEmpty(actualLines);

        if (Environment.GetEnvironmentVariable("UPDATE_CATALOG_SNAPSHOT") == "1")
        {
            File.WriteAllLines(snapshotPath, actualLines);
            return;
        }

        Assert.True(File.Exists(snapshotPath), $"Missing baseline {snapshotPath}; generate it with UPDATE_CATALOG_SNAPSHOT=1");
        var expectedLines = File.ReadAllLines(snapshotPath);

        var missing = expectedLines.Except(actualLines).Take(10).ToArray();
        var unexpected = actualLines.Except(expectedLines).Take(10).ToArray();
        Assert.True(
            missing.Length == 0 && unexpected.Length == 0,
            $"Catalog cards differ from baseline. Missing (first 10): [{string.Join(" | ", missing)}]; unexpected (first 10): [{string.Join(" | ", unexpected)}]");
        Assert.Equal(expectedLines, actualLines);
    }

    private static string SnapshotPath([CallerFilePath] string testSourceFilePath = "") => Path.Combine(
        Path.GetDirectoryName(Path.GetDirectoryName(testSourceFilePath))!,
        "Fixtures",
        "ShippedCatalogCards.snapshot.tsv");
}
