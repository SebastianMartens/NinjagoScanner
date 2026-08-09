## Why

The `/review` page currently walks through every group (series + card number) regardless of how many of its photos are already reviewed. Once most groups have been confirmed, a person has to page past dozens of fully-reviewed groups to reach the ones that still need attention. A review-status filter lets them jump straight to groups that still contain, e.g., unreviewed or incorrect photos.

## What Changes

- Add a review-status filter control to the `/review` page with the options `All`, `Unreviewed`, `Verified`, and `Incorrect`.
- When a status other than `All` is selected, only groups containing at least one photo with that `ReviewStatus` are shown - but every photo in a matching group is still shown, including photos with a different status.
- The group position indicator (`n / total`) and previous/next navigation operate over the filtered set of groups.
- Changing the filter, or a status change that removes a group from the current filter, resets navigation to the first matching group.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `web-card-review-flow`: adds a review-status filter that determines which groups are shown and navigated, on top of the existing grouping/ordering/navigation behavior.

## Impact

- `NinjagoScanner.Web/Components/Pages/Review.razor`: add the filter control and filtering logic; navigation (`GoToPrevious`/`GoToNext`/`TryFindGroupIndex`) operates over the filtered group list.
- No changes to `CardCatalogService`, gRPC contracts, or sidecar storage - filtering is client-side over the groups already returned by `GetReviewGroupsAsync`.
