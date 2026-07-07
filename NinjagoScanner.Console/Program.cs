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
var seriesCatalog = LoadSeriesCatalog(config.SeriesCatalogPath);

if (string.IsNullOrWhiteSpace(config.ApiKey))
{
	Console.WriteLine("GEMINI_API_KEY ist nicht gesetzt.");
	Console.WriteLine("Setze den API-Key als Umgebungsvariable oder per 'dotnet user-secrets set Gemini:ApiKey <key>'.");
	return;
}

if (!Directory.Exists(config.CardPhotosDirectory))
{
	Console.WriteLine($"Der Ordner '{config.CardPhotosDirectory}' wurde nicht gefunden.");
	Console.WriteLine("Gepruefte Standardpfade:");
	foreach (var candidate in AppConfig.GetDefaultCardPhotosCandidates())
	{
		Console.WriteLine($"- {candidate}");
	}
	return;
}

Console.WriteLine($"Verwende Kartenordner: {config.CardPhotosDirectory}");
Console.WriteLine($"Verwende Serienkatalog: {config.SeriesCatalogPath}");

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
		result = await AnalyzeCardAsync(httpClient, config, seriesCatalog, imagePath, sidecarPath, CancellationToken.None);
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

static async Task<CardAnalysisResult> AnalyzeCardAsync(HttpClient httpClient, AppConfig config, IReadOnlyList<SeriesInfo> seriesCatalog, string imagePath, string sidecarPath, CancellationToken cancellationToken)
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

