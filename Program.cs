using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
	.AddUserSecrets<Program>(optional: true)
	.AddEnvironmentVariables()
	.Build();

var config = AppConfig.Load(configuration);

if (string.IsNullOrWhiteSpace(config.ApiKey))
{
	Console.WriteLine("GEMINI_API_KEY ist nicht gesetzt.");
	Console.WriteLine("Setze den API-Key als Umgebungsvariable oder per 'dotnet user-secrets set Gemini:ApiKey <key>'.");
	return;
}

if (!Directory.Exists(config.CardPhotosDirectory))
{
	Console.WriteLine($"Der Ordner '{config.CardPhotosDirectory}' wurde nicht gefunden.");
	return;
}

var cardImages = Directory
	.EnumerateFiles(config.CardPhotosDirectory)
	.Where(path => AppConfig.SupportedExtensions.Contains(Path.GetExtension(path)))
	.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
	.ToList();

if (cardImages.Count == 0)
{
	Console.WriteLine($"Im Ordner '{config.CardPhotosDirectory}' wurden keine Kartenbilder gefunden.");
	return;
}

Console.WriteLine($"{cardImages.Count} Kartenbilder in '{config.CardPhotosDirectory}' gefunden.");
Console.WriteLine($"Verwende Gemini-Modell: {config.Model}");

using var httpClient = new HttpClient
{
	Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds)
};

var processedCount = 0;
var skippedCount = 0;
var failedCount = 0;
var uncertainCount = 0;

for (var index = 0; index < cardImages.Count; index++)
{
	var imagePath = cardImages[index];
	var sidecarPath = GetSidecarPath(imagePath);

	if (!config.OverwriteExistingSidecars && File.Exists(sidecarPath))
	{
		skippedCount++;
		Console.WriteLine($"[{index + 1}/{cardImages.Count}] Uebersprungen: {Path.GetFileName(imagePath)} (Sidecar existiert bereits)");
		continue;
	}

	Console.WriteLine($"[{index + 1}/{cardImages.Count}] Analysiere: {Path.GetFileName(imagePath)}");

	CardAnalysisResult result;
	try
	{
		result = await AnalyzeCardAsync(httpClient, config, imagePath, sidecarPath, CancellationToken.None);
	}
	catch (Exception exception)
	{
		result = CreateFailureResult(imagePath, sidecarPath, config.Model, $"Unerwarteter Fehler: {exception.Message}");
	}

	await WriteSidecarAsync(sidecarPath, result, CancellationToken.None);

	processedCount++;
	if (string.Equals(result.Status, AnalysisStatuses.Failed, StringComparison.OrdinalIgnoreCase))
	{
		failedCount++;
	}
	else if (string.Equals(result.Status, AnalysisStatuses.Uncertain, StringComparison.OrdinalIgnoreCase))
	{
		uncertainCount++;
	}

	Console.WriteLine($"    -> Status: {result.Status}, Karte: {result.CardName ?? "unbekannt"}, Nummer: {result.CardNumber ?? "-"}, Confidence: {FormatConfidence(result.Confidence)}");

	if (index < cardImages.Count - 1 && config.DelayBetweenRequestsMs > 0)
	{
		await Task.Delay(config.DelayBetweenRequestsMs);
	}
}

Console.WriteLine();
Console.WriteLine("Batch abgeschlossen:");
Console.WriteLine($"Verarbeitet: {processedCount}");
Console.WriteLine($"Uebersprungen: {skippedCount}");
Console.WriteLine($"Unsicher: {uncertainCount}");
Console.WriteLine($"Fehlgeschlagen: {failedCount}");

return;

