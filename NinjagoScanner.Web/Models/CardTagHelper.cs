namespace NinjagoScanner.Web.Models;

/// <summary>
/// Derives a placeholder "Tags" display from the catalog card's rarity, since no real Tags
/// field exists server-side yet. Shared by the gallery tile and the table view so a future real
/// Tags field only needs to replace this helper's call sites.
/// </summary>
public static class CardTagHelper
{
    public static IReadOnlyList<string> TagsForRarity(string? rarity)
    {
        var label = CardRarity.BadgeLabel(rarity);
        return label is null ? Array.Empty<string>() : [label];
    }
}
