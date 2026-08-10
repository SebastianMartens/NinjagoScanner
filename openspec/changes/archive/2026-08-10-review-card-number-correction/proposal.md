## Why

Gemini's card-number detection is sometimes wrong, and today the only way to fix a photo's `CardNumber` is by editing the sidecar JSON file by hand. During review this misdetection is easy to spot (the photo lands in the wrong catalog group, or in the catch-all group), but there is no in-page way to correct it - mirroring the series-reassignment control that already exists for `SetName`.

## What Changes

- Add an `UpdateCardNumber` RPC to `CardPictureService`, mirroring `UpdateSetName`: it updates only a photo's `CardNumber` field, creating a pending sidecar record first if none exists yet.
- Add an inline edit control to each photo tile on the review page (`Review.razor`) that lets the user type a corrected card number and save it, alongside the existing series-reassignment buttons.
- Saving a corrected card number re-evaluates the photo's group membership the same way a series reassignment does today (the group list is reloaded, and the page follows the photo's current group if it changed).

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `picture-service-sidecar-editing`: adds the `UpdateCardNumber` RPC (analogous to the existing `UpdateSetName` RPC), which updates only a sidecar's `CardNumber` field.
- `web-card-review-flow`: adds a per-photo card-number correction control to the review page, alongside the existing series-reassignment control.

## Impact

- `NinjagoScanner.PictureService/Protos/picture_service.proto` (and its mirrored copy under `NinjagoScanner.Web/Protos/`): new `UpdateCardNumber` RPC and request/response messages.
- `NinjagoScanner.PictureService/Services/PictureScannerGrpcService.cs`: new `UpdateCardNumber` handler.
- `NinjagoScanner.Web/Services/CardCatalogService.cs`: new `UpdateCardNumberAsync` client method.
- `NinjagoScanner.Web/Components/Pages/Review.razor`: new inline card-number edit control per photo tile.
- Test projects covering the above (`NinjagoScanner.PictureService.Tests`, `NinjagoScanner.Web.Tests`).
