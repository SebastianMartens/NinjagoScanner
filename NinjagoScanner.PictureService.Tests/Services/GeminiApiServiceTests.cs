using System.Net;
using NinjagoScanner.PictureService;

namespace NinjagoScanner.PictureService.Tests.Services;

public sealed class GeminiApiServiceTests : IDisposable
{
    private readonly string imagePath = Path.Combine(
        Path.GetTempPath(),
        $"NinjagoScannerGeminiApiServiceTests_{Guid.NewGuid():N}.jpg");

    public GeminiApiServiceTests()
    {
        File.WriteAllBytes(imagePath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
    }

    public void Dispose()
    {
        if (File.Exists(imagePath))
        {
            File.Delete(imagePath);
        }
    }

    private static ScannerConfig CreateConfig()
    {
        return new ScannerConfig
        {
            ApiKey = "test-key",
            Model = "gemini-test",
            CardPhotosDirectory = Path.GetTempPath(),
            CatalogServiceAddress = "http://localhost:5073",
            OverwriteExistingSidecars = false,
            DelayBetweenRequestsMs = 0,
            RetryDelayMs = 0,
            MaxAttempts = 1,
            TimeoutSeconds = 10
        };
    }

    private static HttpClient CreateHttpClientReturningLanguage(string language)
    {
        var candidateJson = $$"""
        {"status":"ok","cardName":"Kai","cardNumber":"1","setName":"Serie 1","rarity":"Common","language":"{{language}}","confidence":0.9,"reasoningSummary":"test","detectedText":[]}
        """;

        var envelopeJson = $$"""
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  { "text": {{System.Text.Json.JsonSerializer.Serialize(candidateJson)}} }
                ]
              }
            }
          ]
        }
        """;

        var handler = new FakeHttpMessageHandler(envelopeJson);
        return new HttpClient(handler);
    }

    [Fact]
    public async Task AnalyzeCardAsync_NormalizesPolishLanguage()
    {
        using var httpClient = CreateHttpClientReturningLanguage("pl");
        var config = CreateConfig();

        var result = await GeminiApiService.AnalyzeCardAsync(httpClient, config, Array.Empty<SeriesInfo>(), imagePath, imagePath + ".json", CancellationToken.None);

        Assert.Equal("pl", result.Language);
    }

    [Fact]
    public async Task AnalyzeCardAsync_NormalizesMixedCasePolishLanguage()
    {
        using var httpClient = CreateHttpClientReturningLanguage("PL");
        var config = CreateConfig();

        var result = await GeminiApiService.AnalyzeCardAsync(httpClient, config, Array.Empty<SeriesInfo>(), imagePath, imagePath + ".json", CancellationToken.None);

        Assert.Equal("pl", result.Language);
    }

    [Fact]
    public async Task AnalyzeCardAsync_NormalizesUnsupportedLanguage_ToUnknown()
    {
        using var httpClient = CreateHttpClientReturningLanguage("fr");
        var config = CreateConfig();

        var result = await GeminiApiService.AnalyzeCardAsync(httpClient, config, Array.Empty<SeriesInfo>(), imagePath, imagePath + ".json", CancellationToken.None);

        Assert.Equal("unknown", result.Language);
    }

    private sealed class FakeHttpMessageHandler(string responseJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            };
            return Task.FromResult(response);
        }
    }
}
