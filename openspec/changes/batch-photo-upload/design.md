## Context

`/upload` ([Upload.razor](../../../NinjagoScanner.Web/Components/Pages/Upload.razor)) has one `InputFile` with `capture="environment"`. It calls `PictureServiceClient.UploadPhotoAsync`, which streams the bytes to `UploadPhoto` and then fetches a download URL. `UploadPhoto` stores the bytes in S3 under a generated photo ID, runs the Gemini analysis inline, and only then writes the sidecar (DynamoDB) — so today the sidecar, and with it the original file name, exists only after analysis. S3 objects are keyed by photo ID alone; the file name lives only in the sidecar.

The Overview page already has "Gemini-Suche starten", which calls the bulk `Scan` RPC. `Scan` analyzes every photo whose sidecar is missing, `failed`, or not `ok`/`uncertain`, and preserves an existing sidecar's `source_file_name`.

PictureService runs as a single 512 MB Fly machine; Web is a Blazor Server app (one SignalR circuit per tab). Motivation and scope: see proposal.md.

## Goals / Non-Goals

**Goals:**
- Get thousands of photos into a collection with a plain browser, resumable by simply re-selecting the same files.
- Keep the change additive: no impact on the mobile-camera flow, `Scan`, Overview, or CatalogService.

**Non-Goals:**
- Anything that survives a closed tab on the upload side (the upload loop lives in the circuit).
- Faster-than-sequential upload, a server-side queue, or analysis progress (see proposal.md).

## Decisions

### 1. The upload loop lives in the Blazor page, one file at a time

`Upload.razor` iterates the selected `IBrowserFile`s and calls the client once per file. No queue, worker, or persisted batch state.

- **Why:** the user's requirement is "simple". Every file is an independent, atomic `UploadPhoto` call, so there is nothing to recover except which files are done — and that is derivable from the stored data (decision 3).
- **Alternatives:** a server-side queue/worker (durable and tab-independent, but new state, new status, recovery logic — rejected in exploration); parallel uploads (faster, but more circuit and PictureService memory pressure; can be added later behind the same loop).
- **Consequence:** the tab must stay open until the batch finishes. `IBrowserFile` handles die with the circuit, so recovery means re-selecting the files; the page uses a `CancellationToken` cancelled on dispose so a dead circuit stops issuing calls.

### 2. Mode is decided by the input, not the file count

Two inputs on the page: the existing single-photo input (unchanged) and a batch input (`multiple`, no `capture`, optionally `webkitdirectory` passed through `AdditionalAttributes`). The batch input always sends `skip_analysis` and always applies the name-skip rule.

- **Why:** "more than one file" would make the behaviour depend on how many files happen to be picked and silently change what one-file batches do.
- **Consequence:** browsers cannot reliably tell "camera" from "gallery", so a phone user *can* multi-pick from the gallery through the batch input. That is accepted; only the camera hint is absent.
- `InputFileChangeEventArgs.GetMultipleFiles` must be called with `maximumFileCount = 10000` (its default is 10 and it throws above the limit); the page catches this and shows the "too many files" error.

### 3. Resume via "does this file name already exist", using a dedicated lean RPC

Before the loop, the page calls a new RPC (`ListSourceFileNames`) that returns a de-duplicated list of `SourceFileName` values for the collection. The client keeps them in an ordinal `HashSet<string>`; a selected file whose name is in the set is skipped, and a name is added after each successful upload.

