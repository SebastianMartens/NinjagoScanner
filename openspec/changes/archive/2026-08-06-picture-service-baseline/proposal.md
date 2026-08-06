## Why

NinjagoScanner.PictureService is running in production, backing card scanning and photo upload for the Web app, but most of its behavior has no OpenSpec baseline. Only one narrow slice (`picture-service-sidecar-review`, covering `AnalysisStatus`/`ReviewStatus` semantics and legacy sidecar migration) is currently specced. The rest — the Scan RPC's orchestration, the Gemini API contract, series-name resolution, card listing, manual sidecar edits, photo upload, and shared directory resolution — exists only as source code, making it hard to propose or review future changes against a documented baseline. This change establishes that baseline: no source code changes, only specs describing current behavior.

## What Changes

- Add baseline specs for the `CardPictureService` gRPC service (`Protos/picture_service.proto`) covering the RPCs not already specced: `Scan`, `ListCards`, `UpdateSidecar`, `UpdateSetName`, `UploadPhoto`. (`MigrateSidecars` and the `AnalysisStatus`/`ReviewStatus` field semantics are already covered by `picture-service-sidecar-review`.)
- Add a baseline spec for the Gemini API call mechanics implemented in `GeminiApiService` (request construction, retry policy, response parsing, confidence/status normalization).
- Add a baseline spec for the series-name resolution logic implemented in `SeriesCatalogService.ResolveSetName` (exact match, evidence-based scoring, tie handling).
- Add a baseline spec for the shared directory/config resolution logic implemented in `ScannerConfig` (request override, configuration keys, default candidate probing, git-worktree awareness).
- No behavior, API, or code changes — this is a documentation-only baseline.

## Capabilities

### New Capabilities
- `picture-service-photo-scan`: `Scan` RPC — batch-analyzes card photos in a directory, validating configuration and catalog availability up front, skipping already-scanned images unless overwrite is requested, preserving existing review state across rescans, and reporting a summary of processed/skipped/uncertain/failed counts.
- `picture-service-gemini-analysis`: the Gemini API call itself — request shape (image + series-catalog prompt), retry policy on transient HTTP errors, response parsing, and confidence/status normalization.
- `picture-service-series-name-matching`: `SeriesCatalogService.ResolveSetName` — resolves the model's freeform set-name guess to a known catalog series name via exact match or scored evidence matching, refusing to guess on a tie.
- `picture-service-card-listing`: `ListCards` RPC — enumerates card photos in a directory and reports each one's sidecar state, including synthetic entries for photos with no sidecar or an unreadable one.
- `picture-service-sidecar-editing`: `UpdateSidecar` and `UpdateSetName` RPCs — manual, human-driven edits to a card's sidecar record, independent of the scan pipeline.
- `picture-service-photo-upload`: `UploadPhoto` RPC — client-streaming upload of a card photo (e.g. from a mobile device) into the card photos directory, with validation and collision-safe naming.
- `picture-service-directory-resolution`: shared logic for resolving the card photos directory and other scanner configuration from a request override, app configuration, or default candidate paths (including a git-worktree-aware main-repo lookup).

### Modified Capabilities
(none — `picture-service-sidecar-review` is unaffected by this change)

## Impact

- Adds files under `openspec/specs/picture-service-*/spec.md` only; no changes to `NinjagoScanner.PictureService` source.
- Establishes the documented contract that `NinjagoScanner.Web` (via `PictureServiceClient`) depends on when triggering scans, listing cards, editing sidecars, and uploading photos over gRPC.
