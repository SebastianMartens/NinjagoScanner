## 1. Backend: UpdateReviewStatus RPC

- [x] 1.1 Add `UpdateReviewStatus` RPC + `UpdateReviewStatusRequest`/`UpdateReviewStatusResponse` messages to `NinjagoScanner.PictureService/Protos/picture_service.proto`, modeled on `UpdateSetName`.
- [x] 1.2 Mirror the same proto changes in `NinjagoScanner.Web/Protos/picture_service.proto` so both copies stay in sync.
- [x] 1.3 Implement the `UpdateReviewStatus` handler in `NinjagoScanner.PictureService` (alongside the existing `UpdateSetName` handler): create a `pending` sidecar if none exists, otherwise update only `ReviewStatus`, leaving every other field untouched.
- [x] 1.4 Add/extend PictureService unit tests covering: creating a pending sidecar via `UpdateReviewStatus`, updating only `ReviewStatus` on an existing sidecar, and that other fields are left unchanged. (New `NinjagoScanner.PictureService.Tests` project added, since none existed yet.)

## 2. Web: service layer support

- [x] 2.1 Add `UpdateReviewStatusAsync(imageFileName, reviewStatus)` to `CardCatalogService`, calling the new RPC (same pattern as `UpdateSetNameAsync`).
- [x] 2.2 Add a method/helper on `CardCatalogService` (or a small dedicated grouping helper) that loads all card entries and known series, then groups entries by (`SetName`, `CardNumber`), ranks known-series groups by the existing known-series order (reusing the `GetKnownSeriesAsync`-order-as-rank convention already used in `CardsTable.razor`'s `GetSeriesGroupRank`), and merges every non-matching/blank-`SetName` entry into one trailing catch-all group.
- [x] 2.3 Sort groups by series rank then `CardNumber`, with the catch-all group last; sort photos within a group deterministically (e.g. by `ImageFileName`).

## 3. Web: Overview page (new home)

- [x] 3.1 Create `NinjagoScanner.Web/Components/Pages/Overview.razor` at route `/`, carrying over the scan-trigger button, `isScanning` state, and scan summary message handling from the current `Home.razor`.
- [x] 3.2 Remove `NinjagoScanner.Web/Components/Pages/Home.razor` and its tile-gallery-specific markup/state (grouping/search/filter controls, card tiles, status/review badges for the gallery), keeping only what moved to Overview.
- [x] 3.3 Update `NavMenu.razor`'s home link label/icon if needed to reflect "Overview" (link target `href=""` stays the same route). Renamed to "Übersicht"; also added a `/review` nav entry (needed by section 4).
- [x] 3.4 Remove or prune gallery-only CSS rules that no longer apply once the tile markup is gone (check `cards-page`/`cards-*` classes for usages elsewhere before deleting).

## 4. Web: Review page

- [x] 4.1 Create `NinjagoScanner.Web/Components/Pages/Review.razor` at route `/review`, loading card entries + known series on init and computing the grouped/sorted list via the Section 2 helper.
- [x] 4.2 Track the current group by its group key (`SetName`+`CardNumber`, or the catch-all sentinel); render the current group's photos.
- [x] 4.3 Render each photo tile with: image, always-visible series name/card name/card number, a collapsed-by-default details toggle exposing rarity/confidence/reasoning summary/detected text/error message/scanned-at.
- [x] 4.4 Add per-photo "Confirm" and "Has Error" buttons calling `UpdateReviewStatusAsync` with `verified`/`incorrect`, then reload + re-locate the current group by key (no group navigation).
- [x] 4.5 Add per-photo series-reassignment buttons (one per known series, ordered by the known-series order) calling `UpdateSetNameAsync`, then reload + re-locate the current group by key; if the group is now empty, advance to the next group in sort order (or show the "nothing left to review" empty state if it was the last group).
- [x] 4.6 Add the group-level "Confirm all" button: sequentially call `UpdateReviewStatusAsync(verified)` for every photo currently in the group, then reload and advance to the next group by index in the freshly sorted list (wrapping to an empty state if none remain).
- [x] 4.7 Add manual Prev/Next controls that move by index in the freshly sorted group list without mutating any photo.
- [x] 4.8 Add empty states: no groups at all (no scanned photos), and "reached the end" after the last group. (Also added a "start over" button on the end state, since otherwise it would be a dead end.)
- [x] 4.9 Add a `NavMenu.razor` entry linking to `/review`.

## 5. Verification

- [x] 5.1 Run PictureService tests. (Ran full solution: `dotnet test NinjagoScanner.slnx` — 2/2 PictureService, 52/52 CatalogService, all green.)
- [x] 5.2 Manually verify in-browser: Overview page scan trigger still works; `/review` groups photos as expected (known series ordered, unknown/blank series merged into one trailing group); Confirm/Has-Error/series-reassign/Confirm-all/Prev/Next all behave per the specs in `specs/web-card-review-flow/spec.md`. Verified against the real `cardFotos` directory (7,642 photos) with Playwright/Chromium: Overview shows the scan trigger with no tile gallery; `/review` correctly grouped and paginated (1,687 groups); Confirm, Has Error, series-reassign (photo moves out of its old group), and Confirm-all (bulk-verifies + auto-advances) all persisted correctly to the sidecar JSON with no other fields touched and no server errors. All test mutations were reverted afterward (`git status` on `cardFotos` clean).
- [x] 5.3 Run `openspec validate --change add-photo-review-flow --strict` and fix any reported issues. (Valid, no issues.)
