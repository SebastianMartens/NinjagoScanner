## Why

The `/table` page (`CardsTable.razor`, "Tabelle") offered a dense grouped-table alternative to browsing cards, but the Gallery and Collection pages now cover the same card-browsing functionality (grouping, filtering, set assignment, review status). Keeping three overlapping browsing views adds navigation clutter and an extra page to maintain for no remaining benefit.

## What Changes

- **BREAKING**: Remove the `/table` route and `CardsTable.razor` component entirely
- Remove the "Tabelle" links from `NavMenu.razor` (both the top nav and the bottom tab bar)
- Remove table-only CSS rules from `app.css` (`.cards-table*`, `.table-group*`, `.table-thumb*`, `.table-status`, `.table-set-select`, `.table-details*`, `.table-image-preview*`, `.table-primary`, `.table-secondary`, `.table-tags`, `.table-controls*`, `.table-header`, plus the `.table-page` selector's own rule block), and drop `.table-page`/`.table-header`/`.table-image-preview-backdrop` from the shared selector lists they appear in (e.g. the shared page-container rule at `app.css:351`, the shared focus/interactive-element rule at `app.css:92`) without touching the other page classes those rules still serve
- No `CardCatalogService` changes are expected: `GetCardsAsync`, `GetKnownSeriesAsync`, and `UpdateSetNameAsync` are shared with Gallery/Collection and stay; all of CardsTable's grouping/filtering/sorting logic lives locally in the page's own `@code` block and is deleted with the component

## Capabilities

### New Capabilities

_(none)_

### Modified Capabilities

- `web-card-table-view`: Remove the entire capability — the table view no longer exists

## Impact

- **Code**: `NinjagoScanner.Web/Components/Pages/CardsTable.razor` (deleted), `NinjagoScanner.Web/Components/Layout/NavMenu.razor` (two nav entries removed), `NinjagoScanner.Web/wwwroot/app.css` (table-scoped rules removed)
- **No API/service changes**: `NinjagoScanner.PictureService` and `NinjagoScanner.CatalogService` are untouched; no gRPC contract changes
- **Unrelated to** the separate sidecar/photo storage migration (Firestore + GCS) currently being explored — that work is scoped to its own change
