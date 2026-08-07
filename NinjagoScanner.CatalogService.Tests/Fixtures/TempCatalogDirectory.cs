using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinjagoScanner.CatalogService.Catalog;

namespace NinjagoScanner.CatalogService.Tests.Fixtures;

/// <summary>
/// Creates a temp directory of catalog JSON files, and a <see cref="CatalogRepository"/> wired
/// to read from it via <c>Catalog:Directory</c> configuration, so tests can drive the repository's
/// public API (GetSnapshot/FindByName/FindSeriesMetadata) against controlled fixture data.
/// </summary>
public sealed class TempCatalogDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        $"NinjagoScannerCatalogTests_{Guid.NewGuid():N}");

    public TempCatalogDirectory()
    {
        Directory.CreateDirectory(Path);
    }

    public string WriteFile(string fileName, string content)
    {
        var filePath = System.IO.Path.Combine(Path, fileName);
        File.WriteAllText(filePath, content, Encoding.UTF8);
        return filePath;
    }

    public CatalogRepository CreateRepository()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Catalog:Directory"] = Path
            })
            .Build();

        return new CatalogRepository(
            NullLogger<CatalogRepository>.Instance,
            Mock.Of<IWebHostEnvironment>(),
            configuration);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
