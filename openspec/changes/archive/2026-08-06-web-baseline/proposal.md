## Why

NinjagoScanner.Web is already running (it's the primary UI for browsing scanned cards, uploading photos, and triggering PictureService scans) but only one of its behaviors — `web-card-review` — has a documented spec. The rest of the app's views and startup configuration exist only as source code, making it hard to propose or review future Web changes against a documented baseline. This change establishes that baseline: no source code changes, only specs describing current behavior.

## What Changes

- Add a baseline spec for the card gallery view (`Components/Pages/Home.razor`, `/`): tile browsing of scanned photos, search/filter/group controls, and the manual Gemini-scan trigger.
- Add a baseline spec for the card table view (`Components/Pages/CardsTable.razor`, `/table`): grouped tabular browsing, inline set assignment, and inline detail expansion.
- Add a baseline spec for the photo upload flow (`Components/Pages/Upload.razor`, `Services/CardCatalogService.SaveUploadedPhotoAsync`, `/upload`): mobile-friendly capture, client-side validation, and chunked upload to PictureService.
- Add a baseline spec for the collection overview (`Components/Pages/Collection.razor`, `/collection`): full catalog-backed ownership view merging CatalogService's card list with owned photos, plus the per-card sidecar detail/edit pane.
- Add a baseline spec for app configuration and photo hosting (`Program.cs`): resolving the card photos directory, CatalogService/PictureService addresses, and max upload size from configuration/environment with fallback discovery, and serving the resolved directory as static files.
- No behavior, API, or code changes — this is a documentation-only baseline.

## Capabilities

### New Capabilities
- `web-card-gallery`: tile view of scanned card photos on `/` with search/status/set/rarity filters, grouping, and the manual Gemini-scan trigger.
- `web-card-table-view`: dense tabular alternative view on `/table` with grouping, search, inline set assignment, and inline reasoning/error detail expansion.
- `web-photo-upload`: `/upload` flow for saving a photo (typically from a mobile camera) into shared `cardFotos` storage via PictureService, with size/type validation.
- `web-collection-overview`: `/collection` full-catalog ownership view (owned vs. missing, duplicate counts) with filtering, keyboard navigation, and a sidecar detail/edit pane.
- `web-app-configuration`: startup resolution of the card photos directory, CatalogService/PictureService addresses, and max upload size, plus static-file hosting of the resolved photos directory under `/cardFotos`.

### Modified Capabilities
(none — `web-card-review` already has a spec and is unaffected by this baseline)

## Impact

- Adds files under `openspec/specs/web-*/spec.md` only; no changes to `NinjagoScanner.Web` source.
- Establishes the documented contract for the Web app's UI surface and startup configuration, complementing the existing `catalog-service-*`, `picture-service-sidecar-review`, and `web-card-review` specs.
