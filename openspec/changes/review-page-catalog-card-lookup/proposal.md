## Why

Series name and card number now uniquely identify a catalog card (see the archived `catalog-card-identity-series-number` change), so the review page can safely look up and show the catalog card name for each review group. Today the review page only shows the raw `SetName`/`CardNumber` sidecar values in a group's header — no card name — and it groups photos by an exact, un-normalized match on those raw strings, which means spelling/formatting variants (e.g. `"Serie 2"` vs `"Serie_2"`, `"1"` vs `"01"`) that the collection page already treats as the same card can end up split across separate review groups.

## What Changes

- The review page performs a catalog lookup (by normalized series name + card number, reusing the same normalization already used by the collection list) for each review group and shows the resolved catalog card name in the group header, alongside the existing series/card-number label.
- Review groups are now keyed by the **normalized** series name + card number instead of the raw sidecar strings, so photos whose sidecar values normalize to the same catalog card are grouped together even if their raw text differs.
- A group counts as "matched" only when its normalized series name + card number resolves to an actual catalog entry. Photos whose sidecar values don't resolve to any catalog entry (blank series/number, unknown series, or unknown card number) continue to be shown, collected in the existing catch-all bucket — unmatched photos are never hidden.
- Grouping stays photo-driven (a group exists only when at least one photo produces it); the review page does not enumerate the full catalog and does not show empty groups for catalog cards nobody has scanned.

## Capabilities

### Modified Capabilities
- `web-card-review-flow`: review groups are now formed by normalized catalog identity instead of raw sidecar strings, and each matched group's header shows the resolved catalog card name.

## Impact

- `NinjagoScanner.Web/Services/CardCatalogService.cs`: `GetReviewGroupsAsync` — change grouping key from raw `SetName`/`CardNumber` to the normalized key already used by `NormalizeSeriesKey`/`NormalizeCardNumber` (collection-list matching), and resolve/attach the catalog `CardName` for each matched group.
- `NinjagoScanner.Web/Models/CardReviewGroup.cs`: add a field for the resolved catalog card name.
- `NinjagoScanner.Web/Components/Pages/Review.razor`: header rendering (`GroupTitle`) shows the catalog card name for matched groups.
- No changes to `NinjagoScanner.CatalogService` or the gRPC contracts — this reuses existing `ListAllCards`/`ListSeries` data already available to `CardCatalogService`.
