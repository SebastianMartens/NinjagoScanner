using System.Text;
using System.Text.Json;

namespace NinjagoScanner.Scanner;

internal static class SeriesCatalogService
{
    public static IReadOnlyList<SeriesInfo> Load(string seriesCatalogPath)
    {
        if (!File.Exists(seriesCatalogPath))
        {
            return Array.Empty<SeriesInfo>();
        }

        try
        {
            var json = File.ReadAllText(seriesCatalogPath, Encoding.UTF8);
            var catalog = JsonSerializer.Deserialize<SeriesCatalogRoot>(json, ScannerJsonOptions.Default);
            var cardNamesBySeries = LoadSeriesCardNames(seriesCatalogPath);

            return catalog?.Series?
                .Where(series => !string.IsNullOrWhiteSpace(series.Serie))
                .Select(series => new SeriesInfo
                {
                    Serie = series.Serie,
                    Jahr = series.Jahr,
                    Besonderheiten = series.Besonderheiten,
                    Sondereditionen = series.Sondereditionen,
                    CardNames = ResolveSeriesCardNames(series, cardNamesBySeries)
                })
                .ToArray() ?? Array.Empty<SeriesInfo>();
        }
        catch
        {
            return Array.Empty<SeriesInfo>();
        }
    }

    public static string BuildPrompt(IReadOnlyList<SeriesInfo> seriesCatalog)
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

            if (series.CardNames.Length > 0)
            {
                builder.Append(" | Bekannte Kartennamen: ")
                    .Append(string.Join(", ", series.CardNames));
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    public static string? ResolveSetName(GeminiCardPayload payload, IReadOnlyList<SeriesInfo> seriesCatalog)
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

        var inferredMatch = FindSeriesByEvidence(seriesCatalog, payload.SetName, payload.CardName, payload.ReasoningSummary, payload.DetectedText);
        return inferredMatch?.Serie;
    }

    private static IReadOnlyDictionary<string, string[]> LoadSeriesCardNames(string seriesCatalogPath)
    {
        var seriesDirectory = Path.GetDirectoryName(seriesCatalogPath);
        if (string.IsNullOrWhiteSpace(seriesDirectory) || !Directory.Exists(seriesDirectory))
        {
            return new Dictionary<string, string[]>(StringComparer.Ordinal);
        }

        var cardNamesBySeries = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var detailFilePath in Directory.EnumerateFiles(seriesDirectory, "series_*.json"))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(detailFilePath, Encoding.UTF8));
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var cardNames = ExtractSeriesCardNames(property.Value);
                    if (cardNames.Length == 0)
                    {
                        continue;
                    }

                    cardNamesBySeries[NormalizeLookupText(property.Name.Replace('_', ' '))] = cardNames;
                }
            }
            catch
            {
                // Ignore malformed detail files and continue with other entries.
            }
        }

        return cardNamesBySeries;
    }

    private static string[] ExtractSeriesCardNames(JsonElement rootElement)
    {
        var cardNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectSeriesCardNames(rootElement, cardNames);

        return cardNames
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void CollectSeriesCardNames(JsonElement element, HashSet<string> cardNames)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("Name")
                        && property.Value.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                    {
                        cardNames.Add(property.Value.GetString()!.Trim());
                    }

                    CollectSeriesCardNames(property.Value, cardNames);
                }
                break;

            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    CollectSeriesCardNames(child, cardNames);
                }
                break;
        }
    }

    private static string[] ResolveSeriesCardNames(SeriesInfo series, IReadOnlyDictionary<string, string[]> cardNamesBySeries)
    {
        return cardNamesBySeries.TryGetValue(NormalizeLookupText(series.Serie), out var cardNames)
            ? cardNames
            : Array.Empty<string>();
    }

    private static SeriesInfo? FindSeriesByName(IReadOnlyList<SeriesInfo> seriesCatalog, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var normalizedCandidate = NormalizeLookupText(candidate);
        return seriesCatalog.FirstOrDefault(series => NormalizeLookupText(series.Serie) == normalizedCandidate);
    }

    private static SeriesInfo? FindSeriesByEvidence(IReadOnlyList<SeriesInfo> seriesCatalog, string? setName, string? cardName, string? reasoningSummary, string[]? detectedText)
    {
        var evidence = new List<string>();
        AddEvidence(evidence, setName);
        AddEvidence(evidence, cardName);
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

    private static void AddEvidence(List<string> evidence, string? text)
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

    private static int ScoreSeriesMatch(SeriesInfo series, IReadOnlyList<string> evidence)
    {
        var score = 0;
        var normalizedName = NormalizeLookupText(series.Serie);
        var symbolHint = NormalizeLookupText(ExtractSeriesSymbolHint(series));
        var year = series.Jahr > 0 ? series.Jahr.ToString() : null;
        var normalizedCardNames = series.CardNames
            .Select(NormalizeLookupText)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

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

            if (normalizedCardNames.Any(cardName => text.Contains(cardName, StringComparison.Ordinal)))
            {
                score = Math.Max(score, 35);
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

    private static string ExtractSeriesSymbolHint(SeriesInfo series)
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

    private static string NormalizeLookupText(string value)
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
}
