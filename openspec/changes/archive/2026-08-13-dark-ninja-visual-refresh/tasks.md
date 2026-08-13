## 1. Shared design tokens

- [x] 1.1 In `app.css`, add a `:root` block with the surface/text/accent CSS custom properties listed in design.md.
- [x] 1.2 Add the Rajdhani + Noto Sans JP Google Fonts `<link>` tags to `App.razor`'s `<head>`.
- [x] 1.3 Add shared component classes to `app.css`: `.cv-btn-primary`, `.cv-btn-secondary`, `.cv-btn-ghost`, `.cv-input`, `.cv-chip`, `.cv-card`, matching the design reference's button/input/chip treatments (colors, radius, hover states).
- [x] 1.4 Add the page fade-in keyframe (`cv-fadein`) and apply it to each page's root container.

## 2. Nav shell

- [x] 2.1 Restructure `NavMenu.razor`'s markup into a top nav bar (desktop, `min-width` media query) and bottom tab bar (mobile), keeping existing `<NavLink>` `href`s/routes unchanged.
- [x] 2.2 Style both in `NavMenu.razor.css` (or `app.css`, per design.md's token-location decision) using the shared tokens; keep Blazor's built-in `NavLink` active-class behavior working.
- [x] 2.3 Verify all existing routes (`/`, `/table` or `Collection` route, `/upload`, `/review`, overview route) are still reachable from both nav forms. Both `NavMenu` variants render all six routes (`/`, `gallery`, `table`, `collection`, `review`, `upload`) via `<NavLink>`, so routing is unaffected.

## 3. Gallery / tile view (`web-gallery-page`)

- [x] 3.1 Restyle the card tile component to the dark card treatment (surface color, border, hover lift) - no change to the tile's data bindings.
- [x] 3.2 Add the `Tags` chip row to the tile using the `TagsForRarity` helper (see design.md); remove the current `Rarity`/element display from the tile. The gallery tile had no `Rarity`/element display today (verified in code), so this was purely additive - added `GalleryCardItem.Rarity` (populated from the matched photo in `CardCatalogService.GetGalleryCardsAsync`) and rendered via the shared `CardTagHelper.TagsForRarity` in `Gallery.razor`.
- [x] 3.3 Restyle the catalog/collection toggle and series filter controls to `.cv-input`/segmented-control styling. No change to filter logic. Note: `Gallery.razor` has no catalog/collection toggle (that concept only existed in the design mockup's fictional data) - restyled the two real controls (series select, category select) only.

## 4. Table / collection list (`web-card-table-view`, `web-collection-list`)

- [x] 4.1 Restyle the table shell (header, zebra rows, borders) to the dark treatment.
- [x] 4.2 Add a `Tags` column to the `/table` view using the same `TagsForRarity` helper as the gallery tile (single shared helper, not duplicated) - see `specs/web-card-table-view/spec.md`.
- [x] 4.3 Restyle the owned-quantity badge and rarity/tag chips to `.cv-chip` variants.

## 5. Review page (`web-card-review-flow`, `web-review-series-logos`)

- [x] 5.1 Restyle the group header, review-status filter bar, and Confirm All / prev / next controls to the shared token system. No change to filter/navigation logic.
- [x] 5.2 Restyle each photo tile's 3-segment review-status control to the dark segmented-control treatment. No change to `ReviewStatus` update logic.
- [x] 5.3 Restructure the series-reassignment control: replace the always-visible per-series button row with a compact trigger button (shows the photo's current series) that opens a 4-column popover grid of the known series - see `specs/web-card-review-flow/spec.md`. Mapped-series cells show the logo icon only (no visible caption - series name is exposed via `title`/alt text instead, per the refined design.md goal); unmapped series keep the text-only fallback - see `specs/web-review-series-logos/spec.md`.
- [x] 5.4 Confirm activating a popover cell still calls the existing `ReassignSeriesAsync(photo, series)` and closes the popover; confirm the trigger button's own activation only opens/closes the popover and never itself reassigns. `PickSeriesAsync` clears the popover state then calls `ReassignSeriesAsync`; `ToggleSeriesPopover` (wired to the trigger) only flips `openSeriesPopoverPhotoFileName` and never touches `SetName`.
- [x] 5.5 Restyle the inline card-number and language correction controls and the collapsible sidecar-details panel to the shared token system. No change to their update logic.

## 6. Upload page (`web-photo-upload`)

- [x] 6.1 Restyle the camera/gallery dropzone and recent-uploads list to the dark treatment. No change to upload behavior. (This page has no recent-uploads list in the real app - only the dropzone/file-picker card - which was restyled; nothing else exists to restyle.)

## 7. Overview / status page (`web-overview`)

- [x] 7.1 Restyle the summary stat cards and per-series progress bars to the shared token system and add the bar-grow-in animation. No change to computed values.

## 8. Assets

- [x] 8.1 The 4 background/hero PNGs couldn't be pulled automatically (design-sync tool's file read caps at 256KB, all 4 exceeded it - see git history of `design-reference/assets/README.md`). User manually placed `circuit-dojo` as `NinjagoScanner.Web/wwwroot/images/background.png`; wired it into `app.css`'s `body` background (dark gradient overlay + `center/cover fixed`), matching the design reference's treatment. Kept as PNG rather than converting to WebP - the project's existing `wwwroot/images/*.jpg` convention is JPG, not WebP as originally drafted, and a single ~1MB cached background image doesn't warrant a conversion pipeline for this change. The other 3 hero images (elemental-mist, hero-rooftop, misty-peaks) were never used by any page in this change's scope, so they remain unneeded.

## 9. Verification

- [x] 9.1 Run the Web project and visually confirm every page (gallery, table, review, upload, overview) against the design reference bundle. Ran `NinjagoScanner.Web` + `CatalogService` + `PictureService` together locally and drove all six pages with Playwright against real catalog data (3915 cards): dark theme, Rajdhani/Noto Sans JP fonts, purple/green accents, and the nav shell all render correctly.
- [x] 9.2 Confirm no `@code` behavior regressions: run existing `NinjagoScanner.Web.Tests` and manually re-verify the Review page's group navigation, status filter, and series reassignment flows end to end. `dotnet test` → 21/21 passed. Manually verified in-browser: the series popover opens on trigger click, shows the 4-column grid with logos where mapped (e.g. Serie 2) and text-only where not (Serie 1), only one popover is open at a time, and Confirm All / status segments still work.
- [x] 9.3 Confirm responsive behavior: resize to a mobile viewport and confirm the bottom tab bar replaces the top nav, and the gallery grid/table remain usable (table scrolls horizontally rather than overlapping). Verified at 390×844: top nav hidden, bottom tab bar shown with the active route highlighted in green.
- [x] 9.4 Run `openspec validate 2026-08-13-dark-ninja-visual-refresh --strict` and fix any reported issues.
