using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NinjagoScanner.PictureService.Protos;
using NinjagoScanner.PictureService.Services;
using NinjagoScanner.PictureService.Tests.Fixtures;

namespace NinjagoScanner.PictureService.Tests.Services;

public sealed class PictureScannerGrpcServiceUpdateReviewStatusTests : IDisposable
{
    private readonly string cardPhotosDirectory = Path.Combine(
        Path.GetTempPath(),
        $"NinjagoScannerPictureServiceTests_{Guid.NewGuid():N}");

    public PictureScannerGrpcServiceUpdateReviewStatusTests()
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

    private static PictureScannerGrpcService CreateService()
    {
        var configuration = new ConfigurationBuilder().Build();
        return new PictureScannerGrpcService(configuration, NullLogger<PictureScannerGrpcService>.Instance);
    }

    private JsonDocument ReadSidecarJson(string imageFileName)
    {
        var sidecarPath = Path.Combine(cardPhotosDirectory, imageFileName + ".json");
        return JsonDocument.Parse(File.ReadAllText(sidecarPath));
    }

    [Fact]
    public async Task UpdateReviewStatus_CreatesPendingSidecar_WhenNoneExists()
    {
        var service = CreateService();

        await service.UpdateReviewStatus(
            new UpdateReviewStatusRequest
            {
                ImageFileName = "card-1.jpg",
                CardPhotosDirectory = cardPhotosDirectory,
                ReviewStatus = "verified"
            },
            new FakeServerCallContext());

        using var json = ReadSidecarJson("card-1.jpg");
        Assert.Equal("pending", json.RootElement.GetProperty("AnalysisStatus").GetString());
        Assert.Equal("verified", json.RootElement.GetProperty("ReviewStatus").GetString());
    }

    [Fact]
    public async Task UpdateReviewStatus_OnlyChangesReviewStatus_OnExistingSidecar()
    {
        var sidecarPath = Path.Combine(cardPhotosDirectory, "card-2.jpg.json");
        await File.WriteAllTextAsync(sidecarPath, """
        {
          "AnalysisStatus": "ok",
          "CardName": "Kai",
          "CardNumber": "43",
          "SetName": "Serie 9",
          "Rarity": "Common",
          "Confidence": 0.95,
          "ReviewStatus": "unreviewed"
        }
        """);

        var service = CreateService();

        await service.UpdateReviewStatus(
            new UpdateReviewStatusRequest
            {
                ImageFileName = "card-2.jpg",
                CardPhotosDirectory = cardPhotosDirectory,
                ReviewStatus = "incorrect"
            },
            new FakeServerCallContext());

        using var json = ReadSidecarJson("card-2.jpg");
        var root = json.RootElement;
        Assert.Equal("incorrect", root.GetProperty("ReviewStatus").GetString());
        Assert.Equal("ok", root.GetProperty("AnalysisStatus").GetString());
        Assert.Equal("Kai", root.GetProperty("CardName").GetString());
        Assert.Equal("43", root.GetProperty("CardNumber").GetString());
        Assert.Equal("Serie 9", root.GetProperty("SetName").GetString());
        Assert.Equal("Common", root.GetProperty("Rarity").GetString());
        Assert.Equal(0.95, root.GetProperty("Confidence").GetDouble());
    }
}
