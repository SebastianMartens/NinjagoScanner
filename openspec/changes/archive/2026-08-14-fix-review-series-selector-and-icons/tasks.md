## 1. Fix missing series icons

- [x] 1.1 Move `Series8NL_klein.jpg` from `NinjagoScanner.CatalogService/cardInfos/` to `NinjagoScanner.Web/wwwroot/images/` (use `git mv` to preserve history).
- [x] 1.2 Move `Series10_klein.jpg` from `NinjagoScanner.CatalogService/cardInfos/` to `NinjagoScanner.Web/wwwroot/images/` (use `git mv` to preserve history).
- [x] 1.3 In `Review.razor`, update the `Serie 10` entry in `SeriesLogos` from `(string.Empty, "Wolfskopf")` to `("Series10_klein.jpg", "Wolfskopf")`.
- [x] 1.4 Confirm the `Serie 8 Next Level` entry already correctly references `Series8NL_klein.jpg` (no code change needed there - only the asset move in 1.1 was missing).

## 2. Always-visible series buttons

- [x] 2.1 In `Review.razor`, remove the `.review-series-trigger` button and its open/close affordance (caret, `aria-haspopup`/`aria-expanded`).
- [x] 2.2 Render the series grid (currently `.review-series-popover` / `.review-series-cell` buttons) unconditionally on every photo tile, in place of the removed trigger, so all series options are always visible with no click needed to reveal them.
- [x] 2.3 Remove the now-unused popover-open state (`openSeriesPopoverPhotoFileName` field, `IsSeriesPopoverOpen`, `ToggleSeriesPopover`) from `Review.razor`'s `@code` block, keeping `PickSeriesAsync`/`ReassignSeriesAsync` as the click handler for each series button.
- [x] 2.4 Update `NinjagoScanner.Web/wwwroot/app.css`: remove trigger-only styles (`.review-series-trigger*`) and popover-only positioning (`.review-series-popover`'s `position: absolute`/`z-index`), and adjust `.review-series-picker`/`.review-series-cell` so the grid renders inline within the tile's normal flow at a reasonable default size.

## 3. Verify

- [x] 3.1 Run `dotnet build NinjagoScanner.slnx` and `dotnet test NinjagoScanner.slnx` to confirm nothing else references the removed popover state or the moved image paths.
- [x] 3.2 Run the app (CatalogService + PictureService + Web) and manually check the Review page: series buttons are visible on every photo tile without opening anything, selecting a series still reassigns and saves immediately, and `Serie 10` and `Serie 8 Next Level` both show their logo icons instead of falling back to text.
