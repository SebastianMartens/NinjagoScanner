## 1. Proto contract

- [x] 1.1 Add `GetPhotoDownloadUrls` RPC plus `GetPhotoDownloadUrlsRequest`/`GetPhotoDownloadUrlsResponse` messages to `NinjagoScanner.PictureService/Protos/picture_service.proto` (repeated `photo_ids` in; a map or repeated `{photo_id, download_url}` pairs out, covering only the photo IDs that exist).
- [x] 1.2 Mirror the same RPC/messages in `NinjagoScanner.Web/Protos/picture_service.proto`, keeping both copies in sync per existing repo convention.

## 2. PictureService implementation

- [x] 2.1 Implement `GetPhotoDownloadUrls` in `PictureScannerGrpcService.cs`, reusing `photoStore.ExistsAsync`/`photoStore.CreateDownloadUrlAsync` per requested photo ID, omitting IDs that don't exist rather than failing the call (per `specs/picture-service-photo-download`).
- [x] 2.2 Handle an empty `photo_ids` list by returning an empty response without error.
- [x] 2.3 No fixture wiring needed: `PictureServiceTestHost.cs` (`NinjagoScanner.Web.Tests/Fixtures`) maps the whole `PictureScannerGrpcService` class via `MapGrpcService<PictureScannerGrpcService>()`, so the new RPC becomes callable in-process as soon as 2.1 is implemented. Confirm this by writing the test in 4.1 against the existing test host without touching the fixture.

## 3. Web consumer

- [x] 3.1 Add a `GetDownloadUrlsAsync(IReadOnlyCollection<string> photoIds, CancellationToken)` helper to `PictureServiceClient.cs` that calls the new batch RPC once and returns a photo-ID-to-URL lookup.
- [x] 3.2 Update `PictureServiceClient`'s private `ToCardListItemsAsync` (and `ToCardListItemAsync`'s call site) to collect all photo IDs up front, call the batch helper once, and build each `CardListItem`'s `ImageUrl` from the returned lookup instead of awaiting `GetDownloadUrlAsync` per item in the loop. `CollectionQueryService.GetReviewGroupsAsync` (`CollectionQueryService.cs`) needs no change since it consumes `GetCardsAsync` as-is.
- [x] 3.3 Leave `CollectionQueryService.BuildCardPhotosAsync` (collection detail view) and `GetGalleryCardsAsync` (gallery), and their existing per-photo `GetDownloadUrlAsync` calls, unchanged, per design.md's non-goals.

## 4. Tests

- [x] 4.1 Add a PictureService-side test covering the batch RPC: multiple existing IDs, an empty list, and a mix of existing/missing IDs.
- [x] 4.2 Add/update a `CollectionQueryServiceReviewGroupsTests.cs` or `PictureServiceClient`-focused test asserting review-group/card-list photo URLs are resolved via one batched call rather than one call per photo (e.g. assert on call count against the test host, or on correct `ImageUrl` values for many photos in one call).
- [x] 4.3 Run `dotnet test NinjagoScanner.slnx` and confirm existing review/collection/gallery tests still pass unchanged.

## 5. Deployment

- [ ] 5.1 Deploy PictureService (with the new RPC) before deploying Web (with the new caller), per design.md's Migration Plan, so Web never calls an RPC that doesn't exist yet on PictureService.
