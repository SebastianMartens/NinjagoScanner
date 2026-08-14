## Why

On the Review page, reassigning a photo to a different series requires opening a popover behind a dropdown-style trigger button before any series option is visible, adding an extra click to the single most common correction on that page. Separately, two series (`Serie 10` and `Serie 8 Next Level`) never show a logo icon in that same picker even though the artwork exists in the repository, because the icon files were committed into the wrong project's folder and the `Serie 10` mapping entry was never pointed at a file.

## What Changes

- Replace the collapsed series-picker (trigger button + `role="menu"` popover) on the Review page with the series buttons rendered directly and always visible inline on each photo tile, with no dropdown/popover wrapper and no separate open/close step.
- Move `Series8NL_klein.jpg` and `Series10_klein.jpg` from `NinjagoScanner.CatalogService/cardInfos/` into `NinjagoScanner.Web/wwwroot/images/`, where every other series logo already lives and where the Review page actually serves icons from.
- Fix the `Serie 10` logo mapping entry in `Review.razor` to point at `Series10_klein.jpg` instead of an empty file reference, so it renders like every other mapped series.
- **BREAKING**: none (internal UI-only rearrangement and asset fix; no API, sidecar, or gRPC contract changes).

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `web-card-review-flow`: the requirement "A single photo can be reassigned to a different series via a popover" changes — series options are always visible on each photo tile with no trigger/popover step; activating a series option still updates only that photo's `SetName`.
- `web-review-series-logos`: the requirement "Series without a logo mapping fall back to text only" changes — `Serie 10` moves from the "no logo mapping" example to a mapped series showing its logo icon, since its mapping is being fixed rather than left empty. `Serie 8 Next Level` remains mapped but now actually resolves to an existing file.

## Impact

- **Affected code**: `NinjagoScanner.Web/Components/Pages/Review.razor` (series picker markup, `SeriesLogos` dictionary, popover state/toggle logic, related CSS selectors in `NinjagoScanner.Web/wwwroot/app.css`).
- **Affected assets**: `NinjagoScanner.CatalogService/cardInfos/Series8NL_klein.jpg` and `Series10_klein.jpg` move to `NinjagoScanner.Web/wwwroot/images/`.
- **No changes** to CatalogService or PictureService gRPC contracts, sidecar schema, or any other page.
