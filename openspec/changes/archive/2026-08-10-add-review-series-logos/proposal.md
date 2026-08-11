## Why

On the Review page, the series-reassignment buttons show only the series name as text. To pick the right series, the user has to compare the small symbol printed in the lower-right corner of the card photo against their own memory of what each series' official symbol looks like. Showing the official series logo next to each button lets the user compare symbols directly, side by side.

## What Changes

- Add a small (~18-20px) series logo icon inline on each series-reassignment button on the Review page, using the image assets already present at `NinjagoScanner.Web/wwwroot/images/Series{N}[NL]_klein.jpg`.
- Add a hardcoded mapping (series display name → image file + caption) directly in `Review.razor`'s `@code` block. The mapping is not computed from a filename convention and does not read the CatalogService `Logo` JSON field.
- The caption is used only as the icon's `alt`/tooltip text; the button's visible label keeps showing the raw series name exactly as today.
- Series with no entry in the mapping (e.g. `Serie 1`, which has no official logo) render exactly as today: a plain text-only button, no icon slot, no placeholder image.
- Seed the mapping with one real entry (`Serie 2`) as a worked example. Populating the mapping for the remaining series is left as manual follow-up work outside this change.

## Capabilities

### New Capabilities
- `web-review-series-logos`: Displaying a series' official logo icon (with alt-text caption) on its reassignment button on the Review page, sourced from a hardcoded name-to-asset mapping, with graceful text-only fallback when no mapping entry exists.

### Modified Capabilities
(none - no existing archived specs cover the Review page's series-reassignment buttons yet)

## Impact

- Affected code: `NinjagoScanner.Web/Components/Pages/Review.razor` (series-reassignment button markup and a new mapping dictionary in `@code`), `NinjagoScanner.Web/wwwroot/app.css` (`.review-btn` / `.review-series-buttons` styling for the inline icon).
- No changes to `NinjagoScanner.CatalogService`, `NinjagoScanner.PictureService`, or any gRPC/proto contract - the image assets already live in `NinjagoScanner.Web/wwwroot/images/` and are served by the existing `app.MapStaticAssets()` call, so this is entirely front-end/Web-project scoped.
- No breaking changes.
