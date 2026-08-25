## 1. `CatalogServiceClient` (catalog-only)

- [x] 1.1 Rename `NinjagoScanner.Web/Services/CardCatalogService.cs` to `CatalogServiceClient.cs`, rename the class to `CatalogServiceClient`, and drop the constructor's `pictureServiceAddress`/`maxUploadBytes` parameters (keep only `catalogServiceAddress`).
- [x] 1.2 Keep only `GetKnownSeriesAsync` and make `LoadCardsFromCatalogServiceAsync` and `LoadSeriesMetadataAsync` public, renamed `ListCatalogCardsAsync` and `GetSeriesMetadataAsync` respectively; remove every other method/helper (they move in sections 2-3).
- [x] 1.3 Remove now-unused `using`s and the `SeriesMetadata` nested type stays (still used by `GetSeriesMetadataAsync`'s return shape).

## 2. `PictureServiceClient` (picture-only)

- [x] 2.1 Move `UploadPhotoAsync`, `GetCardsAsync`, `UpdateCardSidecarAsync`, `UpdateReviewStatusAsync`, `DeletePhotoAsync`, `UpdateSetNameAsync`, `UpdateCardNumberAsync`, `UpdateCardLanguageAsync` from the old `CardCatalogService` into `PictureServiceClient.cs`, adjusting field/property names (`pictureServiceAddress`, `MaxUploadBytes`, `EnsureUploadIsValid`) to fit its existing primary constructor (`pictureServiceAddress`, `catalogServiceAddress`) - add a `maxUploadBytes` constructor parameter since `EnsureUploadIsValid`/`UploadPhotoAsync` need it.
- [x] 2.2 Move the private helpers `GetDownloadUrlAsync`, `LoadCardEntriesAsync` (rename `ListCardEntriesAsync`, make public), `ToCardListItemAsync`, `ToCardListItemsAsync`, `ParseScannedAtUtc`, `NormalizeNullable` into `PictureServiceClient.cs`; make `GetDownloadUrlAsync` public (needed by `CollectionQueryService`).
- [x] 2.3 Move the `WebConfig.SupportedExtensions`/upload-validation logic (`EnsureUploadIsValid`) along with `UploadPhotoAsync`.

## 3. `CollectionQueryService` (aggregation)

- [x] 3.1 Create `NinjagoScanner.Web/Services/CollectionQueryService.cs` with a primary constructor taking `CatalogServiceClient catalogServiceClient` and `PictureServiceClient pictureServiceClient`.
- [x] 3.2 Move `GetCollectionOverviewAsync`, `GetGalleryCardsAsync`, `GetSeriesSummaryAsync`, `GetCollectionCardDetailsAsync`, `GetReviewGroupsAsync`, `BuildCardPhotosAsync`, `BuildOwnershipLookup`, `BuildOwnershipKey`, `NormalizeSeriesKey`, `NormalizeCardNumber`, `NormalizeSeriesNameForSummary`, `BuildAnalysisStatusCounts`, `BuildReviewStatusCounts`, `ToCollectionSidecar` into this class, rewriting their internals to call `catalogServiceClient.ListCatalogCardsAsync()`/`GetSeriesMetadataAsync()`/`GetKnownSeriesAsync()` and `pictureServiceClient.ListCardEntriesAsync()`/`GetCardsAsync()`/`GetDownloadUrlAsync()` instead of opening `GrpcChannel`s directly.
- [x] 3.3 Double-check `GetReviewGroupsAsync` still uses `pictureServiceClient.GetCardsAsync()` (photos with resolved `ImageUrl`) exactly as `ToCardListItemsAsync` did before the move, so its behavior (and the batched-download-URL fix from `batch-photo-download-urls`, if already applied) is preserved unchanged.

## 4. Dependency injection & pages

- [x] 4.1 Update `NinjagoScanner.Web/Program.cs`: register `CatalogServiceClient`, `PictureServiceClient` (now taking `maxUploadBytes` too), then `CollectionQueryService` depending on both, replacing the old two-line `CardCatalogService`/`PictureServiceClient` registration.
- [x] 4.2 Update `Upload.razor` to inject `PictureServiceClient` instead of `CardCatalogService`.
- [x] 4.3 Update `Review.razor` to inject `CollectionQueryService` (for `GetReviewGroupsAsync`) and `PictureServiceClient`/`CatalogServiceClient` for whatever else it currently calls on `CardCatalogService` (sidecar updates, known series list, delete).
- [x] 4.4 Update `Gallery.razor`, `Collection.razor`, `Overview.razor` similarly - inject `CollectionQueryService` for the aggregation calls and `CatalogServiceClient`/`PictureServiceClient` for anything else each page uses.

## 5. Tests

- [x] 5.1 `CardCatalogServiceReviewGroupsTests.cs` - construct `CatalogServiceClient` + `PictureServiceClient` + `CollectionQueryService`, call `GetReviewGroupsAsync` on the latter; no assertion changes.
- [x] 5.2 `CardCatalogServiceCardNumberBeforeCategoryTests.cs` and `CardCatalogServiceGalleryTests.cs` - same wiring, calling `GetCollectionOverviewAsync`/`GetGalleryCardsAsync` on `CollectionQueryService`.
- [x] 5.3 `CardCatalogServiceAnalysisStatusCountsTests.cs` - same wiring, calling `GetSeriesSummaryAsync` on `CollectionQueryService`.
- [x] 5.4 `CardCatalogServiceDeletePhotoTests.cs` - construct all three; `DeletePhotoAsync`/`GetCardsAsync` go through `PictureServiceClient`, `GetGalleryCardsAsync` through `CollectionQueryService`.
- [x] 5.5 `CardCatalogServiceUploadValidationTests.cs` - construct only `PictureServiceClient`, call `UploadPhotoAsync` on it.
- [x] 5.6 Rename the test files above to match their primary subject where it's no longer `CardCatalogService` (e.g. `CollectionQueryServiceReviewGroupsTests.cs`, `PictureServiceClientUploadValidationTests.cs`), keeping each file's existing scenarios/assertions unchanged.
- [x] 5.7 Run `dotnet build NinjagoScanner.slnx` and `dotnet test NinjagoScanner.slnx`, confirm everything is green.

## 6. Documentation

- [x] 6.1 Update `CLAUDE.md`'s `NinjagoScanner.Web` architecture bullet to describe `CatalogServiceClient.cs`, `PictureServiceClient.cs`, and `CollectionQueryService.cs` instead of `CardCatalogService.cs`/`PictureServiceClient.cs`.
