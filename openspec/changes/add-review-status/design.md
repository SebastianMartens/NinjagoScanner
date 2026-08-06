## Context

`Status` is currently a single string field that flows: `GeminiApiService.NormalizeStatus` → `CardAnalysisResult.Status` → sidecar `*.json` (`SidecarRecord.Status`) → gRPC `CardEntry.status` / `UpdateSidecarRequest.status` (defined identically in `NinjagoScanner.PictureService/Protos/picture_service.proto` and `NinjagoScanner.Web/Protos/picture_service.proto` — there is no shared proto project, each side has its own copy compiled independently) → `CardCatalogService` mapping → `CardListItem` / `CollectionCardSidecarData` / `CollectionCardSidecarUpdate` → Razor UI (`Home.razor`, `CardsTable.razor`, `Collection.razor`). The same field is also bound to a free-text `<input>` in `Collection.razor`'s sidecar edit form, making it directly human-editable today. See proposal.md - Why.

A second, unrelated JSON contract exists between `GeminiApiService` and the Gemini API itself: the prompt built in `CreateGeminiRequest` (`GeminiApiService.cs` ~line 58) literally asks Gemini to return a `"status"` key, which is deserialized into `GeminiCardPayload.Status`. That is Gemini's raw, self-reported opinion, consumed exactly once by `NormalizeStatus(payload.Status, payload.Confidence)` to produce the sidecar's actual status. It shares a name with the sidecar's `Status` field by coincidence, not by design, and is a different concept.

`ScannerConfig.ResolveCardPhotosDirectory` resolves a directory per-request (an optional override falling back to a configured default), and `Program.cs` is minimal (`AddGrpc()` / `MapGrpcService<PictureScannerGrpcService>()` / `Run()`) with no hosted-service or startup-hook scaffolding today.

## Goals / Non-Goals

