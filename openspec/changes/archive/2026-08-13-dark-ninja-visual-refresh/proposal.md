## Why

The Web project currently has no cohesive visual identity - default Blazor styling with no shared color/type system. A dark, ninja/gaming-inspired visual design ("Card Vault") has been prototyped and approved (see design reference bundle) covering every existing page plus the Review page's series-reassignment UI. This change brings that visual design into `NinjagoScanner.Web` without altering any page's underlying behavior, data, or routes.

## What Changes

- Introduce a shared dark visual system in `app.css`: color tokens (dark purple/near-black surfaces, purple + green accents), a two-font pairing (Rajdhani for headings/numbers, Noto Sans JP for body), shared button/input/chip/badge styles, and a page fade-in + hover-lift motion language.
- Restyle `Home`/gallery tile view, `Collection` list/table view, `Review.razor`, the photo upload page, and the overview/status page to the new system, with no changes to their data-fetching, routes, or component logic.
- Add a top nav bar (desktop) / bottom tab bar (mobile) shell around the existing pages, replacing the current default Blazor nav (`NavMenu.razor`).
- On the Review page's series-reassignment control specifically: replace the current inline row of text buttons (one per known series, `web-review-series-logos`) with a compact trigger + popover grid (4×N) of icon buttons, so the control no longer grows unbounded with the number of series. Reassignment behavior (`ReassignSeriesAsync`) is unchanged.
- Add a "Tags" concept placeholder to card tiles/table rows (e.g. `Ultra`, `Mega`, `Holo`) shown where the design previously surfaced `Rarity`/element info, since `Tags` is planned but not yet modeled server-side. Until a real `Tags` field exists, derive a placeholder tag client-side from existing `Rarity` (this is explicitly temporary - see design.md).
- Add a small set of generated background/hero art assets (dark ninja/circuit-line motif) to `wwwroot/images/`.

## Capabilities

### New Capabilities
(none - this change is presentation-only; no new user-facing capability is introduced)

### Modified Capabilities
- `web-gallery-page`: visual restyle only (tile layout, colors, typography, hover states). No change to filtering/grouping logic.
- `web-card-table-view` / `web-collection-list`: visual restyle; the `Element` column is replaced by a `Tags` display (placeholder data - see design.md). No change to sorting/grouping logic.
- `web-overview`: visual restyle of stat cards and per-series progress bars. No change to computed values.
- `web-photo-upload`: visual restyle of the upload/dropzone screen. No change to upload behavior.
- `web-card-review-flow`: visual restyle only (status segmented control, filter bar, Confirm All/prev/next). No change to review-status or grouping semantics.
- `web-review-series-logos`: the reassignment control's presentation changes from an inline button row to a popover grid (see What Changes); the logo-icon-per-series behavior it specifies is preserved inside the new popover cells.

## Impact

- Affected code: `NinjagoScanner.Web/wwwroot/app.css` (new token layer + component styles), `NinjagoScanner.Web/Components/Layout/MainLayout.razor` + `NavMenu.razor` (new nav shell), `NinjagoScanner.Web/Components/Pages/*.razor` (markup restructuring to the new layout - no `@code` behavior changes except the Review series-picker markup restructure), `wwwroot/images/` (new background art assets).
- No changes to `NinjagoScanner.CatalogService`, `NinjagoScanner.PictureService`, or any gRPC/proto contract.
- No breaking changes to routes, query params, or data shapes. The `Tags` display is presentation-only placeholder data derived from `Rarity` client-side; it does not add a `Tags` field to `CardListItem`/`CollectionCardDetails` or any sidecar JSON. A future change should introduce a real `Tags` field server-side and remove the placeholder derivation.
