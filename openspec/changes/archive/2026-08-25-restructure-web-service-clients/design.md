## Context

See [proposal.md](proposal.md) for motivation. Current state: `NinjagoScanner.Web/Services/CardCatalogService.cs` (674 lines) opens its own `GrpcChannel` to both `CatalogService` and `PictureService` for every call, and `PictureServiceClient.cs` currently only has `ScanAsync`. Both are registered as singletons in `Program.cs`:
```csharp
builder.Services.AddSingleton(_ => new CardCatalogService(catalogServiceAddress, pictureServiceAddress, maxUploadBytes));
builder.Services.AddSingleton(_ => new PictureServiceClient(pictureServiceAddress, catalogServiceAddress));
```
Six test files under `NinjagoScanner.Web.Tests/Services/` construct `CardCatalogService` directly against in-process test hosts (`CatalogServiceTestHost`, `PictureServiceTestHost`) and call its methods; each test file maps cleanly to one of the three post-split classes (see tasks.md).

## Goals / Non-Goals

**Goals:**
- Every class's public surface only calls the gRPC service(s) implied by its name.
- No method is duplicated between classes - the aggregation class reuses the catalog/picture clients' methods rather than re-opening channels itself.
- Zero behavior change: identical inputs produce identical outputs for every page and every existing test (once updated to construct the new classes).

**Non-Goals:**
- Changing what any RPC does, or touching `NinjagoScanner.CatalogService`/`NinjagoScanner.PictureService` - explicitly ruled out by the user for this change.
- Changing the matching/normalization algorithm (`BuildOwnershipKey`, `NormalizeSeriesKey`, `NormalizeCardNumber`) - it moves, but its logic is untouched.
- Introducing an interface/abstraction layer beyond the three concrete classes - three classes with clear, non-overlapping responsibilities is enough; no factory, no `ICatalogServiceClient` etc. unless a future need (e.g. mocking in tests) actually requires it.

## Decisions

- **Three classes, not two.** Folding the aggregation methods into either `CatalogServiceClient` or `PictureServiceClient` would reintroduce the same "which service does this actually call" ambiguity the proposal is trying to remove - `GetReviewGroupsAsync` genuinely needs both. A third class (`CollectionQueryService`) that depends on the other two via constructor injection keeps every class single-purpose.
- **Composition over duplicated gRPC calls.** `CollectionQueryService` takes `CatalogServiceClient` and `PictureServiceClient` as constructor dependencies and calls their public methods (e.g. `pictureServiceClient.GetCardsAsync()`, `pictureServiceClient.ListCardEntriesAsync()`, `catalogServiceClient.ListCatalogCardsAsync()`) instead of opening its own `GrpcChannel`s. This means:
  - `PictureServiceClient` needs a new public `ListCardEntriesAsync()` returning raw `CardEntry` (today's private `LoadCardEntriesAsync`) for the aggregation methods that only need counts/matching (`GetCollectionOverviewAsync`, `GetSeriesSummaryAsync`), not per-photo download URLs.
  - `PictureServiceClient` needs a public `GetDownloadUrlAsync(photoId)` (today's private helper) for `GetGalleryCardsAsync`'s single matched-photo URL and `CollectionQueryService`'s `BuildCardPhotosAsync`.
  - `CatalogServiceClient` needs today's private `LoadCardsFromCatalogServiceAsync`/`LoadSeriesMetadataAsync` made public (renamed `ListCatalogCardsAsync`/`GetSeriesMetadataAsync` to match the codebase's existing public-method naming, e.g. `GetKnownSeriesAsync`).
- **`CollectionQueryService` keeps the matching/sorting helpers as its own private statics** (`BuildOwnershipKey`, `BuildOwnershipLookup`, `NormalizeSeriesKey`, `NormalizeCardNumber`, `NormalizeSeriesNameForSummary`, `BuildCardPhotosAsync`) rather than exposing them from `CatalogServiceClient`/`PictureServiceClient` - they're specific to how catalog and photo data get matched together, which is exactly `CollectionQueryService`'s job, not either client's.
- **Rename over new-file-plus-delete.** `CardCatalogService.cs` becomes `CatalogServiceClient.cs` (same file, most content removed) rather than leaving `CardCatalogService.cs` in place with reduced content, so the filename always matches the class's actual scope.
- **DI stays singleton, constructor injection resolves order.** `CollectionQueryService`'s constructor takes `CatalogServiceClient` and `PictureServiceClient`; register those two first, then `CollectionQueryService` referencing them, mirroring the existing `AddSingleton(_ => new X(...))` factory-lambda style already used in `Program.cs` rather than introducing constructor-based DI resolution (`AddSingleton<CollectionQueryService>()`) inconsistent with the rest of the file.

## Risks / Trade-offs

- [Six existing test files construct `CardCatalogService` directly and call a mix of methods now split across three classes (e.g. `CardCatalogServiceDeletePhotoTests` calls `DeletePhotoAsync` + `GetCardsAsync` (`PictureServiceClient`) and `GetGalleryCardsAsync` (`CollectionQueryService`))] → Each test file's setup constructs whichever of the three classes it needs (one, two, or all three); no test's assertions or scenario descriptions change, only which object the calls go through. Covered explicitly per-file in tasks.md.
- [Renaming a widely-injected class touches every Razor page that injects it] → Mechanical `@inject` update in each of the 5 affected pages; the compiler will catch any missed reference since the old type name stops existing.
- [Making previously-private helpers public on `CatalogServiceClient`/`PictureServiceClient` slightly widens their surface] → Acceptable: these are internal-to-Web `internal sealed class`es (not `public`), so the widened surface is only visible within `NinjagoScanner.Web` (and `NinjagoScanner.Web.Tests` via `InternalsVisibleTo`), not a public API change.

## Migration Plan

No deployment/runtime migration - this is a compile-time-only reorganization within `NinjagoScanner.Web`. Land it as one PR/build: rename, move, update DI, update pages, update tests, `dotnet build`/`dotnet test` green. Rollback is a normal revert; no data or running-service state is affected.
