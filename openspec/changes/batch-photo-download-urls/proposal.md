## Why

`Review.razor` calls `CollectionQueryService.GetReviewGroupsAsync`, which fetches every card photo via `PictureServiceClient.GetCardsAsync` (backed by `ListCards`) and then resolves each photo's display URL with one `GetPhotoDownloadUrl` gRPC call per photo, awaited sequentially in a loop inside `PictureServiceClient`'s private `ToCardListItemsAsync`/`ToCardListItemAsync` helpers (opening a fresh gRPC channel each time). At production photo volume this becomes hundreds of sequential round trips to PictureService and the page times out before it can render.

## What Changes

- Add a batched `GetPhotoDownloadUrls` RPC to `picture_service.proto`, taking a list of photo IDs and returning a pre-signed S3 download URL for each, so a page load needs one round trip instead of one per photo.
- Implement the batch RPC in PictureService (`PictureScannerGrpcService`, alongside the existing `GetPhotoDownloadUrl`), generating pre-signed URLs for the requested photo IDs.
- Update `PictureServiceClient.GetCardsAsync` (and its private helpers `ToCardListItemsAsync`/`ToCardListItemAsync`) in `NinjagoScanner.Web` - which `CollectionQueryService.GetReviewGroupsAsync` calls - to resolve all photo download URLs for a page load with a single batched call instead of per-photo sequential calls.
- Keep the existing single-photo `GetPhotoDownloadUrl` RPC for call sites that only ever need one or a handful of URLs (e.g. `CollectionQueryService.BuildCardPhotosAsync` for the collection detail view and `GetGalleryCardsAsync` for the gallery, both already scoped to a small, bounded number of photos).

## Capabilities

### New Capabilities
- `picture-service-photo-download`: PictureService's contract for resolving one or many photo IDs to short-lived pre-signed S3 download URLs, including the new batched RPC and its behavior for empty/partial/missing photo IDs.

### Modified Capabilities
- `web-card-review-flow`: the review page's photo list SHALL be built without issuing a separate download-URL request per photo, so page load time does not scale linearly with the number of photos being reviewed.

## Impact

- `NinjagoScanner.PictureService/Protos/picture_service.proto` and `NinjagoScanner.Web/Protos/picture_service.proto` (kept in sync — see existing convention of duplicated `.proto` files per project) — new RPC and messages, non-breaking (additive).
- `NinjagoScanner.PictureService/Services/PictureScannerGrpcService.cs` — new RPC handler alongside the existing `GetPhotoDownloadUrl`.
- `NinjagoScanner.Web/Services/PictureServiceClient.cs` — `GetCardsAsync`/`ToCardListItemsAsync` switch from N sequential `GetDownloadUrlAsync` calls to one batched call; `CollectionQueryService.GetReviewGroupsAsync` (`NinjagoScanner.Web/Services/CollectionQueryService.cs`) benefits with no code change of its own since it consumes `GetCardsAsync` as-is.
- `NinjagoScanner.Web.Tests/Fixtures/PictureServiceTestHost.cs` maps the whole `PictureScannerGrpcService` class, so no fixture wiring is needed for the new RPC to become callable in-process - only the handler implementation is required.
