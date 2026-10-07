## 1. Shared gRPC contract

- [x] 1.1 In `NinjagoScanner.Web/Protos/picture_service.proto`: add `bool rotated_180 = 16;` to `CardEntry`; add `UpdateRotationRequest { string photo_id = 1; bool rotated_180 = 3; string collection_id = 4; }` and `UpdateRotationResponse { bool success = 1; }`; add `rpc UpdateRotation (UpdateRotationRequest) returns (UpdateRotationResponse);` to the `CardPictureService` service, next to `UpdateReviewStatus`. Verify `dotnet build NinjagoScanner.slnx` regenerates the C# stubs without error.

## 2. PictureService (Python)

- [x] 2.1 Run `uv run python scripts/gen_proto.py` (from `picture_service/`) to regenerate the gRPC stubs from the updated proto, and verify `UpdateRotationRequest`/`UpdateRotationResponse`/`UpdateRotation` appear in the generated code.
- [x] 2.2 Add `rotated_180: bool = False` to `SidecarRecord` in `models.py`, documented as part of the Judged section alongside `review_status`.
- [x] 2.3 Thread `rotated_180` through `sidecar_table.py` (DynamoDB attribute read/write) and `sidecar_store.py`/`sidecar_cache.py` (read-through/write-through), following the exact pattern `review_status` already uses. Verify with a unit test that writing then reading a sidecar round-trips `rotated_180`.
- [x] 2.4 Implement the `UpdateRotation` RPC handler in `picture_scanner_service.py`, mirroring `UpdateReviewStatus`'s handler: scoped to `collection_id`, creates a sidecar (reporting `notAnalyzed`) if none exists, otherwise updates only `rotated_180`, leaving every other field untouched and not touching `analysis_status` on an existing record.
- [x] 2.5 Include `rotated_180` in the `CardEntry`-equivalent response built by `ListCards`, so `rotated_180` round-trips without a flag defaulting to `false` forever. Verify with a test that a rotated photo's `rotated_180=true` is returned by `ListCards`.
- [x] 2.6 Add `uv run pytest` test cases in `picture_service/tests` covering: `UpdateRotation` creates a not-analyzed sidecar when none exists; `UpdateRotation` flips only the flag on an existing sidecar and leaves `analysis_status`/`review_status`/etc. unchanged; `UpdateRotation` is scoped per `collection_id` (same file name in two collections edited independently, mirroring the existing sidecar-editing test suite's collection-isolation tests). Verify `uv run pytest` passes.

## 3. NinjagoScanner.Web

- [x] 3.1 Add `UpdateRotationAsync(string photoId, bool rotated180, CancellationToken cancellationToken = default)` to `Services/PictureServiceClient.cs`, mirroring the existing `UpdateReviewStatusAsync`/`UpdateCardLanguageAsync` methods (collection ID resolved internally via `GetCollectionIdAsync`, same as those methods).
- [x] 3.2 Thread `Rotated180` through `Services/CollectionQueryService.cs`'s view-model construction for the three view models that actually carry a photo image: `CardListItem` (via `ToCardListItem`, already added), `GalleryCardItem` (new `Rotated180` property, set in `GetGalleryCardsAsync` from `matchedPhoto.Rotated180`), and `CollectionCardPhotoItem` (new `Rotated180` property, set in `BuildCardPhotosAsync` from `entry.Rotated180`). The Overview page renders no photo images, so `CollectionCardItem` needs no change.
- [x] 3.3 In `Components/Pages/Review.razor`: add a rotate button next to the existing review-status control (same `review-photo-tile` card), wired to a `ToggleRotationAsync(photo)` handler that calls `PictureServiceClient.UpdateRotationAsync` with the flipped value and updates the photo's local `Rotated180` state on success, following the same busy-state (`isBusy`) and error-handling pattern as `SetReviewStatusAsync`/`SaveLanguageAsync`.
- [x] 3.4 Add a CSS class (e.g. `rotated-180`) to the app's stylesheet that applies `transform: rotate(180deg)` to the `<img>`, and apply it conditionally on the `<img>` in `Review.razor` (`review-photo-image`), `Gallery.razor` (`gallery-tile-image`), and `Collection.razor` (`detail-image`).
- [x] 3.5 Verify in a running app (`dotnet run` for CatalogService, `uv run python -m picture_service.main` for PictureService, `dotnet run` for Web): rotate a photo from the Review page, confirm it flips immediately; confirm the same photo appears rotated in the gallery and collection detail views; toggle it back and confirm it returns to normal everywhere.

## 4. Tests

- [x] 4.1 Add `UpdateRotation` handling to `NinjagoScanner.Web.Tests/Fixtures/PictureServiceTestHost.cs`'s fake `CardPictureServiceBase` implementation, following the existing `UpdateReviewStatus` fake handler's pattern (in-memory state, no real gRPC call to Python).
- [x] 4.2 Add a `NinjagoScanner.Web.Tests` test exercising the Review page's rotate control end-to-end against the fakes (`dotnet test NinjagoScanner.Web.Tests --filter "FullyQualifiedName~Rotation"`), verifying the toggle calls `UpdateRotation` and the resulting view model reflects the new `Rotated180` value.
- [x] 4.3 Run the full suite (`dotnet test NinjagoScanner.slnx` and `uv run pytest` from `picture_service/`) and verify both pass.

## 5. OpenSpec housekeeping

- [x] 5.1 Run `openspec validate rotate-images --strict` and fix any reported issues.
