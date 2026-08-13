namespace NinjagoScanner.Web.Models;

/// <summary>
/// Derives a placeholder "Tags" display from the existing Rarity field, since no real
/// Tags field exists server-side yet. Shared by the gallery tile and the table view so
/// a future real Tags field only needs to replace this helper's call sites.
/// </summary>
internal static class CardTagHelper
{
    public static IReadOnlyList<string> TagsForRarity(string? rarity)
    {
        return string.IsNullOrWhiteSpace(rarity)
            ? Array.Empty<string>()
            : [rarity.Trim()];
    }
}