static async Task<CardAnalysisResult> AnalyzeCardAsync(HttpClient httpClient, AppConfig config, string imagePath, string sidecarPath, CancellationToken cancellationToken)
{
	var imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
	var requestBody = CreateGeminiRequest(config, imagePath, imageBytes);
	var requestUri = $"https://generativelanguage.googleapis.com/v1beta/models/{config.Model}:generateContent?key={Uri.EscapeDataString(config.ApiKey)}";

	for (var attempt = 1; attempt <= config.MaxAttempts; attempt++)
	{
		using var response = await httpClient.PostAsJsonAsync(requestUri, requestBody, cancellationToken);
		var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

		if (response.IsSuccessStatusCode)
		{
			return ParseSuccessResponse(imagePath, sidecarPath, config.Model, responseText);
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

static object CreateGeminiRequest(AppConfig config, string imagePath, byte[] imageBytes)
{
	var prompt = """
	Du analysierst genau ein Foto einer Lego Ninjago Sammelkarte.
	Gib ausschliesslich gueltiges JSON ohne Markdown oder Codeblock zurueck.
	Wenn du dir nicht sicher bist, setze status auf \"uncertain\" und confidence entsprechend niedrig.
	Wenn das Bild keine klar lesbare einzelne Karte zeigt, setze status auf \"failed\".

	Verwende exakt dieses JSON-Schema:
	{
	  "status": "ok|uncertain|failed",
	  "cardName": "string|null",
	  "cardNumber": "string|null",
	  "setName": "string|null",
	  "rarity": "string|null",
	  "confidence": 0.0,
	  "reasoningSummary": "string",
	  "detectedText": ["string"]
	}

	Nutze sichtbare Kartennummern, Charakternamen, Set-Hinweise und Seltenheitsmerkmale.
	""";

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

static CardAnalysisResult ParseSuccessResponse(string imagePath, string sidecarPath, string model, string responseText)
{
	try
	{
		var response = JsonSerializer.Deserialize<GeminiResponseEnvelope>(responseText, JsonOptions.Default);
		var modelJson = response?.Candidates?
			.SelectMany(candidate => candidate.Content?.Parts ?? Array.Empty<GeminiPart>())
			.Select(part => part.Text)
			.FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

		if (string.IsNullOrWhiteSpace(modelJson))
		{
			return CreateFailureResult(imagePath, sidecarPath, model, "Gemini hat kein JSON-Ergebnis geliefert.", responseText);
		}

		var payload = JsonSerializer.Deserialize<GeminiCardPayload>(modelJson, JsonOptions.Default);
		if (payload is null)
		{
			return CreateFailureResult(imagePath, sidecarPath, model, "Gemini JSON konnte nicht gelesen werden.", modelJson);
		}

		var normalizedStatus = NormalizeStatus(payload.Status, payload.Confidence);
		return new CardAnalysisResult
		{
			Status = normalizedStatus,
			SourceFileName = Path.GetFileName(imagePath),
			SourceFilePath = imagePath,
			SidecarFilePath = sidecarPath,
			CardName = payload.CardName,
			CardNumber = payload.CardNumber,
			SetName = payload.SetName,
			Rarity = payload.Rarity,
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

static CardAnalysisResult CreateFailureResult(string imagePath, string sidecarPath, string model, string errorMessage, string? rawModelResponse = null)
{
	return new CardAnalysisResult
	{
		Status = AnalysisStatuses.Failed,
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

static string BuildApiErrorMessage(HttpStatusCode statusCode, string responseText, string model)
{
	if (statusCode == HttpStatusCode.NotFound && responseText.Contains("no longer available", StringComparison.OrdinalIgnoreCase))
	{
		return $"Gemini-Modell '{model}' ist nicht mehr verfuegbar. Setze Gemini:Model oder GEMINI_MODEL auf ein aktuelles Modell, z. B. 'gemini-2.5-flash'.";
	}

	return $"Gemini API Fehler ({(int)statusCode} {statusCode})";
}

static async Task WriteSidecarAsync(string sidecarPath, CardAnalysisResult result, CancellationToken cancellationToken)
{
	var json = JsonSerializer.Serialize(result, JsonOptions.Pretty);
	await File.WriteAllTextAsync(sidecarPath, json, Encoding.UTF8, cancellationToken);
}

static string GetSidecarPath(string imagePath)
{
	return imagePath + ".json";
}

static string GetMimeType(string imagePath)
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

static string NormalizeStatus(string? status, double confidence)
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

static double ClampConfidence(double value)
{
	if (double.IsNaN(value) || double.IsInfinity(value))
	{
		return 0;
	}

	return Math.Clamp(value, 0, 1);
}

static string FormatConfidence(double confidence)
{
	return ClampConfidence(confidence).ToString("0.00");
}

internal static class AnalysisStatuses
{
	public const string Ok = "ok";
	public const string Uncertain = "uncertain";
	public const string Failed = "failed";
}

internal sealed class AppConfig
{
	public static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".jpg",
		".jpeg",
		".png",
		".bmp",
		".webp"
	};

	public required string ApiKey { get; init; }
	public required string Model { get; init; }
	public required string CardPhotosDirectory { get; init; }
	public required bool OverwriteExistingSidecars { get; init; }
	public required int DelayBetweenRequestsMs { get; init; }
	public required int RetryDelayMs { get; init; }
	public required int MaxAttempts { get; init; }
	public required int TimeoutSeconds { get; init; }

	public static AppConfig Load(IConfiguration configuration)
	{
		return new AppConfig
		{
			ApiKey = configuration["Gemini:ApiKey"] ?? configuration["GEMINI_API_KEY"] ?? string.Empty,
			Model = configuration["Gemini:Model"] ?? configuration["GEMINI_MODEL"] ?? "gemini-2.5-flash",
			CardPhotosDirectory = configuration["CardPhotos:Directory"] ?? configuration["CARD_PHOTOS_DIRECTORY"] ?? Path.Combine(Environment.CurrentDirectory, "cardFotos"),
			OverwriteExistingSidecars = bool.TryParse(configuration["Scanner:OverwriteSidecars"] ?? configuration["OVERWRITE_SIDECARS"], out var overwrite) && overwrite,
			DelayBetweenRequestsMs = TryParseInt(configuration["Scanner:DelayBetweenRequestsMs"] ?? configuration["DELAY_BETWEEN_REQUESTS_MS"], 1000),
			RetryDelayMs = TryParseInt(configuration["Scanner:RetryDelayMs"] ?? configuration["RETRY_DELAY_MS"], 3000),
			MaxAttempts = Math.Max(1, TryParseInt(configuration["Scanner:MaxAttempts"] ?? configuration["MAX_ATTEMPTS"], 3)),
			TimeoutSeconds = Math.Max(10, TryParseInt(configuration["Scanner:HttpTimeoutSeconds"] ?? configuration["HTTP_TIMEOUT_SECONDS"], 90))
		};
	}

	private static int TryParseInt(string? value, int fallback)
	{
		return int.TryParse(value, out var parsedValue) ? parsedValue : fallback;
	}
	}

internal sealed class CardAnalysisResult
{
	public required string Status { get; init; }
	public required string SourceFileName { get; init; }
	public required string SourceFilePath { get; init; }
	public required string SidecarFilePath { get; init; }
	public string? CardName { get; init; }
	public string? CardNumber { get; init; }
	public string? SetName { get; init; }
	public string? Rarity { get; init; }
	public double Confidence { get; init; }
	public string? ReasoningSummary { get; init; }
	public IReadOnlyList<string> DetectedText { get; init; } = Array.Empty<string>();
	public required string AiModel { get; init; }
	public required DateTimeOffset ScannedAtUtc { get; init; }
	public string? ErrorMessage { get; init; }
	public string? RawModelResponse { get; init; }
}

internal sealed class GeminiCardPayload
{
	public string? Status { get; init; }
	public string? CardName { get; init; }
	public string? CardNumber { get; init; }
	public string? SetName { get; init; }
	public string? Rarity { get; init; }
	public double Confidence { get; init; }
	public string? ReasoningSummary { get; init; }
	public string[]? DetectedText { get; init; }
}

internal sealed class GeminiResponseEnvelope
{
	public GeminiCandidate[]? Candidates { get; init; }
}

internal sealed class GeminiCandidate
{
	public GeminiContent? Content { get; init; }
}

internal sealed class GeminiContent
{
	public GeminiPart[]? Parts { get; init; }
}

internal sealed class GeminiPart
{
	public string? Text { get; init; }
}

internal static class JsonOptions
{
	public static readonly JsonSerializerOptions Default = new()
	{
		PropertyNameCaseInsensitive = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	public static readonly JsonSerializerOptions Pretty = new(Default)
	{
		WriteIndented = true
	};
}
