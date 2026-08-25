## Context

`CollectionQueryService.GetReviewGroupsAsync` (`NinjagoScanner.Web/Services/CollectionQueryService.cs`) calls `PictureServiceClient.GetCardsAsync` (`NinjagoScanner.Web/Services/PictureServiceClient.cs`), which calls its private `ToCardListItemsAsync`, which loops over every `CardEntry` from `ListCards` and awaits `ToCardListItemAsync` one at a time; that method calls `GetDownloadUrlAsync`, which opens a new `GrpcChannel` and makes one `GetPhotoDownloadUrl` call per photo. At production photo counts this is hundreds of sequential gRPC round trips (plus per-call channel setup) before the page can render, and it times out. See [proposal.md](proposal.md) for the full motivation.

PictureService already generates pre-signed S3 GET URLs one at a time in its `GetPhotoDownloadUrl` handler (`PhotoStore`-backed). `CollectionQueryService.BuildCardPhotosAsync` (used by the collection detail view) and `GetGalleryCardsAsync` (used by the gallery) have the same per-photo `GetDownloadUrlAsync` call but are each scoped to a small, bounded set of photos (one card's photos, or one matched photo per gallery card), so neither is part of this fix.

## Goals / Non-Goals

**Goals:**
- Cut the review page's PictureService round trips for download URLs from O(photos) to O(1) per page load.
- Keep the fix additive to the gRPC contract (no breaking change to `GetPhotoDownloadUrl` or existing callers).

**Non-Goals:**
- Changing `CollectionQueryService.BuildCardPhotosAsync`/the collection detail view, or `GetGalleryCardsAsync`/the gallery (both already scoped to a small, bounded number of photos, not a hot path).
- Caching or otherwise avoiding repeated calls to `ListCards` itself - only the download-URL fan-out is addressed.
- Changing pre-signed URL expiry or S3 access patterns beyond generating more of them per call.

## Decisions

- **Add `GetPhotoDownloadUrls` as a new batch RPC**, taking a repeated list of photo IDs and returning a map/list of `{photo_id, download_url}` pairs, rather than only parallelizing the existing per-photo calls client-side. A batch RPC collapses the network round trips themselves (the dominant cost at hundreds of photos), whereas client-side parallelization alone would still make hundreds of concurrent gRPC calls and shift load rather than remove it. This was confirmed with the user as the preferred approach over a Web-only parallelization fix.
- **Keep `GetPhotoDownloadUrl` (singular) unchanged** for existing single-photo/small-batch callers (`CollectionQueryService.GetGalleryCardsAsync`, `BuildCardPhotosAsync`) - no need to migrate every caller to the batch shape.
- **PictureService generates the pre-signed URLs for a batch sequentially in-process** (simple loop over the requested IDs calling the existing S3 pre-sign logic). Pre-signing is a local, CPU-bound, non-blocking-I/O operation (no network call to S3 to produce a pre-signed URL), so parallelizing it server-side wouldn't meaningfully reduce latency and would add complexity for no benefit.
- **Unknown/missing photo IDs are silently omitted from the response** rather than causing the whole batch to fail, since a photo can be deleted between `ListCards` and the download-URL fetch (see spec's "Unknown photo IDs do not fail the whole batch"). `PictureServiceClient` on the Web side simply won't have a URL for that photo ID and skips it when building the list.
- **`PictureServiceClient.ToCardListItemsAsync` collects all photo IDs first, calls the batch RPC once, then builds `CardListItem`s from the returned map** - replacing the current per-item `await GetDownloadUrlAsync(...)` inside the loop. `CollectionQueryService.GetReviewGroupsAsync` needs no change of its own since it consumes `PictureServiceClient.GetCardsAsync` as-is.

## Risks / Trade-offs

- [A very large photo collection could still make one oversized request slow] → Out of scope for this change; current production volume is in the hundreds, not tens of thousands. If it becomes a problem later, the batch RPC can be chunked without changing its contract.
- [Both `.proto` copies (`NinjagoScanner.PictureService/Protos/picture_service.proto` and `NinjagoScanner.Web/Protos/picture_service.proto`) must be kept in sync manually, matching the existing repo convention] → Update both in the same change; covered explicitly in tasks.md.
- [In-process tests need the new RPC to be exercisable] → `PictureServiceTestHost.cs` maps the whole `PictureScannerGrpcService` class via `MapGrpcService<PictureScannerGrpcService>()`, so implementing the handler (task 2.1) is sufficient - no separate fixture wiring is needed. Existing tests that only use `GetPhotoDownloadUrl` are unaffected since that RPC is unchanged.

## Migration Plan

Additive-only change: new RPC and message types, existing RPCs untouched. PictureService must be deployed with the new RPC before Web is deployed with the code that calls it - if Web ships first, the review page's batched call fails against an old PictureService that doesn't implement it yet. Since Fly.io deploys are per-service (see `infra/README.md`), deploy PictureService first, confirm it's healthy, then deploy Web. No data migration; rollback is redeploying the previous image of whichever service regressed.
