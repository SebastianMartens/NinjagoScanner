using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinjagoScanner.CatalogService.Catalog;

namespace NinjagoScanner.CatalogService.Tests.CatalogRepositoryTests;

/// <summary>
/// Regression guard for the shipped <c>cardInfos/*.json</c> data itself (not fixture data):
/// series name + card number must stay unique catalog-wide, since several consumers key on
/// that pair alone (see openspec/GLOSSARY.md's Card entry).
/// </summary>
public sealed class RealCatalogDataTests
{
    [Fact]
    public void GetSnapshot_HasNoDuplicateSeriesAndCardNumberPairs_AcrossShippedCatalogData()
    {
        var repository = CreateRepositoryForShippedData();
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

    private static CatalogRepository CreateRepositoryForShippedData([CallerFilePath] string testSourceFilePath = "")
    {
        var testsProjectDirectory = Path.GetDirectoryName(Path.GetDirectoryName(testSourceFilePath))!;
        var repoRoot = Path.GetDirectoryName(testsProjectDirectory)!;
        var cardInfosDirectory = Path.Combine(repoRoot, "NinjagoScanner.CatalogService", "cardInfos");

        Assert.True(Directory.Exists(cardInfosDirectory), $"Expected shipped catalog data at {cardInfosDirectory}");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Catalog:Directory"] = cardInfosDirectory
            })
            .Build();

        return new CatalogRepository(
            NullLogger<CatalogRepository>.Instance,
            Mock.Of<IWebHostEnvironment>(),
            configuration);
    }
}
