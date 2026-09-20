## 1. Contract (proto)

- [x] 1.1 In `NinjagoScanner.Web/Protos/picture_service.proto`, add `optional bool skip_analysis = 9;` to `UploadPhotoMetadata`, and add `rpc ListSourceFileNames (ListSourceFileNamesRequest) returns (ListSourceFileNamesResponse)` with `collection_id` on the request and `repeated string source_file_names` on the response; verify `dotnet build NinjagoScanner.slnx` succeeds
- [x] 1.2 Regenerate the Python stubs (`uv run python scripts/gen_proto.py` in `picture_service/`) and verify `pb2.ListSourceFileNamesRequest` and `UploadPhotoMetadata.skip_analysis` are importable and `uv run pytest` still collects

## 2. PictureService: skip-analysis upload

- [x] 2.1 Add a failing test in `picture_service/tests/test_picture_scanner_service.py`: an upload with `skip_analysis` stores the bytes, writes a sidecar with `source_file_name` and `notAnalyzed`, returns a card entry with `notAnalyzed`, and never calls the model factory
- [x] 2.2 Implement the skip branch in `UploadPhoto` (`picture_scanner_service.py`): after validation and `put_bytes`, write the `notAnalyzed` sidecar through the sidecar cache and return the card entry without loading `ScannerConfig`/catalog; verify test 2.1 passes
- [x] 2.3 Add tests that a skip-analysis upload succeeds with no Gemini API key and with a catalog loader that raises, and that validation still returns `InvalidArgument` for an unsupported extension, empty content, and a missing `collection_id`; verify they pass
- [x] 2.4 Add a test that two skip-analysis uploads with the same file name produce two distinct photo IDs, and one that a later `Scan` analyzes a skip-analysis photo and keeps its `source_file_name`; verify they pass

## 3. PictureService: source file name listing

- [x] 3.1 Add a names-only paginated query to `sidecar_table.py` (`CollectionId = :cid`, `ProjectionExpression` on `SourceFileName`, following `LastEvaluatedKey`) and tests in `test_sidecar_table.py` covering multiple pages, missing `SourceFileName`, and other-collection exclusion; verify they pass
- [x] 3.2 Implement the `ListSourceFileNames` handler in `picture_scanner_service.py`: `InvalidArgument` for a blank `collection_id`, de-duplicated names, no photo-store or download-URL calls; add tests for empty collection, duplicate names reported once, photos without a name omitted, and a name written by a skip-analysis upload appearing in the next listing; verify they pass
- [x] 3.3 Add a test that listing does not call `PhotoStore.list_photo_ids`, `exists`, or `create_download_url` (fake photo store records calls); verify it passes
- [x] 3.4 Run the whole picture_service suite (`uv run pytest`) and verify it is green

## 4. Web: client

- [x] 4.1 Add `ListSourceFileNamesAsync` to `PictureServiceClient.cs` returning the names as an ordinal `HashSet<string>` and attaching the resolved `collection_id`; verify with a test against the fake PictureService host
- [x] 4.2 Add a batch upload method to `PictureServiceClient.cs` that reuses the existing size/type validation and streaming, sets `skip_analysis`, and does NOT call `GetDownloadUrlAsync`; verify with tests that the request metadata carries `skip_analysis` and that no `GetPhotoDownloadUrl` call is made
- [x] 4.3 Extend `NinjagoScanner.Web.Tests/Fixtures/PictureServiceTestHost.cs` (the hand-written fake `CardPictureServiceBase`) with `ListSourceFileNames` and with recording of `skip_analysis`/uploaded names, and verify the existing Web tests still pass (`dotnet test NinjagoScanner.Web.Tests`)

## 5. Web: batch orchestration

- [x] 5.1 Extract the batch loop into a testable service/class (not in the `.razor` code block) that takes the selected files, the existing-names set, and an upload delegate; it uploads sequentially, skips names in the set, adds each uploaded name to the set, records failures with reasons without aborting, and honors a `CancellationToken`; verify with unit tests for: skip existing, skip same name within batch, case-sensitive matching, invalid file recorded and continue, upload exception recorded and continue, cancellation stops before the next file, one-file batch still skips analysis
- [x] 5.2 Add a test for the retry scenario (1,000 files, 400 already present → 400 skipped, 600 uploaded) using the loop with fakes; verify it passes

## 6. Web: upload page

- [ ] 6.1 In `Upload.razor`, keep the single-photo input unchanged and add a separate batch input (`multiple`, no `capture`, optional folder selection attribute) reading up to 10,000 files via `GetMultipleFiles(10000)` and showing a German error above the limit; verify with a bUnit/component test or manual run that both inputs render and the limit error appears
- [ ] 6.2 Wire the batch button to load existing names and run the loop from task 5.1, disabling the button and showing an in-progress label while running, and cancelling the loop on component dispose; verify by a manual run that the button is disabled during a batch and re-enabled afterwards
- [ ] 6.3 Render the progress summary (processed of total, uploaded/skipped/failed counts, throttled re-rendering) with no per-file rows, and after completion the skipped names and failed names with reasons; verify by a manual run with a mixed set (new, already-uploaded, unsupported, oversized files) that counts and lists are correct
- [ ] 6.4 Verify the single-photo upload path is unchanged (analysis inline, existing names not skipped, download URL fetched) with the existing upload tests plus a manual mobile-camera-style run

## 7. Integration and docs

- [ ] 7.1 Manual end-to-end run against local CatalogService, picture_service and Web: batch-upload ~200 photos, interrupt mid-way (close the tab), re-select the same files, confirm only the remainder uploads; then confirm the photos show as "noch nicht analysiert" on Overview and that "Gemini-Suche starten" analyzes them and keeps their file names
- [x] 7.2 Run `dotnet test NinjagoScanner.slnx` and `uv run pytest` in `picture_service/`, and verify both are green
- [x] 7.3 Run `openspec validate batch-photo-upload --strict` and verify no errors
- [x] 7.4 Invoke `openspec-update-glossary` if the change introduced terms worth recording (e.g. "Batch Upload"), and verify `openspec/GLOSSARY.md` is consistent
