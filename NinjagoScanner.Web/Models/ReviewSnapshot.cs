namespace NinjagoScanner.Web.Models;

/// <summary>
/// Everything the /review page needs from the backend services to build (and later rebuild, in
/// memory) its card groups: the catalog's cards and every photo in the collection. Fetched once
/// on load; later photo changes are applied to it locally instead of fetching it again.
/// </summary>
internal sealed record ReviewSnapshot(
    IReadOnlyList<(string Series, string Category, string CardNumber, string CardName, int SortOrder)> Catalog,
    IReadOnlyList<CardListItem> Photos);
