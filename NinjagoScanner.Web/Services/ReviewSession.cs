using NinjagoScanner.Web.Models;

namespace NinjagoScanner.Web.Services;

/// <summary>
/// The /review page's in-memory working state: the flat list of photos and the catalog it loaded
/// once, the card groups derived from them, the active filters and the current position. A photo
/// change that was saved successfully is applied here (<see cref="ReplacePhoto"/>,
/// <see cref="RemovePhoto"/>, <see cref="ConfirmAsync"/>) and the groups are rebuilt in memory,
/// so no action needs to fetch the collection again. Plain C# with no Blazor or gRPC dependency so
/// the navigation rules are unit-testable.
/// </summary>
internal sealed class ReviewSession
{
    internal const string AllFilterValue = "all";

    private IReadOnlyList<(string Series, string Category, string CardNumber, string CardName, int SortOrder)> catalog;
    private List<CardListItem> photos;
    private IReadOnlyList<CardReviewGroup> groups;
    private IReadOnlyList<CardReviewGroup>? filteredGroups;
    private string reviewStatusFilter = ReviewStatuses.Unreviewed;
    private string analysisStatusFilter = AllFilterValue;
    private string searchText = string.Empty;
    private int currentIndex;

    public ReviewSession(ReviewSnapshot snapshot)
    {
        catalog = snapshot.Catalog;
        photos = snapshot.Photos.ToList();
        groups = CollectionQueryService.BuildReviewGroups(catalog, photos);
    }

    public IReadOnlyList<CardReviewGroup> Groups => groups;

    /// <summary>
    /// The groups satisfying every active filter, in display order. Cached: recomputed only after
    /// the groups or a filter value changed, so reading it repeatedly during a render is free.
    /// </summary>
    public IReadOnlyList<CardReviewGroup> FilteredGroups =>
        filteredGroups ??= groups.Where(group => MatchesFilters(group, reviewStatusFilter, analysisStatusFilter, searchText)).ToArray();

    public int CurrentIndex => currentIndex;

    public CardReviewGroup? CurrentGroup =>
        currentIndex >= 0 && currentIndex < FilteredGroups.Count ? FilteredGroups[currentIndex] : null;

    public string ReviewStatusFilter => reviewStatusFilter;

    public string AnalysisStatusFilter => analysisStatusFilter;

    public string SearchText => searchText;

    public void SetReviewStatusFilter(string value)
    {
        reviewStatusFilter = value;
        FiltersChanged();
    }

    public void SetAnalysisStatusFilter(string value)
    {
        analysisStatusFilter = value;
        FiltersChanged();
    }

    public void SetSearchText(string value)
    {
        searchText = value;
        FiltersChanged();
    }

    public void GoToPrevious()
    {
        if (currentIndex > 0)
        {
            currentIndex--;
        }
    }

    public void GoToNext()
    {
        if (currentIndex < FilteredGroups.Count - 1)
        {
            currentIndex++;
        }
    }

    public void RestartFromBeginning()
    {
        currentIndex = 0;
    }

    /// <summary>
    /// Replaces the catalog and photos with a freshly fetched snapshot (the way to pick up changes
    /// made outside this page), keeping the active filters and returning to the first matching group.
    /// </summary>
    public void Reload(ReviewSnapshot snapshot)
    {
        catalog = snapshot.Catalog;
        photos = snapshot.Photos.ToList();
        groups = CollectionQueryService.BuildReviewGroups(catalog, photos);
        filteredGroups = null;
        currentIndex = 0;
    }

    /// <summary>
    /// Replaces one photo with <paramref name="transform"/> applied to it. No-op for an unknown
    /// photo id. Stays on the current group where possible (see <see cref="Regroup"/>).
    /// </summary>
    public void ReplacePhoto(string photoId, Func<CardListItem, CardListItem> transform)
    {
        if (ApplyReplacement(photoId, transform))
        {
            Regroup(keepPosition: true);
        }
    }

    /// <summary>Removes one photo. No-op for an unknown photo id.</summary>
    public void RemovePhoto(string photoId)
    {
        var index = photos.FindIndex(photo => string.Equals(photo.PhotoId, photoId, StringComparison.Ordinal));
        if (index < 0)
        {
            return;
        }

        photos.RemoveAt(index);
        Regroup(keepPosition: true);
    }

