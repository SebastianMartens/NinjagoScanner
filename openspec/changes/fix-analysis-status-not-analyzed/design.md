## Context

`AnalysisStatus` is a free-form `string` on `SidecarRecord`/`CardEntry` (proto3 `string`, no enum), so renaming its `pending` value is a value-convention change, not a schema change — see proposal.md for the motivating bug (`ToCardEntry` returning `unknown` for a missing sidecar, which `Review.razor` then mislabels as an error) and the duplication it's tangled up with (the literal `"pending"` hardcoded at four call sites in `PictureScannerGrpcService.cs`, with no matching constant in PictureService's own internal `AnalysisStatuses` class).

Two independent projects each keep their own copy of an `AnalysisStatuses` class today: PictureService's internal one (`ScannerModels.cs`, currently `Ok`/`Uncertain`/`Failed` only) and Web's public one (`Models/Statuses.cs`, currently includes `Pending`). They're separate types by design (no shared library between the gRPC services), so both need the new constant.

Explicit product decision: no data migration. Already-stored DynamoDB sidecar records that contain the literal `"pending"` (written only by the four manual-edit fallback paths — never by Gemini analysis, which only ever writes `ok`/`uncertain`/`failed`) are left untouched. Instead, `notAnalyzed` becomes a read-time fallback: PictureService reports it for any `AnalysisStatus` that isn't recognized as `ok`/`uncertain`/`failed`, whatever the underlying stored value (missing entirely, unset field, legacy `pending`, or anything else). This also means the four fallback paths no longer need to write any explicit status at all when creating a sidecar on the fly.

## Goals / Non-Goals

**Goals:**
- Make a missing sidecar and a create-if-missing manual edit both report the same, correctly-labeled not-analyzed status end to end (PictureService → Web → UI).
- Give PictureService its own named constant for this status instead of a raw string duplicated at four call sites.
- Make `notAnalyzed` a durable fallback so any past or future non-`ok`/`uncertain`/`failed` value reads correctly, without ever needing a data migration.

**Non-Goals:**
- No proto/gRPC contract change — `AnalysisStatus` stays a plain string.
- No change to when Gemini analysis runs or what it writes (`ok`/`uncertain`/`failed` are unaffected).
- No general redesign of the Review page's filter/label mechanism beyond swapping the renamed value in.

## Decisions

- **Rename the value itself (`pending` → `notAnalyzed`) rather than keep `pending` and add a second, different status for the no-sidecar case.** The proposal treats "no sidecar at all" and "sidecar exists only because of a manual edit, never analyzed" as the same user-facing concept — neither has been through Gemini — so one status value is correct; introducing a second one would just recreate today's bug in a different shape.
- **Normalize at read time in `ToCardEntry` instead of migrating stored data.** A single helper — "recognized as `ok`/`uncertain`/`failed` (case-insensitive) → report as-is; anything else → `notAnalyzed`" — replaces both the buggy `unknown` default and the `pending` literal, and permanently covers legacy `pending` records with no backfill step. Alternative considered: extend the existing `MigrateSidecars` safety-net RPC to rewrite stored `pending` values — rejected per explicit product direction: no existing data gets touched, and a read-time fallback is simpler than a stored value that must be kept in sync with what the fallback would produce anyway.
- **Stop writing an explicit analysis status in the four manual-edit fallback paths.** Since any missing/unrecognized value now reads as `notAnalyzed`, `UpdateSetName`/`UpdateCardNumber`/`UpdateCardLanguage`/`UpdateReviewStatus` can create a bare `new SidecarRecord()` (its `AnalysisStatus` stays `null`) instead of setting a literal that only exists to be read back the same way. This removes the four-call-site duplication called out in the proposal without needing a shared "creation" constant at all.
- **Add `NotAnalyzed` to PictureService's own internal `AnalysisStatuses` class rather than sharing Web's.** The two services don't share a library; extending PictureService's existing enum-of-constants class (used by the new normalization helper) is consistent with how `Ok`/`Uncertain`/`Failed` are already avoided as raw strings elsewhere in that file.
- **Rename the Review page's filter label from "Kein Sidecar" to a not-analyzed-worded label** (e.g. "Nicht analysiert") to match the merged meaning, since "no sidecar" is no longer an accurate description once manually-edited-but-unanalyzed sidecars share the same status.
- **Fix `ShouldSkipExistingSidecar` to check for `ok`/`uncertain` instead of excluding `failed`.** Its own doc comment already stated the intended rule ("skipped only when it already has a sidecar recording a completed analysis (`ok` or `uncertain`)"), and `picture-service-photo-scan`'s spec already requires the same — the implementation just inverted on the wrong value, so any non-`failed` status (including a manual-edit sidecar's `pending`, and now `notAnalyzed`) was wrongly treated as already handled and never retried by `Scan`. This surfaced directly from this change: the four manual-edit fallbacks stop writing an explicit status, making the not-yet-analyzed case more common and more visible, so it's fixed alongside the rename rather than left as a latent bug.

## Risks / Trade-offs

- [The read-time fallback silently reclassifies *any* unrecognized `AnalysisStatus` as `notAnalyzed`, not just the legacy `pending` value — a genuine future bug that produces a garbage status string would be masked as "not analyzed" instead of surfacing as an error] → Accepted: `AnalysisStatus` is only ever written by PictureService's own code from a small closed set (`ok`/`uncertain`/`failed`, or left unset), so an unrecognized value can only mean "not yet analyzed by today's code" (including the legacy `pending` case) — there's no external writer that could inject something else.

## Migration Plan

None. No stored data is rewritten; the code changes ship as a normal deploy, and existing sidecar records (including any with `AnalysisStatus: "pending"`) read correctly as `notAnalyzed` from the moment the new PictureService build is live.
