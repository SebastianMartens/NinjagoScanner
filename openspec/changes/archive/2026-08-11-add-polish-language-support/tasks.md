## 1. Add Polish to the recognized language set

- [x] 1.1 Add `Polish = "pl"` to `Languages` in `NinjagoScanner.PictureService/ScannerModels.cs`
- [x] 1.2 Add `Polish = "pl"` to `Languages` in `NinjagoScanner.Web/Models/CardListItem.cs`
- [x] 1.3 Update the Gemini prompt text and JSON schema description in `NinjagoScanner.PictureService/GeminiApiService.cs` to include `pl` alongside `de`/`en`/`unknown`
- [x] 1.4 Update `GeminiApiService.NormalizeLanguage` to keep a case-insensitive `pl` match instead of collapsing it to `unknown`

## 2. Add the UpdateCardLanguage RPC

- [x] 2.1 Add `UpdateCardLanguage` RPC plus `UpdateCardLanguageRequest`/`UpdateCardLanguageResponse` messages to `NinjagoScanner.PictureService/Protos/picture_service.proto`, mirroring `UpdateCardNumber`'s shape
- [x] 2.2 Apply the identical proto addition to `NinjagoScanner.Web/Protos/picture_service.proto` so both copies stay byte-identical
- [x] 2.3 Implement the `UpdateCardLanguage` handler in `NinjagoScanner.PictureService/Services/PictureScannerGrpcService.cs`, mirroring `UpdateCardNumber`: create a pending sidecar if none exists, otherwise update only `Language` (normalized blank-to-null) via the sidecar cache
- [x] 2.4 Add `UpdateCardLanguageAsync` to `NinjagoScanner.Web/Services/CardCatalogService.cs`, mirroring `UpdateCardNumberAsync`

## 3. Surface Language on the Review page's data model

- [x] 3.1 Add a `Language` property to `NinjagoScanner.Web/Models/CardListItem.cs`
- [x] 3.2 Map `Language` in `CardCatalogService.ToCardListItem`, defaulting to `Languages.Default` (German) when absent, matching `ToCollectionSidecar`'s existing default

## 4. Add the language control to the Review page

- [x] 4.1 Add a language `<select>` (German/English/Polish/Unknown) to each photo tile in `NinjagoScanner.Web/Components/Pages/Review.razor`, pre-filled from that photo's `Language`
- [x] 4.2 Add a `SaveLanguageAsync` handler in the code-behind that calls `CardCatalogService.UpdateCardLanguageAsync` and reloads/resyncs the page state (reuse `RunPhotoActionAsync`, which is a superset of what's needed since language changes never move a photo between groups)
- [x] 4.3 Ensure the language control's displayed value is re-initialized from the reloaded photo data after any group-list reload, consistent with how the card-number control is resynced

## 5. Add Polish to the Collection page's language selector

- [x] 5.1 Add a Polish `<option>` to the Language `<select>` in `NinjagoScanner.Web/Components/Pages/Collection.razor`

## 6. Tests

- [x] 6.1 Add `PictureScannerGrpcServiceUpdateCardLanguageTests.cs` in `NinjagoScanner.PictureService.Tests/Services/`, mirroring `PictureScannerGrpcServiceUpdateCardNumberTests.cs`: creates a pending sidecar when none exists, updates only `Language` and leaves other fields untouched, blank input normalizes to absent
- [x] 6.2 Add/extend a `GeminiApiService` normalization test covering a `pl` (and mixed-case `PL`) model response resolving to `pl`

## 7. Manual verification

- [ ] 7.1 Run the app, scan or hand-edit a photo's sidecar to `pl`, and confirm it displays correctly on both the Collection and Review pages
- [ ] 7.2 On the Review page, change a photo's language via the new dropdown and confirm only that field changes, the photo stays in its current group, and the value persists after reload