    /// <summary>
    /// Group-level "Confirm all": saves <see cref="ReviewStatuses.Verified"/> for each candidate
    /// photo that is not already verified (an <c>incorrect</c> or <c>unreviewed</c> one is
    /// overwritten) by calling <paramref name="saveVerifiedAsync"/> one photo at a time, and shows
    /// each photo as verified only once its own save has succeeded. Already-verified photos are
    /// not saved again; with nothing to save no call is made. When every save succeeded the page
    /// advances to the next group matching the active filters, evaluated on the updated data.
    /// When a save throws, the photos saved before it stay verified, the rest are unchanged, the
    /// position is kept as for any other photo change, and the exception propagates.
    /// </summary>
    public async Task ConfirmAsync(IReadOnlyList<CardListItem> candidates, Func<string, Task> saveVerifiedAsync)
    {
        var confirmedKey = CurrentGroup?.Key;
        var toSave = candidates
            .Where(photo => !string.Equals(photo.ReviewStatus, ReviewStatuses.Verified, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var saved = 0;
        try
        {
            foreach (var photo in toSave)
            {
                await saveVerifiedAsync(photo.PhotoId);
                ApplyReplacement(photo.PhotoId, current => current with { ReviewStatus = ReviewStatuses.Verified });
                saved++;
            }
        }
        catch
        {
            if (saved > 0)
            {
                Regroup(keepPosition: true);
            }

            throw;
        }

        Regroup(keepPosition: false);
        if (confirmedKey is not null)
        {
            currentIndex = FindNextFilteredIndexAfter(confirmedKey);
        }
    }

    // The value each update RPC stores, which PictureServiceClient normalizes before sending and
    // ToCardListItem normalizes again when reading: blank becomes null (language: the default),
    // otherwise trimmed. Applying the same rule locally keeps the shown value equal to what a
    // reload would show.
    internal static CardListItem WithReviewStatus(CardListItem photo, string reviewStatus) =>
        photo with { ReviewStatus = NormalizeNullable(reviewStatus) ?? ReviewStatuses.Unreviewed };

    internal static CardListItem WithSetName(CardListItem photo, string? setName) =>
        photo with { SetName = NormalizeNullable(setName) };

    internal static CardListItem WithCardNumber(CardListItem photo, string? cardNumber) =>
        photo with { CardNumber = NormalizeNullable(cardNumber) };

    internal static CardListItem WithLanguage(CardListItem photo, string? language) =>
        photo with { Language = NormalizeNullable(language) ?? Languages.Default };

    private static string? NormalizeNullable(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static bool MatchesFilters(CardReviewGroup group, string reviewStatusFilter, string analysisStatusFilter, string searchText)
    {
        return MatchesReviewStatusFilter(group, reviewStatusFilter)
            && MatchesAnalysisStatusFilter(group, analysisStatusFilter)
            && MatchesSearchFilter(group, searchText);
    }

    internal static bool MatchesReviewStatusFilter(CardReviewGroup group, string reviewStatusFilter)
    {
        return string.Equals(reviewStatusFilter, AllFilterValue, StringComparison.Ordinal)
            || group.Photos.Any(photo => string.Equals(photo.ReviewStatus, reviewStatusFilter, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool MatchesAnalysisStatusFilter(CardReviewGroup group, string analysisStatusFilter)
    {
        return string.Equals(analysisStatusFilter, AllFilterValue, StringComparison.Ordinal)
            || group.Photos.Any(photo => string.Equals(photo.AnalysisStatus, analysisStatusFilter, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool MatchesSearchFilter(CardReviewGroup group, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        var term = searchText.Trim();
        return group.Photos.Any(photo =>
            (photo.CardName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || (photo.CardNumber?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    private void FiltersChanged()
    {
        filteredGroups = null;
        currentIndex = 0;
    }

    private bool ApplyReplacement(string photoId, Func<CardListItem, CardListItem> transform)
    {
        var index = photos.FindIndex(photo => string.Equals(photo.PhotoId, photoId, StringComparison.Ordinal));
        if (index < 0)
        {
            return false;
        }

        photos[index] = transform(photos[index]);
        return true;
    }

    /// <summary>
    /// Rebuilds the groups from the current photo list. With <paramref name="keepPosition"/>, stays
    /// on the group currently shown if it still exists, otherwise on the group now at (or, past the
    /// end, nearest to) the same position.
    /// </summary>
    private void Regroup(bool keepPosition)
    {
        var previousIndex = currentIndex;
        var key = CurrentGroup?.Key;

        groups = CollectionQueryService.BuildReviewGroups(catalog, photos);
        filteredGroups = null;

        if (!keepPosition)
        {
            return;
        }

        currentIndex = key is not null && TryFindGroupIndex(key, out var index)
            ? index
            : Math.Min(previousIndex, Math.Max(FilteredGroups.Count - 1, 0));
    }

    private int FindNextFilteredIndexAfter(string confirmedKey)
    {
        var confirmedSeen = false;
        var matchCountBeforeTarget = 0;

        foreach (var candidate in groups)
        {
            var matches = MatchesFilters(candidate, reviewStatusFilter, analysisStatusFilter, searchText);

            if (confirmedSeen && matches)
            {
                return matchCountBeforeTarget;
            }

            if (matches)
            {
                matchCountBeforeTarget++;
            }

            if (string.Equals(candidate.Key, confirmedKey, StringComparison.Ordinal))
            {
                confirmedSeen = true;
            }
        }

        return matchCountBeforeTarget;
    }

    private bool TryFindGroupIndex(string key, out int index)
    {
        var filtered = FilteredGroups;
        for (var i = 0; i < filtered.Count; i++)
        {
            if (string.Equals(filtered[i].Key, key, StringComparison.Ordinal))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }
}
