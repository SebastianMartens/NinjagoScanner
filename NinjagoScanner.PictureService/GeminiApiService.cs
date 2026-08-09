using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NinjagoScanner.PictureService;

internal static class GeminiApiService
{
    public static async Task<CardAnalysisResult> AnalyzeCardAsync(HttpClient httpClient, ScannerConfig config, IReadOnlyList<SeriesInfo> seriesCatalog, string imagePath, string sidecarPath, CancellationToken cancellationToken)
    {
        var imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        var requestBody = CreateGeminiRequest(config, seriesCatalog, imagePath, imageBytes);
        var requestUri = $"https://generativelanguage.googleapis.com/v1beta/models/{config.Model}:generateContent?key={Uri.EscapeDataString(config.ApiKey)}";

        for (var attempt = 1; attempt <= config.MaxAttempts; attempt++)
        {
            using var response = await httpClient.PostAsJsonAsync(requestUri, requestBody, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return ParseSuccessResponse(imagePath, sidecarPath, config.Model, seriesCatalog, responseText);
            }

            if ((int)response.StatusCode == 429 || response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            {
                if (attempt < config.MaxAttempts)
                {
                    await Task.Delay(config.RetryDelayMs * attempt, cancellationToken);
                    continue;
                }
            }

            return CreateFailureResult(
                imagePath,
                sidecarPath,
                config.Model,
                BuildApiErrorMessage(response.StatusCode, responseText, config.Model),
                responseText);
        }

        return CreateFailureResult(imagePath, sidecarPath, config.Model, "Unbekannter API-Fehler.");
    }

    private static object CreateGeminiRequest(ScannerConfig config, IReadOnlyList<SeriesInfo> seriesCatalog, string imagePath, byte[] imageBytes)
    {
        var seriesPrompt = SeriesCatalogService.BuildPrompt(seriesCatalog);
        var prompt = """
    Du analysierst genau ein Foto einer Lego Ninjago Sammelkarte.
    Gib ausschliesslich gueltiges JSON ohne Markdown oder Codeblock zurueck.
    Wenn du dir nicht sicher bist, setze status auf \"uncertain\" und confidence entsprechend niedrig.
    Wenn das Bild keine klar lesbare einzelne Karte zeigt, setze status auf \"failed\".
    Bestimme setName primaer ueber das Symbol in der unteren rechten Ecke der Karte.
    Wenn kein Symbol vorhanden ist, gehoert die Karte zu Serie 1.
    Bestimme language anhand des gedruckten Textes und Charakternamens auf der Karte:
    "de" fuer deutschen Text, "en" fuer englischen Text, "unknown" wenn die Sprache
    nicht sicher bestimmt werden kann.

    Verwende exakt dieses JSON-Schema:
    {
      "status": "ok|uncertain|failed",
      "cardName": "string|null",
      "cardNumber": "string|null",
      "setName": "string|null",
      "rarity": "string|null",
      "language": "de|en|unknown",
      "confidence": 0.0,
      "reasoningSummary": "string",
      "detectedText": ["string"]
    }

    Nutze sichtbare Kartennummern in der unteren linken Ecke, Charakternamen, Set-Hinweise, das Symbol unten rechts und Seltenheitsmerkmale.
    Fuelle setName nur mit einem gueltigen Seriennamen aus der folgenden Liste:
    """;

        prompt += Environment.NewLine + seriesPrompt;

        return new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = prompt },
                        new
                        {
                            inline_data = new
                            {
                                mime_type = GetMimeType(imagePath),
                                data = Convert.ToBase64String(imageBytes)
                            }
                        }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.2,
                responseMimeType = "application/json"
            }
        };
    }

    private static CardAnalysisResult ParseSuccessResponse(string imagePath, string sidecarPath, string model, IReadOnlyList<SeriesInfo> seriesCatalog, string responseText)
    {
        try
        {
            var response = JsonSerializer.Deserialize<GeminiResponseEnvelope>(responseText, ScannerJsonOptions.Default);
            var modelJson = response?.Candidates?
                .SelectMany(candidate => candidate.Content?.Parts ?? Array.Empty<GeminiPart>())
                .Select(part => part.Text)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

            if (string.IsNullOrWhiteSpace(modelJson))
            {
                return CreateFailureResult(imagePath, sidecarPath, model, "Gemini hat kein JSON-Ergebnis geliefert.", responseText);
            }

            var payload = JsonSerializer.Deserialize<GeminiCardPayload>(modelJson, ScannerJsonOptions.Default);
            if (payload is null)
            {
                return CreateFailureResult(imagePath, sidecarPath, model, "Gemini JSON konnte nicht gelesen werden.", modelJson);
            }

            var normalizedStatus = NormalizeStatus(payload.Status, payload.Confidence);
            var resolvedSetName = SeriesCatalogService.ResolveSetName(payload, seriesCatalog);
            return new CardAnalysisResult
            {
                AnalysisStatus = normalizedStatus,
                SourceFileName = Path.GetFileName(imagePath),
                SourceFilePath = imagePath,
                SidecarFilePath = sidecarPath,
                CardName = payload.CardName,
                CardNumber = payload.CardNumber,
                SetName = normalizedStatus == AnalysisStatuses.Failed ? null : resolvedSetName,
                Rarity = payload.Rarity,
                Language = NormalizeLanguage(payload.Language),
                Confidence = ClampConfidence(payload.Confidence),
                ReasoningSummary = payload.ReasoningSummary,
                DetectedText = payload.DetectedText ?? Array.Empty<string>(),
                AiModel = model,
                ScannedAtUtc = DateTimeOffset.UtcNow,
                RawModelResponse = modelJson
            };
        }
        catch (JsonException exception)
        {
            return CreateFailureResult(imagePath, sidecarPath, model, $"Gemini-Antwort war kein gueltiges JSON: {exception.Message}", responseText);
        }
    }

    private static CardAnalysisResult CreateFailureResult(string imagePath, string sidecarPath, string model, string errorMessage, string? rawModelResponse = null)
    {
        return new CardAnalysisResult
        {
            AnalysisStatus = AnalysisStatuses.Failed,
            SourceFileName = Path.GetFileName(imagePath),
            SourceFilePath = imagePath,
            SidecarFilePath = sidecarPath,
            AiModel = model,
            ScannedAtUtc = DateTimeOffset.UtcNow,
            ErrorMessage = errorMessage,
            RawModelResponse = rawModelResponse,
            DetectedText = Array.Empty<string>()
        };
    }

    private static string BuildApiErrorMessage(HttpStatusCode statusCode, string responseText, string model)
    {
        if (statusCode == HttpStatusCode.NotFound && responseText.Contains("no longer available", StringComparison.OrdinalIgnoreCase))
        {
            return $"Gemini-Modell '{model}' ist nicht mehr verfuegbar. Setze Gemini:Model oder GEMINI_MODEL auf ein aktuelles Modell, z. B. 'gemini-2.5-flash'.";
        }

        return $"Gemini API Fehler ({(int)statusCode} {statusCode})";
    }

    private static string GetMimeType(string imagePath)
    {
        return Path.GetExtension(imagePath).ToLowerInvariant() switch
        {
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    private static string NormalizeStatus(string? status, double confidence)
    {
        if (string.Equals(status, AnalysisStatuses.Failed, StringComparison.OrdinalIgnoreCase))
        {
            return AnalysisStatuses.Failed;
        }

        if (string.Equals(status, AnalysisStatuses.Uncertain, StringComparison.OrdinalIgnoreCase) || confidence < 0.65)
        {
            return AnalysisStatuses.Uncertain;
        }

        return AnalysisStatuses.Ok;
    }

    private static string NormalizeLanguage(string? language)
    {
        if (string.Equals(language, Languages.German, StringComparison.OrdinalIgnoreCase))
        {
            return Languages.German;
        }

        if (string.Equals(language, Languages.English, StringComparison.OrdinalIgnoreCase))
        {
            return Languages.English;
        }

        return Languages.Unknown;
    }

    private static double ClampConfidence(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 1);
    }
}
