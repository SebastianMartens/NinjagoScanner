## Why

`ListCards` presigns an S3 download URL for every photo in a collection, on every call — measured at 4.62s of a ~9.8s total call against a real 10,354-photo collection. That cost is thrown away for almost all of it: the Review page only ever displays up to 18 photos (one group) at a time, and the Gallery page only ever displays at most one photo per catalog card within a single selected series — a small fraction of the collection either way. Presigning scales with total collection size, not with what's actually rendered.

This reverses part of `2026-08-27-inline-photo-download-urls`, which put `download_url` directly on every `CardEntry` after judging presigning "local and cheap" — true at the scale that motivated that change, not at today's. It's a different reason than the round-trip-count problem that change (and `2026-08-25-batch-photo-download-urls` before it) solved, and that solution stays solved here: exactly one bounded follow-up call per view, not one call per photo.

This is Lever A of a three-lever plan; its companion `picture-service-nonblocking-presign` (Lever C, already proposed separately) fixes presigning blocking other concurrent requests while it runs. The two are independent and deliberately not merged — this one reduces how much gets presigned per request; that one fixes what happens while it does.

## What Changes

- `ListCards`'s `CardEntry` **no longer includes a download URL**. **BREAKING**: presigning is removed entirely from `ListCards`'s per-photo loop.
- A new bounded RPC on `CardPictureService` resolves download URLs for a caller-supplied list of photo IDs within one collection — reviving the shape of the `GetPhotoDownloadUrls` batch RPC removed by `2026-08-27-inline-photo-download-urls`, this time to bound cost by what's displayed rather than to cut round trips (round trips stay at one per view, same as today). A requested photo ID with no stored photo is omitted from the response rather than failing the whole call.
- Review page: resolves download URLs only for the photos in the currently displayed group (≤18, the existing display cap) — on load, on navigation, and whenever the group's membership changes via a local edit — resolving only photos that don't already have one, so an already-shown photo's URL never changes (preserves the existing stability guarantee).
- Gallery page: resolves download URLs only for the one matched photo per catalog card within the currently selected series, re-resolving when the selected series changes. The Gallery page has exactly the same unscoped-fetch shape as Review does today (it also calls the unscoped photo listing and reads `download_url` off every entry) — fixing it is a first-class part of this change, not a follow-on.
- Collection detail view (per-card photo list): switches to the new bounded call for that one card's photos — already naturally small; changed because the field it reads today is going away, not because its own cost was a problem.
- Deployment ordering: PictureService deploys first (adds the new RPC, stops populating `CardEntry.download_url`); `NinjagoScanner.Web` deploys after (switches the three affected call sites to the new bounded call, stops reading the now-absent field).

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `picture-service-card-listing`: removes the "ListCards includes a ready-to-use download URL on every entry" requirement — `ListCards` no longer resolves download URLs at all.
- `picture-service-photo-download`: adds back a bounded/batch RPC for resolving many photo IDs' download URLs at once, scoped to a caller-supplied list.
- `web-card-review-flow`: changes "Photo display URLs load without one request per photo" from zero additional requests to at most one bounded follow-up request scoped to the displayed group's photos; the separate "Photo display URLs stay stable while the user works on the page" requirement is unchanged and must still hold.
- `web-gallery-page`: adds a new requirement that download URLs are resolved only for photos matched within the selected series, not the whole collection.

## Impact

- `NinjagoScanner.Web/Protos/picture_service.proto` (canonical copy) and `picture_service`'s copy: remove `CardEntry.download_url`; add the new bounded RPC and its request/response messages.
- `picture_service/src/picture_service/picture_scanner_service.py`: `ListCards` stops presigning; new RPC handler added.
- `NinjagoScanner.Web/Services/PictureServiceClient.cs`: new method wrapping the bounded RPC; `ToCardListItem`/`GetCardsAsync` stop reading a download URL from `CardEntry`.
- `NinjagoScanner.Web/Services/CollectionQueryService.cs`: `GetGalleryCardsAsync` and `BuildCardPhotosAsync` switch to the new bounded call, scoped as described above.
- `NinjagoScanner.Web/Services/ReviewSession.cs` and `NinjagoScanner.Web/Components/Pages/Review.razor`: resolve the displayed group's missing URLs on load, navigation, and after a local edit changes group membership.
- `NinjagoScanner.Web.Tests`: `PictureServiceTestHost` fixture needs the new RPC; tests for `ReviewSession`, `CollectionQueryService`, and `PictureServiceClient` updated for the two-phase URL loading.
- `picture_service/tests/`: `ListCards` tests updated to not expect `download_url`; new tests for the bounded RPC (including the omit-on-missing-photo behavior).
- Not in scope: reducing the DynamoDB/S3 cost of `ListCards`'s full-collection read, which stays O(N) regardless (grouping/matching needs every photo's `SetName`/`CardNumber`/status) — candidate future work, not part of this change.
