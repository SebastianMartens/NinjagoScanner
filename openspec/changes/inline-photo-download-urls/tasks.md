## 1. Proto contract

- [x] 1.1 In `NinjagoScanner.PictureService/Protos/picture_service.proto`, add a `download_url` field to `CardEntry`, and remove the `GetPhotoDownloadUrls` RPC declaration along with the `GetPhotoDownloadUrlsRequest`/`GetPhotoDownloadUrlsResponse` messages.
- [x] 1.2 Apply the identical edit to `NinjagoScanner.Web/Protos/picture_service.proto`, keeping the two copies in sync.

## 2. PictureService: ListCards

- [x] 2.1 In `PictureScannerGrpcService.ListCards`, replace the per-photo `sidecarCache.GetAsync` loop with a single bulk read via `SidecarCache`/`SidecarTable`'s existing `ListAllAsync`, building an in-memory lookup keyed by photo ID before iterating the S3-listed photo IDs.
- [x] 2.2 For each photo ID from `photoStore.ListPhotoIdsAsync`, look up its sidecar in that in-memory lookup (treating a miss as unscanned, same as today) and sign a download URL via `photoStore.CreateDownloadUrlAsync`, without any `ExistsAsync` call.
- [x] 2.3 Update `ToCardEntry` (or its call site) to populate the new `download_url` field on the returned `CardEntry`.

## 3. PictureService: remove the batch RPC

- [x] 3.1 Remove the `GetPhotoDownloadUrls` handler from `PictureScannerGrpcService`.
- [x] 3.2 Confirm the singular `GetPhotoDownloadUrl` handler is untouched.

## 4. Web: PictureServiceClient

- [x] 4.1 In `PictureServiceClient`, remove `GetDownloadUrlsAsync` and the `ToCardListItemsAsync` helper.
- [x] 4.2 Update `GetCardsAsync`/`ToCardListItem` to read `entry.DownloadUrl` directly instead of taking a download-URL lookup dictionary.
- [x] 4.3 Confirm `GetDownloadUrlAsync` (singular) and its use in `UploadPhotoAsync` are untouched.

## 5. Web: CollectionQueryService

- [x] 5.1 In `GetGalleryCardsAsync`, replace the per-matched-photo `pictureServiceClient.GetDownloadUrlAsync` call with reading `matchedPhoto.DownloadUrl` directly.
- [x] 5.2 In `BuildCardPhotosAsync`, replace the per-photo `pictureServiceClient.GetDownloadUrlAsync` call with reading `entry.DownloadUrl` directly.

## 6. Tests

- [x] 6.1 Update/replace `NinjagoScanner.PictureService.Tests/Services/PictureScannerGrpcServiceGetPhotoDownloadUrlTests.cs` so its batch-RPC test cases are removed and its singular-RPC test cases remain, adding coverage for `ListCards` now returning `download_url` per entry and resolving sidecar data via the bulk path (including a photo with no sidecar yet, and hundreds of photos exercising the bulk-not-per-photo behavior if feasible against the test double).
- [x] 6.2 Update `NinjagoScanner.Web.Tests/Services/PictureServiceClientGetCardsAsyncTests.cs` so it no longer relies on the removed batch RPC and instead verifies `GetCardsAsync` reads `download_url` straight from `ListCards`' response.
- [x] 6.3 Search both test projects for any other reference to `GetPhotoDownloadUrls`/`GetDownloadUrlsAsync`/`ToCardListItemsAsync` and update or remove accordingly.
- [x] 6.4 Run `dotnet test NinjagoScanner.slnx` and confirm everything passes.

## 7. Manual verification

- [ ] 7.1 Run all three services locally, load `/review` with a realistic number of photos, and confirm the page loads noticeably faster than before.
- [ ] 7.2 Spot-check `/gallery` and a collection detail page (`/collection`) still show working photo images.
- [ ] 7.3 Upload a new photo and confirm it still gets a working display URL immediately after upload.
