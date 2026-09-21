## Context

See proposal.md - Why. Today `Review.razor` holds `groups` (a `CardReviewGroup` list) and rebuilds it by calling `CollectionQueryService.GetReviewGroupsAsync()` after every action (`RunPhotoActionAsync` → `ReloadGroupsKeepingPositionAsync`, `ReanalyzeAsync`, `ConfirmAllAsync`). That method fetches the whole catalog (`CatalogServiceClient.ListCatalogCardsAsync`), the whole collection (`PictureServiceClient.GetCardsAsync` → `ListCards`, which also mints a presigned URL per photo), then groups by `BuildOwnershipKey(SetName, CardNumber)` against the catalog, sorts by catalog `SortOrder` / card-number key, and appends a catch-all group.

Constraints that shape the approach:

- `CardListItem` and `CardReviewGroup` are `init`-only classes; patching means creating a modified copy.
- Update RPCs return only success/failure (`UpdateReviewStatus`, `UpdateSetName`, `UpdateCardNumber`, `UpdateCardLanguage`, `DeletePhoto`), so the page applies the value it sent. `ReanalyzePhoto` returns the updated card without an `ImageUrl`.
- The existing page tests (`NinjagoScanner.Web.Tests/Pages/Review*Tests.cs`) test `internal static` members of `Review.razor` directly; there is no Blazor component test harness. Page-level behaviour (patching, position keeping, confirm-all) is currently untestable except through those statics.
- Web-only: the `.proto` and PictureService stay untouched.

## Goals / Non-Goals

**Goals:**

- No collection or catalog fetch after the initial load except an explicit restart from the beginning.
- The page's state transitions (patch, remove, regroup, keep position, advance after confirm) live in plain C# that can be unit-tested without Blazor.
- Grouping/sorting results identical to today's for the same input.

**Non-Goals:**

- Any change to how `ListCards` builds its response, or to the wire contract.
- Reconciling the page with concurrent changes made elsewhere (another tab, an upload) while the page is open.
- Refactoring the other pages (`Collection`, `Gallery`, `Overview`) that also call `CollectionQueryService`.

## Decisions

### D1: Split grouping out of `GetReviewGroupsAsync` into a pure function

`CollectionQueryService` gets a fetch step returning a snapshot (catalog cards + photo list) and a static, network-free `BuildReviewGroups(catalog, photos)` containing the current grouping/sorting logic moved verbatim. `GetReviewGroupsAsync` stays as fetch + build so other callers and the existing `CollectionQueryServiceReviewGroupsTests` keep working. The catalog lookup keyed by `BuildOwnershipKey` is built inside the pure function from the raw catalog card list (an O(catalog) step per regroup, in memory).

*Alternative: patch the existing `CardReviewGroup` list in place (move a photo between two groups).* Rejected: it duplicates the ordering and catch-all rules in a second place, and a series or card-number change may create a group that did not exist (or empty one). Regrouping the flat list with the same function every time makes the local result equal to what a fresh load would produce by construction. At a few thousand photos and a few hundred catalog cards, one regroup is well under a millisecond-scale cost compared with a network round-trip.

### D2: A plain state holder owns the flat list, catalog and navigation

Introduce a small non-Blazor class (working name `ReviewSession`) constructed from a snapshot. It owns: the flat photo list, the catalog cards, the current groups (rebuilt via D1 after each mutation), the active filters, the current group index, and the cached filtered-group list. Mutations: `ReplacePhoto(photoId, transform)`, `ReplacePhotos(...)` for confirm-all, `RemovePhoto(photoId)`. Each mutation updates the flat list, regroups, and restores the position with the rule the page uses today: stay on the group with the same `Key` if it still exists, otherwise the group now at the previous index, clamped to the end. Confirm-all uses the existing "next filtered group after the confirmed key" logic (`FindNextFilteredIndexAfter`), moved into this class. `Review.razor` keeps only UI state (busy flags, drafts, expanded details, delete dialog, re-analysis errors) and delegates to the session.

