using System.Text.RegularExpressions;

namespace NinjagoScanner.Web.Shared;

/// <summary>
/// Canonical card-number ordering used everywhere cards are sorted by number: purely numeric
/// card numbers first (by value), then alphabetic-prefix-plus-number card numbers (e.g. "LE4",
/// "XXL1", or any other prefix) ordered by prefix alphabetically and then by numeric suffix,
/// then anything else ordered by raw text.
/// </summary>
public static partial class CardNumberSorting
{
    public static string BuildSortKey(string? cardNumber)
    {
        var normalized = Normalize(cardNumber);
        if (normalized.Length == 0)
        {
            return "9-";
        }

        if (int.TryParse(normalized, out var numericValue))
        {
            return $"0-{numericValue:D6}";
        }

        var match = AlphaPrefixNumberRegex().Match(normalized);
        if (match.Success && int.TryParse(match.Groups["number"].Value, out var suffixValue))
        {
            return $"1-{match.Groups["prefix"].Value}-{suffixValue:D6}";
        }

        return $"9-{normalized}";
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToUpperInvariant();
        return NonAlphaNumericRegex().Replace(normalized, string.Empty);
    }

    [GeneratedRegex("^(?<prefix>[A-Z]+)(?<number>\\d+)$")]
    private static partial Regex AlphaPrefixNumberRegex();

    [GeneratedRegex("[^A-Z0-9]")]
    private static partial Regex NonAlphaNumericRegex();
}