static object CreateGeminiRequest(AppConfig config, IReadOnlyList<SeriesInfo> seriesCatalog, string imagePath, byte[] imageBytes)
{
	var seriesPrompt = BuildSeriesPrompt(seriesCatalog);
	var prompt = """
	Du analysierst genau ein Foto einer Lego Ninjago Sammelkarte.
	Gib ausschliesslich gueltiges JSON ohne Markdown oder Codeblock zurueck.
	Wenn du dir nicht sicher bist, setze status auf \"uncertain\" und confidence entsprechend niedrig.
	Wenn das Bild keine klar lesbare einzelne Karte zeigt, setze status auf \"failed\".
	Bestimme setName primaer ueber das Symbol in der unteren rechten Ecke der Karte.
	Wenn kein Symbol vorhanden ist, gehoert die Karte zu Serie 1.

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

	Nutze sichtbare Kartennummern, Charakternamen, Set-Hinweise, das Symbol unten rechts und Seltenheitsmerkmale.
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

static CardAnalysisResult ParseSuccessResponse(string imagePath, string sidecarPath, string model, IReadOnlyList<SeriesInfo> seriesCatalog, string responseText)
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
		var resolvedSetName = ResolveSetName(payload, seriesCatalog);
		return new CardAnalysisResult
		{
			Status = normalizedStatus,
			SourceFileName = Path.GetFileName(imagePath),
			SourceFilePath = imagePath,
			SidecarFilePath = sidecarPath,
			CardName = payload.CardName,
			CardNumber = payload.CardNumber,
			SetName = normalizedStatus == AnalysisStatuses.Failed ? null : resolvedSetName,
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

static IReadOnlyList<SeriesInfo> LoadSeriesCatalog(string seriesCatalogPath)
{
	if (!File.Exists(seriesCatalogPath))
	{
		Console.WriteLine($"Warnung: Serienkatalog '{seriesCatalogPath}' wurde nicht gefunden.");
		return Array.Empty<SeriesInfo>();
	}

	try
	{
		var json = File.ReadAllText(seriesCatalogPath, Encoding.UTF8);
		var catalog = JsonSerializer.Deserialize<SeriesCatalogRoot>(json, JsonOptions.Default);
		return catalog?.Series?
			.Where(series => !string.IsNullOrWhiteSpace(series.Serie))
			.ToArray() ?? Array.Empty<SeriesInfo>();
	}
	catch (Exception exception)
	{
		Console.WriteLine($"Warnung: Serienkatalog konnte nicht geladen werden: {exception.Message}");
		return Array.Empty<SeriesInfo>();
	}
}

static string BuildSeriesPrompt(IReadOnlyList<SeriesInfo> seriesCatalog)
{
	if (seriesCatalog.Count == 0)
	{
		return "- Serie 1: kein Symbol";
	}

	var builder = new StringBuilder();

	foreach (var series in seriesCatalog)
	{
		var symbolHint = ExtractSeriesSymbolHint(series);
		builder.Append("- ")
			.Append(series.Serie)
			.Append(": ")
			.Append(symbolHint);

		if (series.Jahr > 0)
		{
			builder.Append(" (")
				.Append(series.Jahr)
				.Append(')');
		}

		builder.AppendLine();
	}

	return builder.ToString().TrimEnd();
}

static string? ResolveSetName(GeminiCardPayload payload, IReadOnlyList<SeriesInfo> seriesCatalog)
{
	if (seriesCatalog.Count == 0)
	{
		return payload.SetName?.Trim();
	}

	var exactMatch = FindSeriesByName(seriesCatalog, payload.SetName);
	if (exactMatch is not null)
	{
		return exactMatch.Serie;
	}

	var inferredMatch = FindSeriesByEvidence(seriesCatalog, payload.SetName, payload.ReasoningSummary, payload.DetectedText);
	return inferredMatch?.Serie;
}

static SeriesInfo? FindSeriesByName(IReadOnlyList<SeriesInfo> seriesCatalog, string? candidate)
{
	if (string.IsNullOrWhiteSpace(candidate))
	{
		return null;
	}

	var normalizedCandidate = NormalizeLookupText(candidate);
	return seriesCatalog.FirstOrDefault(series => NormalizeLookupText(series.Serie) == normalizedCandidate);
}

static SeriesInfo? FindSeriesByEvidence(IReadOnlyList<SeriesInfo> seriesCatalog, string? setName, string? reasoningSummary, string[]? detectedText)
{
	var evidence = new List<string>();
	AddEvidence(evidence, setName);
	AddEvidence(evidence, reasoningSummary);

	if (detectedText is not null)
	{
		foreach (var text in detectedText)
		{
			AddEvidence(evidence, text);
		}
	}

	if (evidence.Count == 0)
	{
		return null;
	}

	SeriesInfo? bestMatch = null;
	var bestScore = 0;
	var tie = false;

	foreach (var series in seriesCatalog)
	{
		var score = ScoreSeriesMatch(series, evidence);
		if (score <= 0)
		{
			continue;
		}

		if (score > bestScore)
		{
			bestScore = score;
			bestMatch = series;
			tie = false;
		}
		else if (score == bestScore)
		{
			tie = true;
		}
	}

	return tie ? null : bestMatch;
}

static void AddEvidence(List<string> evidence, string? text)
{
	if (string.IsNullOrWhiteSpace(text))
	{
		return;
	}

	var normalizedText = NormalizeLookupText(text);
	if (normalizedText.Length > 0)
	{
		evidence.Add(normalizedText);
	}
}

static int ScoreSeriesMatch(SeriesInfo series, IReadOnlyList<string> evidence)
{
	var score = 0;
	var normalizedName = NormalizeLookupText(series.Serie);
	var symbolHint = NormalizeLookupText(ExtractSeriesSymbolHint(series));
	var year = series.Jahr > 0 ? series.Jahr.ToString() : null;

	foreach (var text in evidence)
	{
		if (text.Contains(normalizedName, StringComparison.Ordinal))
		{
			score = Math.Max(score, 100);
		}

		if (!string.IsNullOrWhiteSpace(symbolHint) && text.Contains(symbolHint, StringComparison.Ordinal))
		{
			score = Math.Max(score, 70);
		}

		if (year is not null && text.Contains(year, StringComparison.Ordinal))
		{
			score = Math.Max(score, 20);
		}

		if (string.Equals(series.Serie, "Serie 1", StringComparison.OrdinalIgnoreCase)
			&& (text.Contains("kein symbol", StringComparison.Ordinal)
				|| text.Contains("ohne symbol", StringComparison.Ordinal)
				|| text.Contains("kein logo", StringComparison.Ordinal)
				|| text.Contains("ohne logo", StringComparison.Ordinal)))
		{
			score = Math.Max(score, 90);
		}
	}

	return score;
}

static string ExtractSeriesSymbolHint(SeriesInfo series)
{
	var logoEntry = series.Besonderheiten
		.FirstOrDefault(entry => entry.StartsWith("Logo:", StringComparison.OrdinalIgnoreCase));

	if (!string.IsNullOrWhiteSpace(logoEntry))
	{
		return logoEntry["Logo:".Length..].Trim();
	}

	if (string.Equals(series.Serie, "Serie 1", StringComparison.OrdinalIgnoreCase))
	{
		return "kein Symbol";
	}

	return "Symbol siehe Serienbeschreibung";
}

static string NormalizeLookupText(string value)
{
	var builder = new StringBuilder(value.Length);

	foreach (var character in value.Trim().ToLowerInvariant())
	{
		if (char.IsLetterOrDigit(character) || char.IsWhiteSpace(character))
		{
			builder.Append(character);
		}
	}

	return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
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
	public required string SeriesCatalogPath { get; init; }
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
			CardPhotosDirectory = configuration["CardPhotos:Directory"] ?? configuration["CARD_PHOTOS_DIRECTORY"] ?? ResolveDefaultCardPhotosDirectory(),
			SeriesCatalogPath = configuration["CardSeries:Path"] ?? configuration["CARD_SERIES_PATH"] ?? ResolveDefaultSeriesCatalogPath(),
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

	private static string ResolveDefaultCardPhotosDirectory()
	{
		var candidateDirectories = GetDefaultCardPhotosCandidates();

		foreach (var candidate in candidateDirectories)
		{
			if (Directory.Exists(candidate))
			{
				return candidate;
			}
		}

		return candidateDirectories[0];
	}

	private static string ResolveDefaultSeriesCatalogPath()
	{
		var candidatePaths = GetDefaultSeriesCatalogCandidates();

		foreach (var candidate in candidatePaths)
		{
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}

		return candidatePaths[0];
	}

	public static IReadOnlyList<string> GetDefaultCardPhotosCandidates()
	{
		return new[]
		{
			Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "cardFotos")),
			Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "cardFotos")),
			Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "cardFotos")),
			Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "cardFotos"))
		};
	}

	public static IReadOnlyList<string> GetDefaultSeriesCatalogCandidates()
	{
		return new[]
		{
			Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "cardInfos", "series.json")),
			Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "cardInfos", "series.json")),
			Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "cardInfos", "series.json")),
			Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "cardInfos", "series.json"))
		};
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

internal sealed class SeriesCatalogRoot
{
	[JsonPropertyName("Ninjago_Sammelkarten_Serien")]
	public SeriesInfo[]? Series { get; init; }
}

internal sealed class SeriesInfo
{
	[JsonPropertyName("Serie")]
	public required string Serie { get; init; }

	[JsonPropertyName("Jahr")]
	public int Jahr { get; init; }

	[JsonPropertyName("Besonderheiten")]
	public string[] Besonderheiten { get; init; } = Array.Empty<string>();

	[JsonPropertyName("Sondereditionen")]
	public string[] Sondereditionen { get; init; } = Array.Empty<string>();
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
