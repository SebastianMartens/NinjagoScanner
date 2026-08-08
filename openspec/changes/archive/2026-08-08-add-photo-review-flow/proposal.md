## Why

The Home page ("/") currently renders every scanned photo as a tile with its raw Gemini analysis details inline — a view the table page (`/table`) has already made redundant for browsing. What's actually missing is a focused workflow for *reviewing*: confirming that all photos grouped under one series/card-number really show that card, and fixing the dominant error (Gemini picking the wrong series) in one click, without hand-editing each sidecar individually.

## What Changes

- **BREAKING**: Remove the tile-gallery rendering currently on Home.razor ("/") — one tile per photo with inline Gemini detail card, plus its search/group/filter controls. Nothing inherits this exact presentation; `/table` already covers browsing.
- Add a new "Overview" page that becomes the new home page ("/"). For this change it only hosts the existing Gemini scan trigger (moved as-is, same behavior/messages), leaving room for later additions (e.g. per-series stats) without building them now.
- Add a new card-review page (`/review`) that:
  - Groups all scanned photos by (`SetName`, `CardNumber`) taken from each photo's own sidecar — not driven by the catalog, so a group only exists if it has at least one photo.
  - Orders groups by the matching known series' catalog `SortOrder` then `CardNumber`; merges every photo whose `SetName` doesn't match a known series (including blank) into one single catch-all group sorted last.
  - Shows every photo in the current group at once, each with its own current series/card name/card number always visible, other sidecar fields collapsed behind an on-demand details toggle.
  - Gives each photo a "Confirm" button (→ `ReviewStatus = verified`), a "Has Error" button (→ `ReviewStatus = incorrect`), and one button per known series that reassigns just that photo's `SetName` via the existing `UpdateSetName` RPC (leaves `ReviewStatus` untouched).
  - Gives the group a "Confirm all" button that sets `ReviewStatus = verified` on every photo currently shown, then auto-advances to the next group in sort order (plain paging, not filtered to unreviewed work).
  - Adds Prev/Next navigation between groups for manual browsing.
- Add a new `UpdateReviewStatus(imageFileName, reviewStatus)` RPC to `CardPictureService`, modeled on the existing `UpdateSetName` RPC: touches only `ReviewStatus`, creates a pending sidecar if none exists, leaves every other field untouched. Used by the per-photo Confirm/Has-Error buttons and by the group's Confirm-all action (one call per photo).

## Capabilities

### New Capabilities
- `web-overview`: the new home page ("/"), currently hosting only the Gemini scan trigger.
- `web-card-review-flow`: the `/review` page — grouping photos by series+card number, per-photo review/reassignment actions, and group-level confirm-all with auto-advance.

### Modified Capabilities
- `web-card-gallery`: all requirements removed — the tile gallery this capability describes no longer exists; its page is replaced by `web-overview`.
- `picture-service-sidecar-editing`: adds a requirement for the new `UpdateReviewStatus` RPC, alongside the existing `UpdateSidecar`/`UpdateSetName` requirements.

## Impact

- `NinjagoScanner.Web/Components/Pages/Home.razor`: replaced by a new Overview page (scan trigger only).
- `NinjagoScanner.Web/Components/Pages/Review.razor` (new): the `/review` page.
- `NinjagoScanner.Web/Services/CardCatalogService.cs`: gains a method to call the new `UpdateReviewStatus` RPC; may gain grouping/sorting helpers for the review page.
- `NinjagoScanner.PictureService/Protos/picture_service.proto` and `NinjagoScanner.Web/Protos/picture_service.proto`: add `UpdateReviewStatus` RPC + request/response messages (kept in sync across both copies, matching the existing pattern for `UpdateSetName`).
- `NinjagoScanner.PictureService/Services/PictureScannerGrpcService.cs` and sidecar update logic: implement `UpdateReviewStatus`, mirroring the existing `UpdateSetName` handler.
- No changes to `/table`, `/collection`, `/upload`, the generic `web-card-review` ReviewStatus-control capability, or `picture-service-sidecar-review`/`picture-service-card-listing` behavior.
