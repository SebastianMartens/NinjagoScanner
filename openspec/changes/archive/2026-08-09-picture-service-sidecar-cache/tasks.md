## 1. SidecarCache

- [x] 1.1 Create `SidecarCache` in `NinjagoScanner.PictureService`: a `ConcurrentDictionary<string, SidecarRecord?>` keyed by resolved absolute sidecar file path.
- [x] 1.2 Add a read-through method (e.g. `GetAsync(sidecarPath, cancellationToken)`) that returns the cached record on a hit, or on a miss calls `SidecarStore.ReadRecordAsync`, stores a successful result in the cache, and returns it without caching read failures (per design.md Decision 4).
- [x] 1.3 Add a write-through method (e.g. `SetAsync(sidecarPath, record, cancellationToken)`) that calls `SidecarStore.WriteAsync`/`WriteRecordAsync` to persist to disk and then stores the same record in the cache.
- [x] 1.4 Register `SidecarCache` as a singleton in `NinjagoScanner.PictureService/Program.cs`.

## 2. Wire PictureScannerGrpcService through the cache

- [x] 2.1 Inject `SidecarCache` into `PictureScannerGrpcService`'s constructor alongside the existing `IConfiguration`/`ILogger` dependencies.
- [x] 2.2 Update `ListCards` to read sidecar content via `SidecarCache` instead of calling `SidecarStore.ReadRecordAsync` directly (the `File.Exists` "no sidecar yet" check stays as-is, per design.md Non-Goals).
- [x] 2.3 Update `Scan` to write each analyzed sidecar via `SidecarCache`'s write-through method instead of calling `SidecarStore.WriteAsync` directly (including its pre-write read of an existing sidecar's `ReviewStatus`, which should also go through the cache).
- [x] 2.4 Update `UpdateSidecar` to read the existing record and write the updated record via `SidecarCache`.
- [x] 2.5 Update `UpdateSetName` to read the existing record and write the updated record via `SidecarCache`.
- [x] 2.6 Update `UpdateReviewStatus` to read the existing record and write the updated record via `SidecarCache`.
- [x] 2.7 Update `MigrateSidecars` to read and rewrite records via `SidecarCache` (its raw `JsonDocument.Parse` legacy-format check can stay a direct file read, since it's inspecting whether a rewrite is needed at all, not just reading sidecar content).

## 3. Tests

- [x] 3.1 Add unit tests for `SidecarCache`: cache-miss reads from disk and populates the cache; cache-hit does not read from disk again; write-through updates the cache without a subsequent disk read; a read failure (corrupt JSON) is not cached and is retried on the next read.
- [x] 3.2 Add/extend `PictureScannerGrpcService` tests (following the existing per-RPC test file convention in `NinjagoScanner.PictureService.Tests/Services/`) to verify that a value written by one RPC (`UpdateSidecar`, `UpdateSetName`, `UpdateReviewStatus`, `Scan`, or `MigrateSidecars`) is immediately visible in a subsequent `ListCards` call within the same test, without relying on disk state.
- [x] 3.3 Run the full `NinjagoScanner.PictureService.Tests` suite and confirm all tests pass.

## 4. Manual verification

- [x] 4.1 Run `PictureService` and `NinjagoScanner.Web` against the real `cardFotos` library; confirm the Review page's confirm/reassign actions feel noticeably faster on the second and later interactions within the same `PictureService` process lifetime.
- [x] 4.2 Confirm a review status change, series reassignment, and sidecar edit each still persist correctly to the `.json` sidecar file on disk (cache does not diverge from disk).
