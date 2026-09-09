## 1. Standard grid CSS

- [x] 1.1 In `NinjagoScanner.Web/wwwroot/app.css`, change `.gallery-grid-standard`'s
      `grid-template-columns` from `repeat(5, 1fr)` to
      `repeat(auto-fill, minmax(275px, 1fr))`, and verify by inspecting the
      compiled rule (or DevTools computed styles) that it resolves to exactly
      5 columns at the page's current default container width (~1452px
      content width).
- [x] 1.2 In the same file, change `.gallery-page`'s `max-width` from `1500px`
      to `4400px` (per design.md's 15-column derivation), and verify the
      Gallery page's overall layout (header, controls, section headers) still
      reads correctly at the default browser width — nothing should look
      different at today's typical window size, since real screens rarely
      reach 4400px.
- [x] 1.3 Confirm the `@media (max-width: 700px)` block's
      `.gallery-grid-standard { grid-template-columns: repeat(2, 1fr); }`
      override is untouched and still wins at narrow viewport widths.

## 2. Manual verification

- [x] 2.1 Run the Web app (with CatalogService and PictureService running) and
      open `/gallery` with a series that has a non-puzzle category with more
      than 15 cards; at 100% browser zoom, confirm the standard grid still
      shows 5 tiles per row, matching today's appearance.
      Verified against the real running app's production `app.css` via a
      Playwright-driven headless browser (local PictureService couldn't
      serve live photo data — its DynamoDB table name is stale in
      `appsettings.Development.json`, a pre-existing environment issue
      unrelated to this change — so a synthetic grid of placeholder tiles
      using the exact same CSS classes Gallery.razor renders was measured
      instead of the live page). At a 1500px viewport (matching today's
      effective container width), the standard grid renders exactly 5
      tiles per row at 277.6px each — unchanged from before this change.
- [x] 2.2 Zoom out with Ctrl+Mouse Wheel in steps (e.g. 90%, 75%, 50%, 33%)
      and confirm the standard grid's tiles-per-row count increases as more
      space becomes available, reaching 15 tiles per row at some zoom level,
      and never exceeding 15 or shrinking tiles below a legible size beyond
      that point.
      Verified by widening the viewport (CSS-equivalent to zooming out) in
      the same harness: 1500px->5 cols, 2000px->6, 3000px->10, 4400px->15
      (the derived cap), 5200px->still 15 with tile width unchanged
      (275.2px) — extra space becomes margin, not smaller/more tiles.
- [x] 2.3 At the same zoom levels, confirm a puzzle sub-group section (e.g. a
      "Puzzle Cards" category) keeps rendering exactly 3 tiles per row,
      unaffected by the standard grid's column changes.
      Verified in the same runs: puzzle grid stayed at exactly 3 tiles per
      row at every width from 375px through 5200px.
- [x] 2.4 Resize the browser window down to a narrow (phone-width) viewport
      and confirm the standard grid still shows 2 tiles per row via the
      existing mobile breakpoint.
      Verified at a 375px viewport: standard grid shows 2 tiles per row
      (155.5px each) via the untouched `@media (max-width: 700px)` rule.
- [x] 2.5 Confirm `dotnet build NinjagoScanner.slnx` succeeds (CSS-only
      change, but this catches accidental syntax issues from editing
      alongside other work).
