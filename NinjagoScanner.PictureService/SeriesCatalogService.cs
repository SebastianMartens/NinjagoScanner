using System.Text;

namespace NinjagoScanner.Scanner;

internal static class SeriesCatalogService
{
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
