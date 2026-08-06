## 1. Proto contract

- [ ] 1.1 In `NinjagoScanner.PictureService/Protos/picture_service.proto`, rename `CardEntry.status` (field 2) to `analysis_status` and `UpdateSidecarRequest.status` (field 3) to `analysis_status`, keeping the existing field numbers.
- [ ] 1.2 In the same file, add `string review_status` to `CardEntry` and to `UpdateSidecarRequest`, each with a fresh, unused field number.
- [ ] 1.3 Apply the identical renames/additions to `NinjagoScanner.Web/Protos/picture_service.proto` so both copies stay in lockstep.
- [ ] 1.4 Rebuild both projects to regenerate the gRPC/protobuf C# code and confirm it compiles.

## 2. PictureService models and analysis logic

- [ ] 2.1 In `ScannerModels.cs`, rename `CardAnalysisResult.Status` to `AnalysisStatus`, and add a new `ReviewStatuses` constants class (`Unreviewed`, `Verified`, `Incorrect`) plus a `ReviewStatus` property on `CardAnalysisResult` defaulting to `ReviewStatuses.Unreviewed`. Do NOT rename `GeminiCardPayload.Status` or change the Gemini prompt text — that DTO models Gemini's own raw response schema and is a separate concern; `GeminiApiService.NormalizeStatus` continues to take `payload.Status` (Gemini's raw opinion) as input and produce the (now renamed) `AnalysisStatus` as output.
- [ ] 2.2 In `SidecarStore.cs`, rename `SidecarRecord.Status` to `AnalysisStatus` and add `SidecarRecord.ReviewStatus` (nullable string, no default-value assumptions since the type is a lenient reader). Make `ReadRecordAsync` backward-compatible: if the deserialized record's `AnalysisStatus` is null/empty but the raw JSON contains a legacy `status` key, populate `AnalysisStatus` from that legacy value in memory (do not rewrite the file here — that is `MigrateSidecars`' job).
- [ ] 2.3 In `GeminiApiService.cs`, update `NormalizeStatus` and all call sites/property assignments to use `AnalysisStatus` instead of `Status` (except the `payload.Status` input parameter, which stays as-is per 2.1); keep the derivation logic (including the `Confidence < 0.65` gate) unchanged.
- [ ] 2.4 In `Services/PictureScannerGrpcService.cs`, update all `Status = ...` literals/assignments to `AnalysisStatus`, and ensure freshly created sidecar records (new scans, and the `"pending"` placeholder for missing sidecars) set `ReviewStatus = ReviewStatuses.Unreviewed`.
- [ ] 2.5 Ensure `UpdateSidecar` handling persists an incoming `review_status` from the request into the sidecar record without altering it when the request does not intend to change it, and never derives it from `analysis_status` or `confidence`.

## 3. Sidecar migration RPC

- [ ] 3.1 In both `picture_service.proto` files, add `MigrateSidecarsRequest` (optional `card_photos_directory` override, same pattern as `ListCardsRequest`) and `MigrateSidecarsResponse` (e.g. `total_files`, `migrated`, `already_current`, `errors` counts) messages, and a `MigrateSidecars` RPC on `CardPictureService`.
- [ ] 3.2 Implement `PictureScannerGrpcService.MigrateSidecars`: resolve the directory via the same `ResolveDirectory` helper used by other RPCs, enumerate sidecar `*.json` files, for each one detect a legacy `status` key with no `AnalysisStatus` key, rewrite it via `SidecarStore.WriteRecordAsync` using the migrated record, and skip files already on the new key. Return counts in the response.
- [ ] 3.3 Verify idempotency: running the RPC twice in a row against the same directory produces zero additional rewrites on the second run.

## 4. Web mapping and models

- [ ] 4.1 In `NinjagoScanner.Web/Services/CardCatalogService.cs`, update all proto <-> model mapping (`ToCardListItem`, `ToCollectionSidecar`, and the `UpdateSidecar` request builder) to read/write `AnalysisStatus` and `ReviewStatus` instead of `Status`.
- [ ] 4.2 In `Models/CardListItem.cs`, rename `Status` to `AnalysisStatus` and add a `ReviewStatus` property.
- [ ] 4.3 In `Models/CollectionCardDetails.cs`, rename `Status` to `AnalysisStatus` and add `ReviewStatus` on both `CollectionCardSidecarData` and `CollectionCardSidecarUpdate`.

## 5. Collection edit form (Web UI)

- [ ] 5.1 In `Components/Pages/Collection.razor`, replace the editable `<input @bind="sidecarDraft.Status" />` with a read-only display of `AnalysisStatus` (e.g. plain text next to the existing "Zuletzt gescannt" info), removing it from the fields that get submitted for edit.
- [ ] 5.2 Add a `ReviewStatus` `<select>` control to the edit form grid with the three fixed options (`unreviewed`, `verified`, `incorrect`), bound to `sidecarDraft.ReviewStatus`.
- [ ] 5.3 Update `SidecarDraft`, `BuildDraft`, and `SaveSelectedSidecarAsync` so saving the form sends the current `ReviewStatus` value and does not send/alter `AnalysisStatus`.
- [ ] 5.4 Verify manually: editing and saving any other field (name, number, set, rarity, etc.) without touching the review control leaves `ReviewStatus` unchanged.

## 6. List views and filtering (Web UI)

- [ ] 6.1 In `Components/Pages/Home.razor`, rename all `card.Status`/`Status` references (badges, grouping, existing filter, `GetStatusLabel`/`GetStatusClass`) to use `AnalysisStatus`.
- [ ] 6.2 In `Components/Pages/Home.razor`, add a second, independent filter for `ReviewStatus` (`selectedReviewStatus`, `AvailableReviewStatuses`, label/class helpers), following the same pattern as the `AnalysisStatus` filter.
- [ ] 6.3 Apply the equivalent renames and new `ReviewStatus` filter to `Components/Pages/CardsTable.razor`.

## 7. Verification

- [ ] 7.1 Build the full solution (`NinjagoScanner.slnx`) and confirm no remaining references to the old `Status` field name anywhere in PictureService or Web source, outside of `GeminiCardPayload`.
- [ ] 7.2 Run a manual end-to-end check: scan a new photo (confirm `AnalysisStatus` populates and `ReviewStatus` defaults to `unreviewed`), mark it `verified` via the UI, filter the list by `verified`, then edit an unrelated field and confirm `ReviewStatus` stays `verified`.
- [ ] 7.3 Confirm a sidecar file with the old `status` JSON key is read correctly into `AnalysisStatus` without a rescan (backward-compatible read from task 2.2).
- [ ] 7.4 Run `MigrateSidecars` against a directory containing such a legacy file; confirm the file is rewritten to use the `AnalysisStatus` key with the same value it had, then run it again and confirm no further changes occur.
- [ ] 7.5 Confirm `GeminiCardPayload`/the prompt were left untouched by running a real scan and verifying `AnalysisStatus` is still classified correctly (`ok`/`uncertain`/`failed`) from Gemini's response.
