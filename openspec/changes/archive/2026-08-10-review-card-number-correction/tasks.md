## 1. gRPC contract

- [x] 1.1 Add `UpdateCardNumber` RPC plus `UpdateCardNumberRequest`/`UpdateCardNumberResponse` messages to `NinjagoScanner.PictureService/Protos/picture_service.proto`, mirroring `UpdateSetName`'s shape (`image_file_name`, optional `card_photos_directory`, `card_number`).
- [x] 1.2 Mirror the same proto changes into `NinjagoScanner.Web/Protos/picture_service.proto`.

## 2. PictureService

- [x] 2.1 Implement `UpdateCardNumber` in `PictureScannerGrpcService.cs`, mirroring `UpdateSetName`: create a `SidecarRecord` with `AnalysisStatus = "pending"` if none exists, then set only `CardNumber` (via `NormalizeNullable`) and persist through `sidecarCache`.
- [x] 2.2 Add tests for `UpdateCardNumber` in `NinjagoScanner.PictureService.Tests` covering: creating a pending record when none exists, updating only `CardNumber` on an existing record (other fields unchanged), and blank input normalizing to null - mirroring the existing `UpdateSetName`/`UpdateReviewStatus` test coverage.

## 3. Web client

- [x] 3.1 Add `UpdateCardNumberAsync(string imageFileName, string? cardNumber, CancellationToken)` to `NinjagoScanner.Web/Services/CardCatalogService.cs`, mirroring `UpdateSetNameAsync`.

## 4. Review page UI

- [x] 4.1 Add an inline card-number edit control (text input pre-filled with `photo.CardNumber`, plus an explicit save action) to each photo tile in `Review.razor`, next to the existing series-reassignment buttons.
- [x] 4.2 Wire the save action to call `CardCatalogService.UpdateCardNumberAsync`, then reload `groups` and reposition `currentIndex` using the same pattern `ReassignSeriesAsync` already uses.
- [x] 4.3 Ensure the control's displayed value is re-initialized from `photo.CardNumber` whenever `groups` is reloaded (so edits by other actions on the page, or the save itself, are reflected without a manual page refresh).

## 5. Verification

- [x] 5.1 Run `NinjagoScanner.PictureService.Tests` and `NinjagoScanner.Web.Tests` and confirm they pass.
- [x] 5.2 Manually verify in the running app: correct a card number on a photo tile, confirm the photo moves to the expected group (or the catch-all group, if the new number doesn't resolve), and confirm other fields (`ReviewStatus`, `SetName`) are unaffected.
