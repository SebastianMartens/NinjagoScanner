using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinjagoScanner.CatalogService.Catalog;

namespace NinjagoScanner.CatalogService.Tests.Fixtures;

/// <summary>
/// Locates the shipped <c>cardInfos/*.json</c> data (not fixture data) relative to this source
/// file, and builds a <see cref="CatalogRepository"/> reading from it.
/// </summary>
public static class ShippedCatalogData
{
    public static string CardInfosDirectory([CallerFilePath] string fixtureSourceFilePath = "")
    {
        var testsProjectDirectory = Path.GetDirectoryName(Path.GetDirectoryName(fixtureSourceFilePath))!;
        var repoRoot = Path.GetDirectoryName(testsProjectDirectory)!;
        var cardInfosDirectory = Path.Combine(repoRoot, "NinjagoScanner.CatalogService", "cardInfos");

        Assert.True(Directory.Exists(cardInfosDirectory), $"Expected shipped catalog data at {cardInfosDirectory}");
        return cardInfosDirectory;
    }

    public static CatalogRepository CreateRepository()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Catalog:Directory"] = CardInfosDirectory()
            })
            .Build();

        return new CatalogRepository(
            NullLogger<CatalogRepository>.Instance,
            Mock.Of<IWebHostEnvironment>(),
            configuration);
    }
}