*Alternative: keep everything in `Review.razor`'s `@code` block.* Rejected: the behaviour that most needs regression tests (position after a photo moves groups, confirm-all advance under filters, partial failure) would only be reachable through Blazor rendering, which this repo doesn't set up.

### D3: Apply the sent value locally after the RPC succeeds (no optimistic UI)

Each action awaits its RPC; on success it patches the photo with the values it sent (`ReviewStatus`, `SetName`, `CardNumber`, `Language`, trimmed/normalized the same way `PictureServiceClient` normalizes them so the local value equals what the server stored: blank → cleared). On failure the exception propagates as today and nothing is patched. Re-analysis patches from the returned card but keeps the old `ImageUrl`, and continues to drop/refresh the cached details as it does now.

*Alternative: optimistic update before the RPC, roll back on failure.* Rejected: more states to reason about for a save that is now a single fast write; the busy flag already disables the tile while it runs.

### D4: Confirm-all saves only non-verified photos, sequentially, patching per success

`ConfirmAllAsync` builds `toSave = DisplayedPhotos(group).Where(p => p.ReviewStatus != verified)` (case-insensitive, matching the existing comparisons). For each photo it awaits `UpdateReviewStatusAsync` and patches that photo immediately in the session (so a failure on photo k leaves 1..k-1 shown as verified, the rest unchanged). After the loop, if no exception occurred, it advances using the confirm-all navigation rule; if one occurred, the page stays on the group and the exception surfaces as it does today. If `toSave` is empty, no RPC is made and it advances directly. Photos beyond the 18-photo display cap remain untouched, as today.

*Alternative: parallelize the writes or add a batch RPC.* Out of scope (proposal). With only the non-verified photos written, the count is usually small; sequential keeps partial-failure semantics simple.

### D5: Stable image URLs come for free; regroup must not rebuild items

Because the flat list holds the original `CardListItem` instances and mutations replace only the changed item (`with`-style copy that preserves `ImageUrl`), no tile's `<img src>` changes after an action, so the browser keeps its cached image. The regroup step must reuse the list's item instances rather than re-materializing them from a fresh fetch.

### D6: Full reload only at load and restart

`OnInitializedAsync` builds the session from a fresh snapshot (known series + catalog + photos, as today). "Von vorne beginnen" re-fetches the snapshot and builds a fresh session (this is also the manual drift-recovery path), resetting the index to 0 and re-syncing card-number drafts. The known-series list for the series picker is fetched once at load, as today.

### D7: Compute the filtered group list once per change

`FilteredGroups` is currently a property that re-filters `groups` on every access (several times per render and per navigation check). The session caches the filtered list and recomputes it only when the groups or any filter value changes.

## Risks / Trade-offs

- **Page drifts from the server if data changes elsewhere** (second tab, upload while reviewing) → accepted in the proposal; "Von vorne beginnen" re-syncs, and a browser refresh always does.
- **Local normalization differs from the server's** (e.g. trimming, case of `SetName`, blank handling) → after a successful write the local value could differ subtly from what a reload would show. Mitigation: reuse the same normalization helpers `PictureServiceClient` applies before sending, patch with the normalized value, and cover blank/whitespace card numbers and series in tests.
- **Grouping cost on very large collections** → regrouping is O(photos + catalog) per action instead of a network fetch; for thousands of photos this is negligible next to the removed round-trips, but the pure function should avoid quadratic work (it doesn't today).
- **Re-analysis changing `SetName`/`CardNumber`** moves a photo between groups; the regroup and position rule from D2 handle it, and this path needs an explicit test because the reanalysis response lacks `ImageUrl`.
- **Existing tests coupled to reload behaviour** → update or replace them; the pure grouping tests carry over unchanged because D1 moves the logic verbatim.

## Open Questions

None that block implementation. Whether to also add a visible "refresh" affordance beyond "Von vorne beginnen" can be decided after the change is used.
