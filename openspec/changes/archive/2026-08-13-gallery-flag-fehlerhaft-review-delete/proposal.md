## Why

Reviewing scanned photos happens on the Review page, but wrong photo-to-card assignments are often *spotted* on the Gallery page, where photos are already grouped visually by catalog card. Today a user who notices a wrong photo there has no way to flag it without navigating to Review and searching for the same card. Separately, once a photo is confirmed useless (duplicate, blurry, wrong card entirely), there is no way to remove it — it stays in `cardFotos` forever, cluttering every card list and the catalog ownership counts.

## What Changes

- Gallery: each card tile that shows a matched photo gets a "Fehlerhaft" button that flags that photo's `ReviewStatus` as `incorrect` (reusing the existing review-status field and the existing "Fehlerhaft" label already used on the Review page) without leaving the Gallery page.
- Gallery: a tile whose matched photo is already flagged `incorrect` visually indicates that state on the tile itself.
- Review: each photo tile gets a "Löschen" (delete) button.
- Review: activating "Löschen" opens a confirmation dialog; only confirming proceeds, cancelling leaves the photo untouched.
- Review: confirming deletion permanently removes both the photo file and its sidecar file from disk, and the photo disappears from every card list (Review, Collection, Gallery, ownership counts) immediately.
- PictureService: new `DeletePhoto` RPC that deletes an image file and its sidecar file (if present) from `cardFotos` given an image file name, and evicts the deleted sidecar from the in-memory sidecar cache.

## Capabilities

### New Capabilities
- `picture-service-photo-deletion`: the `DeletePhoto` RPC that permanently removes a scanned photo and its sidecar file from disk.

### Modified Capabilities
- `web-gallery-page`: card tiles gain a "Fehlerhaft" control that sets the matched photo's `ReviewStatus` to `incorrect`, and reflect that status visually.
- `web-card-review-flow`: photo tiles gain a delete control gated by a confirmation dialog, and deleting a photo removes it from the group (and the group itself, if it was the last photo) with the same navigation continuity as other photo-removing actions.

## Impact

- `NinjagoScanner.Web/Protos/picture_service.proto` and `NinjagoScanner.PictureService/Protos/picture_service.proto`: add `DeletePhoto` RPC + request/response messages (kept in sync, per existing convention of duplicated proto files across the two projects).
- `NinjagoScanner.PictureService/Services/PictureScannerGrpcService.cs`: implement `DeletePhoto`.
- `NinjagoScanner.PictureService/SidecarCache.cs`: add cache eviction for a deleted sidecar path.
- `NinjagoScanner.Web/Services/CardCatalogService.cs`: add `DeletePhotoAsync`, expose the matched photo's `ImageFileName` from `GetGalleryCardsAsync` (needed so the Gallery button can target the right photo) and its `ReviewStatus` (needed for the tile's flagged-state indicator).
- `NinjagoScanner.Web/Models/GalleryCardItem.cs`: add `ImageFileName` and `ReviewStatus` fields.
- `NinjagoScanner.Web/Components/Pages/Gallery.razor` (+ `.razor.css` if present) and `NinjagoScanner.Web/Components/Pages/Review.razor`: new buttons, confirmation dialog, and post-action refresh/navigation handling.
