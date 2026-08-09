## 1. PictureService models and constants

- [x] 1.1 Add a `Languages` static constants class to `NinjagoScanner.PictureService/ScannerModels.cs` (mirroring `AnalysisStatuses`/`ReviewStatuses`): `German = "de"`, `English = "en"`, `Unknown = "unknown"`, `Default = German`.
- [x] 1.2 Add a nullable `Language` string property to `CardAnalysisResult` in `ScannerModels.cs`.
- [x] 1.3 Add a nullable `Language` string property to `GeminiCardPayload` in `ScannerModels.cs`.
- [x] 1.4 Add a nullable `Language` string property to `SidecarRecord` in `NinjagoScanner.PictureService/SidecarStore.cs`.

## 2. Gemini analysis pipeline

- [x] 2.1 Extend the JSON schema block in the prompt built by `CreateGeminiRequest` (`NinjagoScanner.PictureService/GeminiApiService.cs`) with a `"language": "de|en|unknown"` field, and add an instruction telling the model to determine it from the printed text/character names on the card.
- [x] 2.2 In `ParseSuccessResponse`, normalize `payload.Language` to the closed set (`de`/`en` case-insensitively, anything else or missing becomes `unknown`) and set it on the constructed `CardAnalysisResult`. Do not apply the German default here — this path only records what was actually detected (per specs/picture-service-gemini-analysis).
- [x] 2.3 Verify `CreateFailureResult` leaves `Language` unset (consistent with how `CardName`/`CardNumber` are left unset on a hard failure).

## 3. Sidecar reporting and editing (picture-service-card-listing, picture-service-sidecar-editing)

- [x] 3.1 In `PictureScannerGrpcService.ToCardEntry`, resolve `Language` as `sidecar?.Language ?? Languages.Default`, following the existing `ReviewStatus`/`AnalysisStatus` coalescing idiom. Confirm this single choke point covers all three cases: no sidecar file, a sidecar missing the field, and an unreadable sidecar.
- [x] 3.2 Confirm `UpdateSidecar`'s field-merge logic includes `Language` among the overwritten fields (alongside card name/number/set name/rarity/etc.), including the existing blank-string-to-null normalization.
- [x] 3.3 Confirm `UpdateSetName` and `UpdateReviewStatus` continue to leave `Language` untouched (single-field-only RPCs), matching their existing scoped-update behavior.

## 4. gRPC contract

- [x] 4.1 Add a `language` field (new field number) to the `CardEntry` message in `NinjagoScanner.PictureService/Protos/picture_service.proto`.
- [x] 4.2 Add a `language` field (new field number) to the `UpdateSidecarRequest` message in the same proto file.
- [x] 4.3 Apply the identical field additions to the proto copy at `NinjagoScanner.Web/Protos/picture_service.proto`, keeping field numbers in sync between both copies.
- [x] 4.4 Rebuild both projects to regenerate the gRPC client/server code from the updated `.proto` files.

## 5. Web Collection Overview

- [x] 5.1 Add a `Language` property to the Web-side models that carry sidecar data from `CardEntry` (`CollectionCardDetails.cs`'s `CollectionCardSidecarData`/`CollectionCardSidecarUpdate`), and map it from the gRPC response in `CardCatalogService.cs`. (`CardListItem.cs` was intentionally left untouched — it only feeds `CardsTable.razor`/`Review.razor`, neither in scope per the "detail/review pane only" decision and the `web-collection-overview`-only capability scope.)
- [x] 5.2 In `NinjagoScanner.Web/Components/Pages/Collection.razor`, add a Language control to the sidecar edit form: a picker over German/English/Unknown (not free text), pre-filled with the resolved value when a photo is selected.
- [x] 5.3 Include `Language` in the payload sent by the form's save action so it's persisted via `UpdateSidecar` alongside the other edited fields.
- [x] 5.4 Manually verify in the browser: open a card with no prior analysis and confirm the picker defaults to German; open a card with an existing sidecar lacking `Language` and confirm it also shows German; change the value and save, then reload and confirm it persisted. Verified via Playwright against the running app (all three services) on a real legacy sidecar (Serie 6 Next Level #44) with no `Language` field: picker defaulted to "Deutsch" (de), changed to "Englisch" (en) and saved successfully, reload confirmed the persisted value, and the on-disk sidecar JSON showed `"Language": "en"`. Test edit was reverted afterward to restore the original file.

## 6. Documentation

- [x] 6.1 Add a "Language" term to `openspec/GLOSSARY.md` under "Photo & Scanning Pipeline", cross-referencing **Sidecar** and noting the default-to-German behavior for cards with no explicit value.
