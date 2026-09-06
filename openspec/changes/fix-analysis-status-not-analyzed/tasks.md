## 1. PictureService: normalization fallback and fix the no-sidecar bug

- [x] 1.1 Add `NotAnalyzed = "notAnalyzed"` to the internal `AnalysisStatuses` class in `NinjagoScanner.PictureService/ScannerModels.cs`, alongside `Ok`/`Uncertain`/`Failed`.
- [x] 1.2 Add a private `NormalizeAnalysisStatus(string? status)` helper in `PictureScannerGrpcService.cs` that returns the value unchanged if it case-insensitively matches `AnalysisStatuses.Ok`/`Uncertain`/`Failed`, and `AnalysisStatuses.NotAnalyzed` otherwise (covers `null`, blank, and any unrecognized/legacy value such as `pending`).
- [x] 1.3 Use that helper in `ToCardEntry` for `AnalysisStatus = NormalizeAnalysisStatus(sidecar?.AnalysisStatus)`, replacing the raw `"unknown"` no-sidecar default. Verify `PictureScannerGrpcServiceGetPhotoDownloadUrlTests.cs:85` (currently asserting `"unknown"`) is updated to assert `"notAnalyzed"` and passes.
- [x] 1.4 In `UpdateSetName`, `UpdateCardNumber`, `UpdateCardLanguage`, and `UpdateReviewStatus`, replace `new SidecarRecord { AnalysisStatus = "pending" }` with a bare `new SidecarRecord()` (no explicit status — it will read back as `notAnalyzed` via the normalization helper). Update the corresponding tests (`PictureScannerGrpcServiceUpdateSetNameTests.cs`, `PictureScannerGrpcServiceUpdateCardNumberTests.cs`, `PictureScannerGrpcServiceUpdateCardLanguageTests.cs`, `PictureScannerGrpcServiceUpdateReviewStatusTests.cs`) to assert `record.AnalysisStatus` is `null` (the stored value) and/or that the entry read back via `ToCardEntry`/`ListCards` reports `notAnalyzed`, and verify `dotnet test NinjagoScanner.PictureService.Tests --filter "FullyQualifiedName~UpdateSetName|FullyQualifiedName~UpdateCardNumber|FullyQualifiedName~UpdateCardLanguage|FullyQualifiedName~UpdateReviewStatus"` passes.
- [x] 1.5 Update `PictureScannerGrpcServiceSidecarCacheConsistencyTests.cs` (lines using `AnalysisStatus = "pending"`) to use `"notAnalyzed"` (as a stored value being tampered in directly, this stays an explicit literal in the test setup, not a code call site) and verify the test file still passes.
- [x] 1.6 Add a test asserting that a sidecar with a legacy stored `AnalysisStatus` of `"pending"` (or any other unrecognized string) is reported as `notAnalyzed` via `ListCards`/`GetPhotoDownloadUrl`, confirming no data migration is needed.

## 2. Web: rename the constant and fix the filter/label

- [x] 2.1 Rename `AnalysisStatuses.Pending` to `AnalysisStatuses.NotAnalyzed` (value `"notAnalyzed"`) in `NinjagoScanner.Web/Models/Statuses.cs`.
- [x] 2.2 Update `Review.razor`'s filter `<option>` and `GetAnalysisStatusLabel` switch to reference `AnalysisStatuses.NotAnalyzed`, and change its label from "Kein Sidecar" to a not-analyzed-worded label (e.g. "Nicht analysiert") to match the merged meaning (no-sidecar and manually-edited-but-unanalyzed are now the same status).
- [x] 2.3 Update `ReviewFilterTests.cs` and `ReviewDisplayCapTests.cs` (and any other reference to `AnalysisStatuses.Pending`) to use `AnalysisStatuses.NotAnalyzed`, and verify `dotnet test NinjagoScanner.Web.Tests --filter "FullyQualifiedName~Review"` passes.

## 3. Docs

- [x] 3.1 Update `openspec/GLOSSARY.md`'s Analysis Status entry to list `notAnalyzed` in place of `pending`.

## 4. Verify end to end

- [x] 4.1 Run `dotnet build NinjagoScanner.slnx` and `dotnet test NinjagoScanner.slnx` and confirm everything passes.
- [ ] 4.2 Manually verify in the running app (Web + PictureService + CatalogService): upload a photo, confirm it shows the not-analyzed label/filter before analysis completes, then confirm it moves to the correct status once Gemini analysis finishes.