- **Why not `ListCards`:** it lists S3 objects, warms the sidecar cache, and creates a download URL per photo — exactly the path that is already slow. The new RPC only needs one paginated DynamoDB query.
- **Query shape:** a `CollectionId = :cid` query with a `ProjectionExpression` on `SourceFileName`, paginated via `LastEvaluatedKey`, reading the table directly rather than through the sidecar cache (the cache holds full records incl. raw model responses; a names-only listing needn't populate it). Writes go through the cache's write-through `put`, which persists synchronously, so a just-uploaded file's name is visible to the next listing. Projection reduces payload, not consumed read capacity (DynamoDB bills on item size), which is acceptable at this scale.
- **Response size:** ~10,000 names × ~40 bytes is well under gRPC's default 4 MB limit, so no streaming is needed. The response is one message: `repeated string source_file_names`.
- **Matching:** exact ordinal comparison, as the user specified.

### 4. `skip_analysis` is an optional field on `UploadPhotoMetadata`

`optional bool skip_analysis = 9;` (field numbers 1–8 are taken). In `UploadPhoto`, after validation and `put_bytes`, the skip path builds a `SidecarRecord(source_file_name=…, analysis_status=NOT_ANALYZED)` directly and writes it via the sidecar cache, then returns `_to_card_entry`. It never reaches `ScannerConfig.load_for_upload` or `_analyze_stored_photo`, which is what makes the Gemini key and CatalogService unnecessary.

- **Alternatives:** a separate `UploadPhotoWithoutAnalysis` RPC (duplicates the stream/validation code; the flag is smaller); making analysis a follow-up RPC (contradicts the current sync contract for the camera path).
- **Compatibility:** an absent field is `false`, so existing clients and the camera path are unaffected. The canonical `.proto` is `NinjagoScanner.Web/Protos/picture_service.proto`; `picture_service/` regenerates its stubs from it, so both sides must be regenerated together.
- **`Scan` compatibility (verified in code, not changed):** `_should_skip_existing_sidecar` only skips `ok`/`uncertain`, so `notAnalyzed` sidecars are analyzed; `Scan` takes `source_file_name` from the existing sidecar; a `Scan` result replaces the record while carrying over `review_status`.

### 5. No download URL per photo in batch mode

`UploadPhotoAsync` currently calls `GetDownloadUrlAsync` (a S3 existence check plus presign) after each upload. The batch path needs only success/failure, so the client gets a separate method (e.g. `UploadPhotoForBatchAsync`) that shares validation and streaming with the existing one but skips the URL fetch and returns only what the summary needs. The existing method's behaviour stays identical.

### 6. Progress is counters plus two capped lists, refreshed on a timer/throttle

State is `total`, `processed`, `uploaded`, `skipped`, `failed`, plus `skippedNames` and `failedItems` (name + reason). The page never renders a per-file row while running; `StateHasChanged` is throttled (about every 250 ms or every N files) so a 10,000-file batch does not push 10,000 renders over SignalR. Failed/skipped lists are shown in a summary once the batch has finished. UI strings are German, following the page.

### 7. Validation per file, before opening the stream

The existing type/size checks (`EnsureUploadIsValid`) run per file; a failure is recorded and the loop continues. Only `InvalidOperationException` (validation) and transport/RPC errors are recorded per file; cancellation of the loop token ends the batch.

## Risks / Trade-offs

- **File-name collisions** → files are skipped although they are different photos (e.g. iOS captures often called `image.jpg`; two cameras both producing `IMG_0001.jpg`; the mobile-camera uploads share the same name space). Mitigation: skipped names are listed in the summary so the user can see and re-upload them via the single-photo input. Future hardening (out of scope): store the file size on the sidecar and compare name + size.
- **Case-sensitive matching** → `IMG_1.JPG` vs `img_1.jpg` are treated as different files. Accepted, per the "exact" requirement.
- **Orphan window** → if PictureService dies between `put_bytes` and the sidecar write, an S3 object exists without a sidecar. It shows up in `ListCards` with its photo ID as name and status `notAnalyzed`, and a retry will upload the file again (a duplicate). Writing the sidecar first would be worse (a name for a non-existent photo, causing permanent skipping), so bytes-first is kept. The window is tiny; no cleanup is built.
- **Tab must stay open** → a closed tab or dropped circuit stops the batch. Mitigation: the retry story (decision 3). Not solved for very long batches; no wake lock or keep-alive is added.
- **Throughput unknown** → sequential upload of large photos through one SignalR circuit may be slow (tens of GB for 10,000 phone photos). Mitigation: sequential design is deliberately the simplest; measure with a couple of hundred files first, and consider small-N parallelism as a follow-up.
- **`Scan` at ~10,000 photos (out of scope, follow-up)** → one blocking call for hours with no progress; `isScanning` is per-circuit, so a second tab or a reload starts a parallel scan (double Gemini spend, racing sidecar writes); every `failed` photo (e.g. non-card photos) is re-attempted on each run. This change makes that situation likelier but does not touch `Scan`.
- **PictureService memory** → `UploadPhoto` buffers each photo (≤ `MaxUploadBytes`, default 15 MB) and copies it once (`bytearray` → `bytes`). Sequential uploads keep at most one such buffer per active batch, but several simultaneous batches multiply it on the 512 MB machine.
- **Sidecar row without analysis fields** → new `notAnalyzed` sidecars carry only `SourceFileName` (and the status). Existing readers already tolerate this ("Sidecar created without an explicit analysis status" / unrecognized status → `notAnalyzed`), and `UpdateSidecar`-style edits already build on an existing record; verified against `_to_card_entry` and `_normalize_analysis_status`.
