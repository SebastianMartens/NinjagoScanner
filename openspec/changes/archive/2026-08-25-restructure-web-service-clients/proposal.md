## Why

`NinjagoScanner.Web/Services/CardCatalogService.cs` mixes three unrelated concerns behind one name: catalog-only reads, picture-only CRUD, and view-assembly logic that matches/groups/sorts photos against catalog cards for the overview, gallery, series summary, card details, and review pages. The name promises "catalog service" but most of the class is really about combining catalog data with photo data, which makes it hard to tell at a glance which gRPC service a given method actually depends on, and grows unrelated code together every time either concern changes.

## What Changes

- Rename `CardCatalogService.cs` to a narrower `CatalogServiceClient.cs`, keeping only the methods that call `CatalogService` and nothing else (`GetKnownSeriesAsync`, and the private catalog-loading/series-metadata helpers, made accessible to the new aggregation class).
- Move every method that only calls `PictureService` (no catalog dependency) into the existing `PictureServiceClient.cs`: `UploadPhotoAsync`, `GetCardsAsync`, `UpdateCardSidecarAsync`, `UpdateReviewStatusAsync`, `DeletePhotoAsync`, `UpdateSetNameAsync`, `UpdateCardNumberAsync`, `UpdateCardLanguageAsync`, and the private `GetDownloadUrlAsync`/`ListCardEntriesAsync`/`ToCardListItemAsync`/`ToCardListItemsAsync` helpers (the latter two made accessible to the new aggregation class).
- Introduce a new `CollectionQueryService.cs` that owns everything that combines catalog data with photo data: `GetCollectionOverviewAsync`, `GetGalleryCardsAsync`, `GetSeriesSummaryAsync`, `GetCollectionCardDetailsAsync`, `GetReviewGroupsAsync`, and the matching/sorting helpers they use (`BuildOwnershipKey`, `BuildOwnershipLookup`, `NormalizeSeriesKey`, `NormalizeCardNumber`, `NormalizeSeriesNameForSummary`, `BuildCardPhotosAsync`). This class depends on `CatalogServiceClient` and `PictureServiceClient` (constructor injection) instead of opening its own gRPC channels, so the catalog-loading and photo-loading logic isn't duplicated a third time.
- Update DI registration and every Razor page currently injecting `CardCatalogService` to inject whichever of the three new services it actually needs.
- Update `CLAUDE.md`'s architecture section and any affected test fixtures/test class names in `NinjagoScanner.Web.Tests` to match.
- No behavior change: every existing scenario in `web-collection-list`, `web-gallery-page`, `web-overview`, `web-card-review-flow`, `web-review-series-logos`, and `web-photo-upload` must keep passing unchanged - this is purely an internal reorganization of Web-layer classes. No proto, RPC, or other project changes; `NinjagoScanner.CatalogService` and `NinjagoScanner.PictureService` are untouched.

## Capabilities

Pure refactor - no spec-level behavior changes. `.openspec.yaml` sets `skip_specs: true`; no capability deltas are declared or needed.

## Impact

- `NinjagoScanner.Web/Services/CardCatalogService.cs` - renamed to `CatalogServiceClient.cs`, narrowed to catalog-only methods.
- `NinjagoScanner.Web/Services/PictureServiceClient.cs` - gains the pure-picture CRUD methods and helpers listed above.
- `NinjagoScanner.Web/Services/CollectionQueryService.cs` (new) - the catalog+photo aggregation/matching/grouping logic, composing the two clients above.
- `NinjagoScanner.Web/Program.cs` (or wherever these are DI-registered) - registration updated for the new/renamed classes.
- `NinjagoScanner.Web/Components/Pages/{Upload,Review,Gallery,Collection,Overview}.razor` - updated to inject the right service(s) for what each page actually needs.
- `NinjagoScanner.Web.Tests` - existing tests (e.g. `CardCatalogServiceReviewGroupsTests.cs`) updated to reference the new class(es); no test behavior/assertions change.
- `CLAUDE.md` - Web project bullet updated to describe the new class split instead of `CardCatalogService.cs`/`PictureServiceClient.cs`.
- Not affected: `NinjagoScanner.CatalogService`, `NinjagoScanner.PictureService`, any `.proto` file, any deployment/`fly.toml`.
