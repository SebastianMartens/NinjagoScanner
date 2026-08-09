## Context

`NinjagoScanner.PictureService` already models several detected-but-editable sidecar fields (`CardName`, `CardNumber`, `SetName`, `Rarity`) through the same pipeline: Gemini prompt/JSON schema → `GeminiCardPayload` → `CardAnalysisResult` (written on scan) → `SidecarRecord` (lenient read/merge type in `SidecarStore.cs`) → `CardEntry` proto (via `PictureScannerGrpcService.ToCardEntry`) → Web display/edit form. `ToCardEntry` is already the single choke point where every `SidecarRecord` — whether read from a real file, synthesized as `new SidecarRecord { AnalysisStatus = "pending" }` for an unscanned image, or missing a field entirely — is converted into the proto response, and it already applies field-level defaults there (e.g. `ReviewStatus = sidecar?.ReviewStatus ?? ReviewStatuses.Unreviewed`).

Separately, `SidecarStore.ReadRecordAsync` already has precedent for backfilling a field that's absent from older sidecar JSON without rewriting the file (`FindLegacyStatus`, for sidecars written before `AnalysisStatus` was renamed from `status`).

See proposal.md for motivation; see specs/ for the exact behavioral contract.

## Goals / Non-Goals

**Goals:**
- Reuse the existing detected-field pipeline and its established idioms (const classes like `AnalysisStatuses`/`ReviewStatuses`, `??`-default at the `ToCardEntry` boundary) rather than introducing a new pattern.
- Make the German default apply uniformly regardless of *why* a value is absent (no sidecar, sidecar predates the field, sidecar unreadable) by resolving it at the single existing choke point.
- Keep the change additive to on-disk data: no bulk rewrite/migration of existing sidecar files.

**Non-Goals:**
- Changing card identity, ownership counting, or the catalog data model (see proposal.md Non-goals).
- Building a generic "variant" abstraction beyond language.
- Validating or restricting what value the `UpdateSidecar` RPC itself will accept for `Language` beyond existing text-field normalization (blank → absent); the Web UI enforces the closed set via a picker control, consistent with how `SetName` is UI-constrained to known series while the RPC field itself remains a plain string.

## Decisions

**Default resolution happens at `ToCardEntry`, not at `SidecarStore.ReadRecordAsync`.** `ReadRecordAsync` is only reached when a sidecar file exists; `ToCardEntry` is reached for every image regardless of whether a file exists at all (see the `new SidecarRecord { AnalysisStatus = "pending" }` branches in `PictureScannerGrpcService.cs`). Resolving the default in one place, at the point where every code path already converges, avoids duplicating the `?? Languages.Default` logic across multiple call sites and matches how `ReviewStatus`'s default is already handled there.

**`Language` is stored as an explicit nullable string on `SidecarRecord`/`CardAnalysisResult`/`GeminiCardPayload`, not as an enum.** Every sibling field in this pipeline (`AnalysisStatus`, `ReviewStatus`, `Rarity`, `SetName`) is a plain string with a matching `*Statuses`-style constants class, not a C# enum — following that convention keeps JSON (de)serialization, gRPC mapping, and blank-normalization behavior consistent with the rest of the file instead of introducing a one-off type.

**Absence and explicit `unknown` are kept distinct.** Absence (no field, no sidecar, unreadable sidecar) resolves to the default `de`. An explicit `unknown` — meaning a completed analysis genuinely could not tell — is passed through unchanged and is never coerced to `de`. Collapsing these two would erase a real (if inconclusive) signal from a Gemini analysis that did run.

**Gemini's language value is normalized to the closed set at parse time** (`ParseSuccessResponse` in `GeminiApiService.cs`), the same place `NormalizeStatus` already normalizes the analysis status — any value other than `de`/`en` (case-insensitive) becomes `unknown` rather than being stored verbatim, keeping the field closed-set end-to-end even though the model's raw output is freeform text.

**No change to `SeriesCatalogService`'s series-matching evidence.** Adding `Language` doesn't touch how a photo's series is resolved; `KnownCardNames`-based matching remains unaffected. (The proposal's discussion flagged that non-German scans might already see lower match confidence today — that's a pre-existing, separate concern, not something this change addresses.)

## Risks / Trade-offs

- **Risk:** A future reader of `SidecarRecord.Language == null` might assume "not analyzed" the way `CardName == null` does, and skip applying the German default. → Mitigation: centralizing the default at `ToCardEntry` (the only place that hands data to any external consumer) means nothing outside PictureService ever observes a raw `null`; internal call sites that need the resolved value should go through the same helper rather than reading `SidecarRecord.Language` directly.
- **Risk:** Existing sidecars never get an explicit `Language` written unless a person edits them or they're re-scanned, so `de` count is inflated by "never checked" cards, not just genuinely German ones. → Accepted trade-off per proposal: re-analyzing the whole existing collection is explicitly out of scope; the default is a practical assumption (most existing scans are in fact German), not a guarantee.

## Migration Plan

No data migration. Existing sidecar JSON files are left as-is; the default is applied purely at read time. Deploying the new proto field is additive (new field number) and backward compatible with any client that hasn't been rebuilt yet (it simply won't read the new field).
