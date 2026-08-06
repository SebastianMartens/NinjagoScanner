## Why

The sidecar `Status` field is Gemini's own self-assessment of a scan ("ok" / "uncertain" / "failed" / "pending"), but it is also the only field a human can edit in the Collection UI today. There is no way to record that a person actually looked at a card photo and confirmed or corrected the detected data — "manual validation" currently means silently overwriting the AI's own status. This change separates the two concepts: rename the existing field to `AnalysisStatus` (Gemini's read, display-only in the UI) and introduce a distinct `ReviewStatus` field that only changes via explicit human action, so manual validation has its own durable, filterable signal.

## What Changes

- **BREAKING**: Rename the sidecar/proto/UI field `Status` to `AnalysisStatus` everywhere it represents our own analysis result: sidecar JSON key, `CardEntry`/`UpdateSidecarRequest` proto fields (both copies of `picture_service.proto`), C# models (`CardAnalysisResult`, `SidecarRecord`, `CardListItem`, `CollectionCardSidecarData`, `CollectionCardSidecarUpdate`), and UI bindings/labels in `Home.razor`, `CardsTable.razor`, `Collection.razor`. Values and derivation logic (`ok`/`uncertain`/`failed`/`pending`, the `Confidence < 0.65` gate) are unchanged — only the name changes. `GeminiCardPayload.Status` (the DTO that deserializes Gemini's raw API response) and the Gemini prompt text are explicitly excluded — that is a separate wire contract with the Gemini API itself, keyed on the literal `"status"` string the prompt requests, unrelated to our own `AnalysisStatus` concept.
- Add a dedicated, explicitly-invoked `MigrateSidecars` RPC that rewrites existing sidecar files still using the legacy `status` key to the new `AnalysisStatus` key. It is idempotent (already-migrated files are left alone) and, like other RPCs, accepts an optional `card_photos_directory` override. Reading a not-yet-migrated file is backward-compatible regardless (see below), so migration can be run whenever convenient.
- `AnalysisStatus` becomes read-only in the Collection sidecar edit form (was previously a free-text editable input).
- Add a new `ReviewStatus` field to the sidecar schema, proto contract, and all corresponding C# models, with values `unreviewed` (default), `verified`, `incorrect`.
- `ReviewStatus` is editable in the Collection sidecar edit form via an explicit control (e.g. dropdown), independent of every other field in that form. Saving other field edits never changes `ReviewStatus`, and `Confidence`/`AnalysisStatus` never derive or gate it.
- `ReviewStatus` is filterable in the card list views (`Home.razor`, `CardsTable.razor`) the same way `AnalysisStatus` is today.
- `Confidence` behavior and display are unchanged — no new logic tied to it.

## Capabilities

### New Capabilities
- `picture-service-sidecar-review`: The sidecar JSON schema and gRPC contract (`CardEntry`, `UpdateSidecarRequest`) carry a Gemini-produced `AnalysisStatus` (renamed from `Status`, same values/logic) and an independent, human-set `ReviewStatus` (`unreviewed`/`verified`/`incorrect`) that only changes via explicit update, never derived from `Confidence` or `AnalysisStatus`. Includes backward-compatible reads and a dedicated migration RPC for sidecar files still on the legacy `status` key.
- `web-card-review`: The Web UI displays `AnalysisStatus` read-only, lets a user set `ReviewStatus` via an explicit control in the sidecar edit form without affecting other fields, and lets card list views be filtered by `ReviewStatus`.

### Modified Capabilities
(none — no existing specs describe this behavior yet)

## Impact

- **NinjagoScanner.PictureService**: `ScannerModels.cs` (`AnalysisStatuses`, `CardAnalysisResult`; `GeminiCardPayload` intentionally untouched), `SidecarStore.cs` (`SidecarRecord`, backward-compatible legacy read), `GeminiApiService.cs` (`NormalizeStatus`), `Services/PictureScannerGrpcService.cs` (status literals, default sidecar creation, new `MigrateSidecars` handler), `Protos/picture_service.proto` (new `MigrateSidecars` RPC + messages).
- **NinjagoScanner.Web**: `Protos/picture_service.proto` (kept in sync with the PictureService copy), `Services/CardCatalogService.cs` (proto <-> model mapping), `Models/CardListItem.cs`, `Models/CollectionCardDetails.cs` (`CollectionCardSidecarData`, `CollectionCardSidecarUpdate`), `Components/Pages/Home.razor`, `Components/Pages/CardsTable.razor`, `Components/Pages/Collection.razor`.
- **On-disk data**: existing sidecar `*.json` files with the legacy `status` key are read backward-compatibly (no rescan required) and are converted to the new `AnalysisStatus` key by explicitly invoking the new `MigrateSidecars` RPC (see design.md - Decisions). No cross-project shared library exists for the proto — both copies of `picture_service.proto` must be edited in lockstep.
