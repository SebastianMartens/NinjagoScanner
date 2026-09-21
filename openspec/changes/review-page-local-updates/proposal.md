## Why

Every action on the `/review` page - one status click, a language change, "Alle bestätigen" - is followed by a full reload of the review data: `CatalogService.ListAllCards` plus `PictureService.ListCards` for the *whole* collection, then a regroup. That is O(all photos) work to change at most 18 rows, and the fresh presigned image URLs it returns make the browser refetch every full-resolution photo of the visible group. "Alle bestätigen" additionally sends one write per displayed photo even when most are already verified. The result is a page that feels slow on every click, for changes that mostly don't alter the grouping at all.

## What Changes

- The review page keeps the flat photo list and the catalog snapshot it loaded in memory. After a successful update RPC it patches the affected photo(s) in that list and regroups in memory, instead of calling `GetReviewGroupsAsync` again. This covers review status, language, series, card number, delete (photo removed from the list) and re-analyze (photo replaced by the returned card, keeping its existing `ImageUrl`, since the reanalysis response carries none).
- `CollectionQueryService.GetReviewGroupsAsync` is split into a fetch step (catalog + photos) and a pure, network-free grouping step that builds the ordered `CardReviewGroup` list from a catalog snapshot and a photo list. The page uses the fetch step on initial load and on "Von vorne beginnen", and the grouping step after every local patch.
- Because a photo keeps its `ImageUrl` across local updates, the browser no longer refetches the group's images after each action.
- "Alle bestätigen" saves only the displayed photos whose `ReviewStatus` is not already `verified` (this includes `incorrect` ones, which are overwritten). If none qualify, no RPC is made and the page just advances. If an RPC fails part-way, only the photos whose RPC succeeded are patched locally, so the UI matches the server.
- Navigation behaviour is unchanged: the current group is kept when it still exists, otherwise the nearest remaining one; "Alle bestätigen" advances to the next group matching the active filters, evaluated on the updated local data.
- The filtered group list is computed once per change instead of on every property access during a render.
- Accepted trade-off: the page can drift from the server if another tab or an upload changes data while it is open; "Von vorne beginnen" re-syncs from the server.

Out of scope: any change to `ListCards` itself (per-call cache warm-up, per-photo presigned URLs, S3 listing), a batch or parallel update RPC, and staged/deferred saving. No `.proto` or PictureService change.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `web-card-review-flow`: the review page updates from local state instead of reloading the group list after photo changes; "Confirm all" saves only photos not already verified and handles partial failure; displayed photo URLs stay stable across updates; the group list is re-synced from the server only on initial load and on restarting from the beginning.

## Impact

- `NinjagoScanner.Web/Components/Pages/Review.razor`: local state (photo list, catalog snapshot), patch-and-regroup after each action, dirty-only `ConfirmAllAsync`, cached filtered-group list.
- `NinjagoScanner.Web/Services/CollectionQueryService.cs`: `GetReviewGroupsAsync` split into fetch and pure grouping (the grouping step is exposed for the page and unit tests).
- `NinjagoScanner.Web/Models/`: possibly a small snapshot type holding the catalog lookup used by the grouping step.
- `NinjagoScanner.Web.Tests`: unit tests for the pure grouping step and for the page's patch/regroup and confirm-all behaviour; existing review-page tests that assumed a reload need updating.
- No changes to CatalogService, PictureService, the `.proto` contract, or infrastructure.
