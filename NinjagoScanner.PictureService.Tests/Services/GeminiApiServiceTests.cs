using System.Net;
using NinjagoScanner.PictureService;

namespace NinjagoScanner.PictureService.Tests.Services;

public sealed class GeminiApiServiceTests
{
    private static readonly byte[] ImageBytes = [0xFF, 0xD8, 0xFF, 0xD9];

    private static ScannerConfig CreateConfig(int maxAttempts = 1)
    {
        return new ScannerConfig
        {
            ApiKey = "test-key",
            Model = "gemini-test",
            CatalogServiceAddress = "http://localhost:5073",
            OverwriteExistingSidecars = false,
            DelayBetweenRequestsMs = 0,
            RetryDelayMs = 0,
            MaxAttempts = maxAttempts,
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

        var result = await GeminiApiService.AnalyzeCardAsync(httpClient, config, Array.Empty<SeriesInfo>(), "card-1", "card-1.jpg", ImageBytes, CancellationToken.None);

        Assert.Equal("pl", result.Language);
    }

    [Fact]
    public async Task AnalyzeCardAsync_NormalizesMixedCasePolishLanguage()
    {
        using var httpClient = CreateHttpClientReturningLanguage("PL");
        var config = CreateConfig();

        var result = await GeminiApiService.AnalyzeCardAsync(httpClient, config, Array.Empty<SeriesInfo>(), "card-1", "card-1.jpg", ImageBytes, CancellationToken.None);

        Assert.Equal("pl", result.Language);
    }

    [Fact]
    public async Task AnalyzeCardAsync_NormalizesUnsupportedLanguage_ToUnknown()
    {
        using var httpClient = CreateHttpClientReturningLanguage("fr");
        var config = CreateConfig();

        var result = await GeminiApiService.AnalyzeCardAsync(httpClient, config, Array.Empty<SeriesInfo>(), "card-1", "card-1.jpg", ImageBytes, CancellationToken.None);

        Assert.Equal("unknown", result.Language);
    }

    [Fact]
    public async Task AnalyzeCardAsync_MarksTransportFailure_WhenRetriesExhaustedAgainst429()
    {
        using var httpClient = new HttpClient(new FakeStatusCodeHandler(HttpStatusCode.TooManyRequests));
        var config = CreateConfig(maxAttempts: 2);

        var result = await GeminiApiService.AnalyzeCardAsync(httpClient, config, Array.Empty<SeriesInfo>(), "card-1", "card-1.jpg", ImageBytes, CancellationToken.None);

        Assert.Equal("failed", result.AnalysisStatus);
        Assert.True(result.IsTransportFailure);
    }

    [Fact]
    public async Task AnalyzeCardAsync_MarksTransportFailure_WhenRetriesExhaustedAgainst5xx()
    {
        using var httpClient = new HttpClient(new FakeStatusCodeHandler(HttpStatusCode.ServiceUnavailable));
        var config = CreateConfig(maxAttempts: 2);

        var result = await GeminiApiService.AnalyzeCardAsync(httpClient, config, Array.Empty<SeriesInfo>(), "card-1", "card-1.jpg", ImageBytes, CancellationToken.None);

        Assert.Equal("failed", result.AnalysisStatus);
        Assert.True(result.IsTransportFailure);
    }

    [Fact]
    public async Task AnalyzeCardAsync_MarksTransportFailure_OnImmediateNonRetryableStatus()
    {
        using var httpClient = new HttpClient(new FakeStatusCodeHandler(HttpStatusCode.BadRequest));
        var config = CreateConfig();

        var result = await GeminiApiService.AnalyzeCardAsync(httpClient, config, Array.Empty<SeriesInfo>(), "card-1", "card-1.jpg", ImageBytes, CancellationToken.None);

        Assert.Equal("failed", result.AnalysisStatus);
        Assert.True(result.IsTransportFailure);
    }

    [Fact]
    public async Task AnalyzeCardAsync_DoesNotMarkTransportFailure_OnMalformedJson()
    {
        var handler = new FakeHttpMessageHandler("not valid json at all");
        using var httpClient = new HttpClient(handler);
        var config = CreateConfig();

        var result = await GeminiApiService.AnalyzeCardAsync(httpClient, config, Array.Empty<SeriesInfo>(), "card-1", "card-1.jpg", ImageBytes, CancellationToken.None);

        Assert.Equal("failed", result.AnalysisStatus);
        Assert.False(result.IsTransportFailure);
    }

    [Fact]
    public async Task AnalyzeCardAsync_DoesNotMarkTransportFailure_WhenModelReportsFailed()
    {
        var candidateJson = """
        {"status":"failed","cardName":null,"cardNumber":null,"setName":null,"rarity":null,"language":"unknown","confidence":0.1,"reasoningSummary":"kein lesbares Motiv","detectedText":[]}
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
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(envelopeJson));
        var config = CreateConfig();

        var result = await GeminiApiService.AnalyzeCardAsync(httpClient, config, Array.Empty<SeriesInfo>(), "card-1", "card-1.jpg", ImageBytes, CancellationToken.None);

        Assert.Equal("failed", result.AnalysisStatus);
        Assert.False(result.IsTransportFailure);
    }

    [Fact]
    public async Task AnalyzeCardAsync_DoesNotMarkTransportFailure_WhenSeriesMatchEscalates()
    {
        var candidateJson = """
        {"status":"ok","cardName":"Kai","cardNumber":"1","setName":"Unbekannte Serie","rarity":"Common","language":"de","confidence":0.9,"reasoningSummary":"test","detectedText":[]}
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
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(envelopeJson));
        var config = CreateConfig();
        var seriesCatalog = new[] { new SeriesInfo { Serie = "Serie 5", CardNames = ["Zane"] } };

        var result = await GeminiApiService.AnalyzeCardAsync(httpClient, config, seriesCatalog, "card-1", "card-1.jpg", ImageBytes, CancellationToken.None);

        Assert.Equal("failed", result.AnalysisStatus);
        Assert.False(result.IsTransportFailure);
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

    private sealed class FakeStatusCodeHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("{}")
            };
            return Task.FromResult(response);
        }
    }
}
