## 1. Shared design tokens

- [ ] 1.1 In `app.css`, add a `:root` block with the surface/text/accent CSS custom properties listed in design.md.
- [ ] 1.2 Add the Rajdhani + Noto Sans JP Google Fonts `<link>` tags to `App.razor`'s `<head>`.
- [ ] 1.3 Add shared component classes to `app.css`: `.cv-btn-primary`, `.cv-btn-secondary`, `.cv-btn-ghost`, `.cv-input`, `.cv-chip`, `.cv-card`, matching the design reference's button/input/chip treatments (colors, radius, hover states).
- [ ] 1.4 Add the page fade-in keyframe (`cv-fadein`) and apply it to each page's root container.

## 2. Nav shell

- [ ] 2.1 Restructure `NavMenu.razor`'s markup into a top nav bar (desktop, `min-width` media query) and bottom tab bar (mobile), keeping existing `<NavLink>` `href`s/routes unchanged.
- [ ] 2.2 Style both in `NavMenu.razor.css` (or `app.css`, per design.md's token-location decision) using the shared tokens; keep Blazor's built-in `NavLink` active-class behavior working.
- [ ] 2.3 Verify all existing routes (`/`, `/table` or `Collection` route, `/upload`, `/review`, overview route) are still reachable from both nav forms.

## 3. Gallery / tile view (`web-gallery-page`)

- [ ] 3.1 Restyle the card tile component to the dark card treatment (surface color, border, hover lift) - no change to the tile's data bindings.
- [ ] 3.2 Add the `Tags` chip row to the tile using the `TagsForRarity` helper (see design.md); remove the current `Rarity`/element display from the tile.
- [ ] 3.3 Restyle the catalog/collection toggle and series filter controls to `.cv-input`/segmented-control styling. No change to filter logic.

## 4. Table / collection list (`web-card-table-view`, `web-collection-list`)

- [ ] 4.1 Restyle the table shell (header, zebra rows, borders) to the dark treatment.
- [ ] 4.2 Replace the `Element` column with a `Tags` column using the same `TagsForRarity` helper as the gallery tile (single shared helper, not duplicated).
- [ ] 4.3 Restyle the owned-quantity badge and rarity/tag chips to `.cv-chip` variants.

## 5. Review page (`web-card-review-flow`, `web-review-series-logos`)

- [ ] 5.1 Restyle the group header, review-status filter bar, and Confirm All / prev / next controls to the shared token system. No change to filter/navigation logic.
- [ ] 5.2 Restyle each photo tile's 3-segment review-status control to the dark segmented-control treatment. No change to `ReviewStatus` update logic.
- [ ] 5.3 Restructure the series-reassignment control: replace the always-visible per-series button row with a compact trigger button (shows the photo's current series) that opens a 4-column popover grid of the known series. Preserve `web-review-series-logos`' existing per-series icon/caption/fallback rendering *inside* each grid cell.
- [ ] 5.4 Confirm clicking a popover cell still calls the existing `ReassignSeriesAsync(photo, series)` and closes the popover; confirm the trigger button's own click only opens/closes the popover and never itself reassigns.
- [ ] 5.5 Restyle the inline card-number and language correction controls and the collapsible sidecar-details panel to the shared token system. No change to their update logic.

## 6. Upload page (`web-photo-upload`)

- [ ] 6.1 Restyle the camera/gallery dropzone and recent-uploads list to the dark treatment. No change to upload behavior.

## 7. Overview / status page (`web-overview`)

- [ ] 7.1 Restyle the summary stat cards and per-series progress bars to the shared token system and add the bar-grow-in animation. No change to computed values.

## 8. Assets

- [ ] 8.1 Add the generated background/hero art (see `design-reference/assets/`) to `wwwroot/images/`, sized/optimized for web (WebP, matching the project's existing image format convention in `wwwroot/images/`).

## 9. Verification

- [ ] 9.1 Run the Web project and visually confirm every page (gallery, table, review, upload, overview) against the design reference bundle.
- [ ] 9.2 Confirm no `@code` behavior regressions: run existing `NinjagoScanner.Web.Tests` and manually re-verify the Review page's group navigation, status filter, and series reassignment flows end to end.
- [ ] 9.3 Confirm responsive behavior: resize to a mobile viewport and confirm the bottom tab bar replaces the top nav, and the gallery grid/table remain usable (table scrolls horizontally rather than overlapping).
