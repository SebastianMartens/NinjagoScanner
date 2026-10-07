## Context

See proposal.md - Why. Two architectural facts drive this design:

1. **Web never proxies photo bytes.** `Review.razor` (and the gallery/overview/collection views) render photos via `<img src="@photo.ImageUrl">`, where `ImageUrl` is a presigned S3 GET URL resolved through `PictureServiceClient.GetPhotoDownloadUrl(s)`. The browser fetches straight from S3. Any "rotation" that doesn't touch stored bytes has to be applied client-side, in the browser, on the `<img>` element itself.
2. **The discussed alternative - rewriting the S3 object - was explicitly rejected** (see conversation): PictureService has no image-manipulation dependency today, and a destructive rewrite adds a new failure mode (partial write, re-encode loss) for a problem that is purely about how the photo is *displayed*. The user also narrowed scope to 180° only (not arbitrary 90° increments), which removes the one real complication a view-only approach would otherwise have: a 90°/270° rotation swaps displayed width/height and breaks fixed-aspect-ratio grid tiles; 180° does not.

## Goals / Non-Goals

**Goals:**
- A single boolean rotation flag per photo, persisted as an ordinary sidecar field, editable like the existing single-field sidecar edits.
- Every Web surface that renders a photo picks up the flag from the same `CardEntry` data it already fetches via `ListCards`, so there's one data path, not a bespoke lookup per page.

**Non-Goals:**
- Arbitrary-angle rotation (90°/270°) - explicitly out of scope per user direction.
- Rotating the stored photo bytes in S3.
- Triggering or affecting AI Analysis in any way.
- Applying rotation to anything outside the Web app's rendering (e.g. a future "download original" feature would still hand out the un-rotated file - acceptable since no such feature exists today).

## Decisions

**Field shape and placement**: a single bool, `rotated_180`, added to `CardEntry` (proto) / `SidecarRecord` (Python, `rotated_180: bool = False`) / the sidecar's Judged section (per `picture-service-sidecar-sections`'s fixed-field list). A bool is sufficient because only two states exist; no need for a degrees-enum that would invite later 90°/270° support that isn't being built.
- Alternative considered: a `rotation_degrees` int (0/180). Rejected - adds a validation surface (reject 90/270) for no present benefit; trivial to widen later if ever needed, since a bool-to-enum migration is a one-line default mapping (`false → 0`, `true → 180`).

**RPC shape**: `UpdateRotation(photo_id, collection_id, rotated_180) -> { success }`, implemented exactly like `UpdateReviewStatus`/`UpdateCardLanguage` (same not-exists-creates-notAnalyzed-record behavior, same collection scoping). This keeps `picture-service-sidecar-editing`'s six RPCs structurally uniform rather than inventing a different shape for one of them.
- Alternative considered: folding rotation into `UpdateSidecar`'s general-purpose field bag instead of a dedicated RPC. Rejected for the same reason the other four single-field RPCs exist as dedicated RPCs: the Review page's rotate button is a one-field, no-other-side-effects action, and reusing `UpdateSidecar` would require the caller to first read back every other field to avoid clobbering them.

**Proto field numbering**: `CardEntry.rotated_180` gets field number 16 (15 is `reserved`/retired `download_url`, 14 is `source_file_name` - 16 is the next free number). `UpdateRotationRequest`/`UpdateRotationResponse` follow the same field layout as `UpdateReviewStatusRequest` (`photo_id = 1`, payload field `= 3` to mirror the sibling messages' gap at 2, `collection_id = 4`).

**Rendering**: apply `transform: rotate(180deg)` via a CSS class toggled by `photo.Rotated180` (or equivalent bound property) on the `<img>` wherever `CardEntry`/the view models derived from it already carry the photo. Because `CollectionQueryService` is the one place that turns `PictureServiceClient`/`CatalogServiceClient` data into every page's view models, threading `Rotated180` through its existing view-model types is enough to reach every render site without each `.razor` file needing its own data-fetching logic. The three actual render sites are Review (`CardListItem`), Gallery (`GalleryCardItem`), and Collection detail (`CollectionCardPhotoItem`) - the Overview page renders no photo images (only per-card ownership counts), so it needs no change.
- Alternative considered: a server-side `<img>` wrapper component that reads the flag and emits the class, instead of each page applying it inline. Left as an implementation choice for tasks.md rather than a design decision - either is a few lines; `CollectionQueryService` already being the single data funnel is the part that matters for keeping this change from sprawling across many files.

**Testing against production data**: per CLAUDE.md/local dev setup, PictureService's local (non-Docker) run already points at the real production S3 bucket/DynamoDB table (only test data lives there). Manual verification of this feature will exercise `UpdateRotation` against that same production-backed local setup - consistent with how every other sidecar edit (`UpdateSetName`, `UpdateReviewStatus`, etc.) has always been tested locally. No new test infrastructure or fixtures are needed beyond extending `PictureServiceTestHost.cs`'s fake (automated tests use the in-process fake, not production data) and `picture_service/tests`' existing sidecar-editing test patterns.

## Risks / Trade-offs

- **Every render site must remember to apply the transform** → Mitigated by funneling the flag through `CollectionQueryService`'s shared view models rather than each page re-fetching it independently; a missed render site is a one-line fix later (not a data-model problem).
- **A future "export/download original" feature would ignore the flag** → Acceptable now (no such feature exists); call out `rotated_180` in that feature's own design if/when it's proposed.
- **Bool can't represent 90°/270° if ever needed later** → Acceptable per explicit scope decision; migration path (see Decisions) is cheap if it's ever revisited.

## Migration Plan

Purely additive: new proto field (default `false`), new sidecar field (absent/`None` in existing records reads as "unrotated" - same pattern as every other optional Judged field per `picture-service-sidecar-sections`'s "Unresolved fields are still present, just empty or raw"). No backfill, no data migration, no `MigrateSidecars` change needed. Rollback is deleting the new RPC/field/UI control - no stored data becomes invalid if rolled back, since nothing outside this feature reads `rotated_180`.
