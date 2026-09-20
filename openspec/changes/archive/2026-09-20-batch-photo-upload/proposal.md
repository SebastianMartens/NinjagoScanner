## Why

`/upload` accepts one photo at a time and analyzes it inline, which is impractical for getting an existing archive of card photos (hundreds to thousands) into a collection. People want to select a whole set of photos from disk, upload them in one go, and run the (slow, Gemini-bound) analysis later from the Overview page.

## What Changes

- `/upload` gets a second, separate file input for **batch upload** — multiple files, no `capture` hint, optional folder selection, up to 10,000 files per selection. The existing single-photo mobile-camera input is unchanged (one photo, analysis inline, same-name duplicates allowed).
- Batch mode is decided by **which input was used**, not by file count: the batch input never analyzes, even for a single file.
- The Web page uploads the selected files **sequentially, one by one** — no server-side queue, no new analysis status, no worker. It shows a simple progress summary (uploaded / total, skipped, failed) instead of a row per file, and lists skipped and failed file names in the summary.
- Files that are invalid (unsupported type, over the maximum size) are collected as failures; they do not abort the batch.
- **Skip-if-filename-exists**: before uploading, the Web page asks PictureService for the collection's existing source file names and skips every selected file whose exact file name (ordinal, case-sensitive) is already present. Each successfully uploaded name is added to the set, so same-named files within one batch are skipped too. Re-selecting the same files after an interruption therefore resumes the batch.
- PictureService gains an optional `skip_analysis` flag on `UploadPhotoMetadata`. When set, it stores the bytes, writes a sidecar carrying the source file name with analysis status `notAnalyzed`, and returns without analysis — needing neither a Gemini API key nor CatalogService.
- PictureService gains a lean RPC that lists only the source file names of a collection's photos (from the sidecar table), instead of `ListCards`, which mints a download URL per photo.
- The batch path does not fetch a download URL per uploaded photo.
- Analysis is started later from the existing Overview button ("Gemini-Suche starten"), which calls the existing `Scan` RPC. No change to Overview or `Scan`.
- The Web tests' fake `PictureServiceTestHost` is extended for the new field and RPC.

**Non-goals:** a server-side queue, a `queued` analysis status, a queue-progress RPC, parallel uploads, direct browser-to-S3 upload, scan progress or an already-running guard for `Scan`, name-plus-size duplicate detection, and duplicate skipping on the mobile-camera path.

## Capabilities

### New Capabilities
- `picture-service-source-file-listing`: an RPC that returns the source file names of every photo in a collection, cheaply (no download URLs, no per-photo reads), so callers can detect already-uploaded files.

### Modified Capabilities
- `picture-service-photo-upload`: "Upload triggers analysis on completion" no longer applies when `skip_analysis` is set; a skip-analysis upload persists a `notAnalyzed` sidecar with the source file name and requires no Gemini key or CatalogService. Same-name uploads are still both stored at the RPC level.
- `web-photo-upload`: adds the batch upload input, sequential upload, skip-if-filename-exists, progress/skipped/failed summary and per-file error collection; the existing camera-oriented file picker requirement is narrowed to the single-photo input.

## Impact

- **Cross-cutting contract:** `NinjagoScanner.Web/Protos/picture_service.proto` (canonical copy; `picture_service/` generates its stubs from it) gains `UploadPhotoMetadata.skip_analysis` and one new RPC with its request/response messages. Both are additive and backward-compatible; no existing capability owns the proto, so the contract change is captured through the two PictureService/Web specs above.
- **picture_service/** (Python): `picture_scanner_service.py` (skip-analysis branch in `UploadPhoto`, new RPC handler), sidecar cache/table access for listing names, tests in `picture_service/tests/`.
- **NinjagoScanner.Web:** `Components/Pages/Upload.razor` (second input, sequential loop, progress summary), `Services/PictureServiceClient.cs` (skip-analysis upload without the per-photo download-URL call, source-name listing).
- **NinjagoScanner.Web.Tests:** `Fixtures/PictureServiceTestHost.cs` and new tests for the batch flow.
- **Not affected:** CatalogService, Overview, the `Scan` RPC.
- **Known risks** (recorded in `design.md`): file-name collisions between batch and mobile-camera uploads; a small orphan-photo window if PictureService dies between the S3 write and the sidecar write; `Scan` running for hours with no progress and a parallel-scan risk at ~10,000 photos (out of scope, follow-up).
