## Why

The review page (`/review`) currently offers only one filter — Review Status — so a person working through a large backlog of scanned photos has no way to narrow the list to a particular Analysis Status (e.g. only `uncertain` or `failed` photos that need attention) or jump straight to a known card by number or name.

## What Changes

- Add an Analysis Status filter control to the review page, offering `All`, `Ok`, `Uncertain`, `Failed`, and `Pending`, following the same select-and-filter pattern as the existing Review Status filter.
- Add a free-text search box to the review page that matches against each photo's own `CardName` and `CardNumber`, using the same case-insensitive substring match, live-on-keystroke behavior already used by the search boxes on `/collection` and `/table`.
- A review group is included in the filtered/navigable list if and only if it satisfies all three active filters at once (Review Status AND Analysis Status AND free-text search), each narrowing independently; a group matches the Analysis Status or search filter if at least one of its photos matches, mirroring how the existing Review Status filter already works. Every photo in an included group is still shown, regardless of that individual photo's own status or whether it personally matched the search text.
- Changing any of the three filters returns navigation to the first matching group, consistent with the existing Review Status filter behavior.

## Capabilities

### Modified Capabilities
- `web-card-review-flow`: the review page's filtering requirement expands from Review Status alone to three independently-combining filters (Review Status, Analysis Status, free-text search over CardName/CardNumber), and group navigation/reset behavior is generalized to react to any of the three.

## Impact

- `NinjagoScanner.Web/Components/Pages/Review.razor` — add Analysis Status `<select>` and search `<input>` controls, extend the group-filtering predicate and the "reset to first group" logic to combine all three filters.
- No changes to PictureService, CatalogService, or gRPC contracts — filtering stays client-side over the already-fetched `CardReviewGroup` list, matching how Review Status filtering and the `/collection` and `/table` search boxes already work.
