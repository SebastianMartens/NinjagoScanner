## Why

Every content page in `NinjagoScanner.Web` (`Collection`, `Gallery`,
`Overview`, `Review`, `Upload`) uses `@rendermode InteractiveServer` without
`prerender: false`. Blazor Server's default `InteractiveServer` mode
prerenders over plain HTTP and then re-renders again once the SignalR
circuit connects, running each page's `OnInitializedAsync` — and therefore
its full gRPC data fan-out through `CollectionQueryService`/
`CatalogServiceClient`/`PictureServiceClient` — twice per page load. This is
common to every page rather than one, and is the leading explanation for
reports that "most pages are slow": every page load currently does double
the backend work it needs before becoming interactive.

## What Changes

- Disable prerendering (`prerender: false`) on the `@rendermode
  InteractiveServer` declaration for all five affected pages
  (`Collection.razor`, `Gallery.razor`, `Overview.razor`, `Review.razor`,
  `Upload.razor`), so each page's data-fetching runs exactly once per
  navigation.
- No user-facing content or navigation changes — pages render the same
  data, just via one fetch pass instead of two, and become interactive
  without the current prerender-then-reconnect handoff.
- Use `add-opentelemetry-observability` (must land first) to capture a
  "before" trace showing the duplicate fetch pattern and an "after" trace
  confirming a single fetch pass, as evidence the fix works — not just an
  assertion.

## Capabilities

### New Capabilities
- `web-page-render-performance`: the requirement that a page's data fetch
  runs exactly once per user navigation, applicable across
  `NinjagoScanner.Web`'s pages rather than duplicated per existing
  page-specific capability spec.

### Modified Capabilities
(none — existing page capabilities like `web-collection-list`,
`web-gallery-page`, etc. describe what each page shows, which doesn't
change; only how many times the data is fetched to produce it changes,
captured in the new cross-cutting capability above)

## Impact

- **Affected code**: `NinjagoScanner.Web/Components/Pages/Collection.razor`,
  `Gallery.razor`, `Overview.razor`, `Review.razor`, `Upload.razor` (render
  mode declaration only).
- **Depends on**: `add-opentelemetry-observability` — this change's
  validation approach requires tracing to exist first; do not implement
  before that change is deployed.
- **No breaking change**: purely a performance fix, same rendered output.
