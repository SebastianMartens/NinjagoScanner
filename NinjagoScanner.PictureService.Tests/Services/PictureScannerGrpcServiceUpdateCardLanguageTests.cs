using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NinjagoScanner.PictureService;
using NinjagoScanner.PictureService.Protos;
using NinjagoScanner.PictureService.Services;
using NinjagoScanner.PictureService.Tests.Fixtures;

namespace NinjagoScanner.PictureService.Tests.Services;

public sealed class PictureScannerGrpcServiceUpdateCardLanguageTests : IDisposable
{
    private readonly string cardPhotosDirectory = Path.Combine(
        Path.GetTempPath(),
        $"NinjagoScannerPictureServiceTests_{Guid.NewGuid():N}");

    public PictureScannerGrpcServiceUpdateCardLanguageTests()
    {
        Directory.CreateDirectory(cardPhotosDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(cardPhotosDirectory))
        {
            Directory.Delete(cardPhotosDirectory, recursive: true);
        }
    }

    private static PictureScannerGrpcService CreateService(SidecarCache? sidecarCache = null)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new PictureScannerGrpcService(configuration, NullLogger<PictureScannerGrpcService>.Instance, sidecarCache ?? new SidecarCache());
    }

    private JsonDocument ReadSidecarJson(string imageFileName)
    {
        var sidecarPath = Path.Combine(cardPhotosDirectory, imageFileName + ".json");
        return JsonDocument.Parse(File.ReadAllText(sidecarPath));
    }

    [Fact]
    public async Task UpdateCardLanguage_CreatesPendingSidecar_WhenNoneExists()
    {
        var service = CreateService();

        await service.UpdateCardLanguage(
            new UpdateCardLanguageRequest
            {
                ImageFileName = "card-1.jpg",
                CardPhotosDirectory = cardPhotosDirectory,
                Language = "pl"
            },
            new FakeServerCallContext());

        using var json = ReadSidecarJson("card-1.jpg");
        Assert.Equal("pending", json.RootElement.GetProperty("AnalysisStatus").GetString());
        Assert.Equal("pl", json.RootElement.GetProperty("Language").GetString());
    }

    [Fact]
    public async Task UpdateCardLanguage_OnlyChangesLanguage_OnExistingSidecar()
    {
        var sidecarPath = Path.Combine(cardPhotosDirectory, "card-2.jpg.json");
        await File.WriteAllTextAsync(sidecarPath, """
        {
          "AnalysisStatus": "ok",
          "CardName": "Kai",
          "CardNumber": "43",
          "SetName": "Serie 9",
          "Rarity": "Common",
          "Language": "de",
          "Confidence": 0.95,
          "ReviewStatus": "verified"
        }
        """);

        var service = CreateService();

        await service.UpdateCardLanguage(
            new UpdateCardLanguageRequest
            {
                ImageFileName = "card-2.jpg",
                CardPhotosDirectory = cardPhotosDirectory,
                Language = "pl"
            },
            new FakeServerCallContext());

        using var json = ReadSidecarJson("card-2.jpg");
        var root = json.RootElement;
        Assert.Equal("pl", root.GetProperty("Language").GetString());
        Assert.Equal("ok", root.GetProperty("AnalysisStatus").GetString());
        Assert.Equal("Kai", root.GetProperty("CardName").GetString());
        Assert.Equal("43", root.GetProperty("CardNumber").GetString());
        Assert.Equal("Serie 9", root.GetProperty("SetName").GetString());
        Assert.Equal("Common", root.GetProperty("Rarity").GetString());
        Assert.Equal(0.95, root.GetProperty("Confidence").GetDouble());
        Assert.Equal("verified", root.GetProperty("ReviewStatus").GetString());
    }

    [Fact]
    public async Task UpdateCardLanguage_NormalizesBlankInput_ToAbsent()
    {
        var sidecarPath = Path.Combine(cardPhotosDirectory, "card-3.jpg.json");
        await File.WriteAllTextAsync(sidecarPath, """
        {
          "AnalysisStatus": "ok",
          "CardNumber": "43",
          "SetName": "Serie 9",
          "Language": "de"
        }
        """);

        var service = CreateService();

        await service.UpdateCardLanguage(
            new UpdateCardLanguageRequest
            {
                ImageFileName = "card-3.jpg",
                CardPhotosDirectory = cardPhotosDirectory,
                Language = "   "
            },
            new FakeServerCallContext());

        using var json = ReadSidecarJson("card-3.jpg");
        Assert.False(json.RootElement.TryGetProperty("Language", out _));
    }
}
