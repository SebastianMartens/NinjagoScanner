## Why

The review page's photo grid (`.review-photo-grid` in [Review.razor](../../../NinjagoScanner.Web/Components/Pages/Review.razor)) is capped by a `max-width: 1400px` container (`.review-page`) and an uncapped `repeat(auto-fill, minmax(280px, 1fr))` track list. On wide monitors this either wastes the screen's extra width (page content stops at 1400px) or, if the container width were simply removed, lets the row grow past a comfortable count of tiles per row. Reviewers comparing many photos of the same card benefit from seeing more tiles at once without the tiles themselves shrinking or growing.

## What Changes

- Widen the review page's content container so it uses more of the available screen width instead of stopping at `max-width: 1400px`.
- Cap the photo grid (`.review-photo-grid`) at a maximum of six tiles per row, regardless of how much horizontal space is available beyond that point.
- Keep the photo tile's visual size unchanged (same minimum/target width as today) — the change adds columns on wide viewports, it does not shrink or stretch tiles.
- Below six-tiles'-worth of width, the grid continues to wrap to fewer columns exactly as it does today (responsive reflow is unaffected).

## Capabilities

### Modified Capabilities
- `web-card-review-flow`: the "All photos in a group are shown at once" requirement gains a layout constraint on how those simultaneously-shown photos are arranged into rows.

## Impact

- Affected code: [NinjagoScanner.Web/Components/Pages/Review.razor](../../../NinjagoScanner.Web/Components/Pages/Review.razor) (page markup only, no `@code` changes expected), and the `.review-page` / `.review-photo-grid` rules in [NinjagoScanner.Web/wwwroot/app.css](../../../NinjagoScanner.Web/wwwroot/app.css).
- No gRPC, service, or data-model changes; purely a Web-project CSS/layout change.
