## 1. gRPC contract: DeletePhoto

- [x] 1.1 Add `DeletePhoto` RPC plus `DeletePhotoRequest` (`image_file_name`, optional `card_photos_directory`) and `DeletePhotoResponse` (`bool success`) messages to `NinjagoScanner.Web/Protos/picture_service.proto`.
- [x] 1.2 Mirror the exact same RPC/messages in `NinjagoScanner.PictureService/Protos/picture_service.proto`, keeping both files identical per the existing duplication convention.

## 2. PictureService: implement deletion

- [x] 2.1 Add a `Remove(string sidecarPath)` method to `SidecarCache` that normalizes the path with `Path.GetFullPath` (matching `SetAsync`/`GetAsync`) and `TryRemove`s it from the backing dictionary.
- [x] 2.2 Implement `PictureScannerGrpcService.DeletePhoto`: resolve the directory the same way other RPCs do (`ResolveDirectory`), resolve the image path and `SidecarStore.GetSidecarPath(imagePath)`, throw `RpcException(StatusCode.NotFound, ...)` if the image file does not exist, otherwise delete the image file, delete the sidecar file if it exists, call `sidecarCache.Remove(sidecarPath)`, and return `DeletePhotoResponse { Success = true }`.

## 3. Web: service layer

- [x] 3.1 Add `ImageFileName` and `ReviewStatus` (nullable string) to `NinjagoScanner.Web/Models/GalleryCardItem.cs`.
- [x] 3.2 In `CardCatalogService.GetGalleryCardsAsync`, populate the two new fields from `matchedPhoto` (mirroring how `Rarity` is already populated there).
- [x] 3.3 Add `CardCatalogService.DeletePhotoAsync(string imageFileName, CancellationToken)` that calls the new `DeletePhoto` RPC via `CardPictureService.CardPictureServiceClient`, following the existing method shape (`UpdateReviewStatusAsync` is the closest template).

## 4. Gallery: Fehlerhaft control

- [x] 4.1 In `Gallery.razor`, add a "Fehlerhaft" button to each non-puzzle photo tile (only when `card.ImageUrl is not null`), calling a new `MarkFehlerhaftAsync(GalleryCardItem card)` handler that invokes `CardCatalogService.UpdateReviewStatusAsync(card.ImageFileName, ReviewStatuses.Incorrect)` and reloads the current series' cards (`LoadSeriesCardsAsync`) so the tile's flagged state and the underlying data stay in sync.
- [x] 4.2 Give the flagged-state indicator its own tile modifier (`gallery-tile-flagged`, plus a `gallery-tile-flag-badge` overlay and disabled-button copy change) and add matching styles in `app.css` (no scoped `.razor.css` files exist in this project) consistent with the existing photo-count badge / tag chip visual language.
- [x] 4.3 (Superseded by the restructuring in design.md: the tile is no longer itself a `<button>`, so the Fehlerhaft button is a sibling of the lightbox-opening button, not nested inside it — no `stopPropagation` needed.) Puzzle tiles originally excluded the control entirely; superseded again by section 7 below, which gives them the control via the lightbox instead.

## 5. Review: delete control and confirmation dialog

- [x] 5.1 In `Review.razor`, add a delete button to each photo tile's action area, disabled while `IsBusy(photo)` like the other per-photo controls.
- [x] 5.2 Add dialog state (`private CardListItem? photoPendingDeletion;`) and a confirmation dialog block modeled on the Gallery lightbox's backdrop pattern (`@onclick` on the backdrop to cancel, `@onclick:stopPropagation` on the dialog body), showing the photo's file name and Confirm/Cancel actions.
- [x] 5.3 Wire the delete button to set `photoPendingDeletion = photo`; wire Cancel to clear it without side effects.
- [x] 5.4 Wire Confirm to call `CardCatalogService.DeletePhotoAsync(photo.ImageFileName)` through the existing `RunPhotoActionAsync` helper (already reloads `groups` and resolves the post-delete index by group key, falling back to a clamped index exactly like `ConfirmAllAsync`'s advance-past-the-current-group behavior), then clear `photoPendingDeletion`.
- [x] 5.5 Add styles for the delete button and confirmation dialog in `app.css`, consistent with the existing `review-btn`/`btn` classes and the Gallery lightbox dialog's visual treatment.

## 6. Validation

- [x] 6.1 Build the solution (`dotnet build`) to confirm the regenerated gRPC client/server code compiles across `NinjagoScanner.Web` and `NinjagoScanner.PictureService`.
- [x] 6.2 Verified via a combination of automated integration tests and a live-server smoke check (no browser-automation tool was available in this environment to drive real clicks, so this substitutes for, rather than fully replicates, an interactive click-through — disclosed here rather than claimed as full UI verification): added `PictureScannerGrpcServiceDeletePhotoTests` (real in-process gRPC calls: deletes image+sidecar, tolerates a missing sidecar, fails `NotFound` on a missing image without touching other files, evicts the sidecar cache) and `CardCatalogServiceDeletePhotoTests`/gallery field tests (real gRPC round-trip through `CardCatalogService`, confirming a deleted photo disappears from `GetCardsAsync`/`GetGalleryCardsAsync`). Separately, started all three real services against the actual `cardFotos` directory (~4855 real photos) and fetched `/gallery?series=Serie 6 Next Level` and `/review` live: confirmed 200 responses, no server-side exceptions, and the rendered HTML contains the new "Fehlerhaft" buttons on real photo tiles and "Löschen" buttons on real review photo tiles. Did not click Fehlerhaft/Löschen against the real data (would mutate/delete real user photos); confirmed `cardFotos` file count unchanged after the session (9710 files before and after).
- [x] 6.3 Run `openspec validate gallery-flag-fehlerhaft-review-delete --strict` and fix any reported issues. (Valid, no issues.)

## 7. Gallery: Fehlerhaft for puzzle tiles via the lightbox

- [x] 7.1 Make `Gallery.IsPuzzleCategory` `internal` (was `private`) so it can be reused as the lightbox's puzzle-detection check and unit tested directly, mirroring the `Review.GroupTitle` testable-static-method pattern.
- [x] 7.2 In the Gallery lightbox markup, capture `lightboxCard` into a local (`openCard`) to avoid a nullable-field-in-closure warning, and add a Fehlerhaft button visible only when `IsPuzzleCategory(openCard.Category)`, wired to the existing `MarkFehlerhaftAsync(openCard)`, disabled/relabeled when already flagged or in flight.
- [x] 7.3 Replace `MarkFehlerhaftAsync`'s call to `LoadSeriesCardsAsync()` with a new lighter `ReloadSeriesCardsAsync()` that refreshes `seriesCards` without the loading-spinner flicker and, if a lightbox is open, re-points `lightboxCard` at the refreshed card by `ImageFileName` (or closes it if the photo is gone) instead of unconditionally closing it.
- [x] 7.4 Add `app.css` styles for `.gallery-lightbox-fehlerhaft-btn`, matching the non-puzzle tile button and the lightbox's existing close-button styling.
- [x] 7.5 Add `GalleryIsPuzzleCategoryTests` (mirrors `ReviewGroupTitleTests`) covering puzzle and non-puzzle category names, including case-insensitivity.
- [x] 7.6 Update the `web-gallery-page` delta spec and `design.md` to reflect the lightbox-based approach in place of the earlier full puzzle exclusion.
- [x] 7.7 Rebuild and rerun the full test suite; confirm 0 warnings/errors.
