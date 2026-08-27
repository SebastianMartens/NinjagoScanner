## Why

The review page is still slow at production photo counts even after the recent batched-download-URL change. Grafana shows `GetPhotoDownloadUrls` as the slow route. Tracing the code shows why: that batch RPC still awaits an S3 existence check per photo, sequentially, inside the one gRPC call — so the round trips didn't disappear, they moved from the Web↔PictureService hop into the PictureService↔S3 hop, hidden inside what looks like a single fast call from the outside. `ListCards` already walks the same S3 bucket to discover photo IDs in the first place, so that existence check is redundant work paid for on every review page load. `ListCards` has an analogous problem of its own: it reads each photo's sidecar with one DynamoDB point-read per photo instead of the bulk scan the codebase already has for this purpose.

## What Changes

- `ListCards` resolves a signed download URL for every `CardEntry` it returns, built from two bulk reads (the S3 listing it already performs, plus a bulk sidecar scan) joined in memory, instead of the review page needing a second call for download URLs at all.
- `ListCards` switches from one DynamoDB point-read per photo to the existing bulk `ListAllAsync` scan for sidecar data, removing another source of O(photos) sequential round trips.
- **BREAKING**: The batched `GetPhotoDownloadUrls` RPC (added in `2026-08-25-batch-photo-download-urls`) is removed, along with its Web-side client plumbing (`PictureServiceClient.GetDownloadUrlsAsync`), since every caller that used it now gets the download URL directly from `ListCards`. The singular `GetPhotoDownloadUrl` RPC (used by the upload flow for a single freshly-uploaded photo, outside of any `ListCards` call) is unchanged.
- `CollectionQueryService.GetGalleryCardsAsync` and `BuildCardPhotosAsync` (collection detail view) read the download URL already present on each `CardEntry` instead of making a follow-up per-photo `GetPhotoDownloadUrl` call.

## Capabilities

### Modified Capabilities
- `picture-service-card-listing`: `ListCards` SHALL include a working download URL on every returned `CardEntry`, and SHALL resolve sidecar data and photo existence via bulk reads rather than one round trip per photo.
- `picture-service-photo-download`: the batched `GetPhotoDownloadUrls` RPC and its "resolve many at once" contract are removed; batch resolution is no longer a capability PictureService needs to expose, since `ListCards` provides download URLs directly.
- `web-card-review-flow`: strengthens the existing "photo display URLs load without one request per photo" requirement — the review page now resolves every photo's display URL with *zero* additional requests to PictureService beyond the one call that lists the cards, not merely a bounded small number of requests.

## Impact

- `NinjagoScanner.PictureService/Protos/picture_service.proto` and the identical `NinjagoScanner.Web/Protos/picture_service.proto` copy (kept in sync per repo convention): add `download_url` to `CardEntry`; remove `GetPhotoDownloadUrls`, `GetPhotoDownloadUrlsRequest`, `GetPhotoDownloadUrlsResponse`, and the RPC declaration. **BREAKING** at the gRPC contract level for any other caller of the removed RPC — none exist in this repo today (verified: `GetDownloadUrlsAsync`/`ToCardListItemsAsync` are its only callers, both being removed in this same change).
- `NinjagoScanner.PictureService/Services/PictureScannerGrpcService.cs`: `ListCards` reworked to join the S3 listing with a bulk sidecar scan and sign a URL per entry; `GetPhotoDownloadUrls` handler removed.
- `NinjagoScanner.Web/Services/PictureServiceClient.cs`: `GetDownloadUrlsAsync` and the `ToCardListItemsAsync` fan-out removed; `GetCardsAsync` builds `CardListItem`s directly from `CardEntry.DownloadUrl`.
- `NinjagoScanner.Web/Services/CollectionQueryService.cs`: `GetGalleryCardsAsync` and `BuildCardPhotosAsync` read `entry.DownloadUrl` instead of calling `PictureServiceClient.GetDownloadUrlAsync` per matched photo.
- Deployment ordering unchanged from the prior batch-URL change: PictureService must deploy (with the new `CardEntry.download_url` field and without the removed RPC) before Web, since Web's updated code depends on that field existing and no longer calls the removed RPC.
