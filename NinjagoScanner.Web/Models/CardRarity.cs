namespace NinjagoScanner.Web.Models;

/// <summary>
/// The catalog's fixed card rarities (see CatalogService's <c>CatalogRarities</c>). Rarity is
/// catalog data: Web only ever reads it from the matched catalog card, never from a photo.
/// </summary>
public static class CardRarity
{
    public const string Common = "common";
    public const string Limited = "limited";
    public const string Legendary = "legendary";

    /// <summary>Lower-cased, trimmed rarity; anything missing or unknown counts as <see cref="Common"/>.</summary>
    public static string Normalize(string? rarity)
    {
        var normalized = rarity?.Trim().ToLowerInvariant();
        return normalized is Limited or Legendary ? normalized : Common;
    }

    /// <summary>German display label, e.g. for the "Seltenheit" lines.</summary>
    public static string Label(string? rarity) => Normalize(rarity) switch
    {
        Limited => "Limited",
        Legendary => "Legendär",
        _ => "Normal"
    };

    /// <summary>Label for tags and chips: null for common cards, which show none.</summary>
    public static string? BadgeLabel(string? rarity) => Normalize(rarity) == Common ? null : Label(rarity);
}