**Goals:**
- Rename `Status` → `AnalysisStatus` consistently across every layer (JSON key, both proto files, C# models, UI).
- Add `ReviewStatus` (`unreviewed` / `verified` / `incorrect`) through every one of those same layers, editable only via its own explicit UI control.
- Keep `AnalysisStatus` values and derivation logic byte-for-byte identical to today's `Status` logic — this is a rename plus an addition, not a behavior change to analysis.
- Migrate existing sidecar files off the legacy `status` key via a dedicated, explicitly-invoked, idempotent operation — not a hidden side effect of normal reads, and not a startup hook.
- Keep existing sidecar files fully usable in the meantime: reading a not-yet-migrated file still surfaces the correct `AnalysisStatus`.

**Non-Goals:**
- No change to the Gemini API prompt or to `GeminiCardPayload` — that DTO models Gemini's own raw response schema (a separate wire contract keyed on the literal `"status"` string the prompt requests), not our domain concept, and is intentionally left untouched.
- No automatic/implicit migration as a side effect of reading a sidecar file (e.g. inside `ListCards`) — migration only happens when the dedicated `MigrateSidecars` operation is explicitly invoked.
- No visual redesign of status badges — `ReviewStatus = incorrect` intentionally reuses the same badge styling already used for `AnalysisStatus = failed`.
- No auto-derivation of `ReviewStatus` from `Confidence` or from edits to other fields.
- No extraction of the proto file into a shared library — both copies continue to be maintained in lockstep as they are today.

## Decisions

**Two proto field numbers, not one reused.** `CardEntry.status` (field 2) and `UpdateSidecarRequest.status` (field 3) get renamed in-place to `analysis_status`, keeping their existing field numbers (proto field identity is the number, not the name, so this is wire-compatible and not a breaking change at the protobuf level — only the JSON sidecar key and generated C# property name change). New `review_status` fields are appended with fresh field numbers at the end of each message, alongside a `review_status` field added to `ListCardsResponse`'s `CardEntry` (already covered by the same message) and to `UpdateSidecarRequest`. Both `.proto` files must be edited identically since there is no shared source.

**`AnalysisStatuses`-style constants for `ReviewStatuses` too.** Mirror the existing `internal static class AnalysisStatuses { Ok, Uncertain, Failed }` pattern in `ScannerModels.cs` with a new `ReviewStatuses { Unreviewed, Verified, Incorrect }` constants class, keeping lowercase string values consistent with the existing convention.

**Backward-compatible read for legacy sidecar files.** `SidecarStore.ReadRecordAsync` treats an absent `AnalysisStatus` alongside a present legacy `status` key as equivalent to `AnalysisStatus` being that legacy value, so already-scanned cards keep showing a correct status immediately, with no dependency on migration having run yet. This is a pure in-memory read behavior — it does not rewrite the file; that is the migration operation's job.

**Dedicated `MigrateSidecars` RPC, not a read-repair side effect or a startup hook.** Considered rewriting legacy files automatically the moment `SidecarStore.ReadRecordAsync` reads them (since `ListCards` already reads every file in a directory on every list request) — rejected in favor of an explicit, separate operation the user chooses when to run. Also considered a startup hook in `Program.cs` — rejected because `Program.cs` has no hosted-service scaffolding today and directory resolution is normally per-request. Landed on: a new gRPC RPC `MigrateSidecars(MigrateSidecarsRequest) returns (MigrateSidecarsResponse)`, mirroring the existing per-operation `card_photos_directory` override pattern used by `Scan`/`ListCards`/etc., so it can be triggered explicitly against any directory, is naturally idempotent (skips files that already have `AnalysisStatus`), and requires no new composition-root code.

**`GeminiCardPayload.Status` keeps its name.** The rename touches only `CardAnalysisResult`/`SidecarRecord` (our domain result) and everything downstream of them; `GeminiCardPayload` and the Gemini prompt text are untouched. The naming boundary sits exactly at `GeminiApiService.NormalizeStatus(payload.Status, payload.Confidence)`: its input (`payload.Status`, Gemini's raw opinion) and output (the resulting `AnalysisStatus`) are two different concepts that happen to look alike.

**`AnalysisStatus` becomes read-only in `Collection.razor`.** The current `<input @bind="sidecarDraft.Status" />` free-text box is removed from the editable form fields; `AnalysisStatus` is rendered as plain text/label instead (similar to how `ScannedAtUtc` is already shown as read-only info in that view). `ReviewStatus` gets its own `<select>` bound to `sidecarDraft.ReviewStatus` with the three fixed values, placed in the editable grid.

**Filtering follows the existing `selectedStatus`/`AvailableStatuses` pattern.** `Home.razor` and `CardsTable.razor` get a second, parallel filter (`selectedReviewStatus` / `AvailableReviewStatuses`) using the same `GetStatusLabel`/`GetStatusClass`-style helpers, rather than merging review and analysis status into one filter control — they are independent dimensions and should stay filterable independently.

## Risks / Trade-offs

- [`MigrateSidecars` is a manual step someone has to remember to run] → Mitigate by keeping the read path backward-compatible regardless (legacy files still show a correct `AnalysisStatus` even before migration runs), so forgetting to run it degrades to "file not yet rewritten," never to "data appears lost."
- [Rolling back the code after `MigrateSidecars` has already run] → Pre-change code only reads the legacy `status` key, so any file already rewritten to `AnalysisStatus` would appear status-less under rolled-back code. Mitigated by documenting this explicitly rather than building bidirectional-forever compatibility, which would defeat the point of renaming the field; if a rollback is ever needed after migration has run, sidecar files would need restoring from backup or re-scanning.
- [Two hand-maintained copies of `picture_service.proto` could drift, e.g. one gets `review_status` and the other doesn't] → Mitigate by editing both files in the same task/commit and relying on the gRPC client failing loudly (missing field / compile error) if they diverge; no new tooling introduced by this change.
- [Reusing the "failed" badge style for `incorrect` could still read as ambiguous at a glance] → Accepted for now per user decision; easy to revisit later since it's a CSS class change only, not a data model change.

## Migration Plan

1. Update both `picture_service.proto` files (rename `status`→`analysis_status` in place, add `review_status`, add the `MigrateSidecars` RPC + request/response messages), regenerate gRPC code for both projects.
2. Update PictureService models/logic: rename `Status`→`AnalysisStatus` in `CardAnalysisResult`/`SidecarRecord` only (not `GeminiCardPayload`), add the backward-compatible legacy-key read in `SidecarStore.ReadRecordAsync`, add `ReviewStatuses` constants, default `ReviewStatus = unreviewed` on new/updated records.
3. Implement `MigrateSidecars` in `PictureScannerGrpcService.cs`: resolve the directory the same way other RPCs do, walk sidecar files, rewrite any still on the legacy `status` key, skip files already migrated, return a summary.
4. Update Web mapping (`CardCatalogService.cs`) and models (`CardListItem`, `CollectionCardSidecarData`, `CollectionCardSidecarUpdate`).
5. Update UI: read-only `AnalysisStatus` display and new `ReviewStatus` control in `Collection.razor`; new review-status filter in `Home.razor` and `CardsTable.razor`.
6. After deployment, invoke `MigrateSidecars` once (e.g. via a gRPC client) against the real card photos directory to convert existing files; safe to re-run.

Rollback: revert the commits. If `MigrateSidecars` has not yet been run, rollback is a plain code revert with no data cleanup required (backward-compatible reads mean nothing depended on the new key yet). If it has already been run, see the rollback risk above.
