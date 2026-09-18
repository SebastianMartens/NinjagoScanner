## 1. Contract

- [x] 1.1 Add `rpc ReanalyzePhoto (ReanalyzePhotoRequest) returns (ReanalyzePhotoResponse)` with `photo_id` + `collection_id` request fields and a `CardEntry card` response to `NinjagoScanner.Web/Protos/picture_service.proto`, and extend the file's header comment to list it as a collection-scoped RPC; verify `dotnet build NinjagoScanner.slnx` and `uv run python scripts/gen_proto.py` both succeed and the generated stubs expose the new RPC.

## 2. PictureService

- [x] 2.1 In `picture_scanner_service.py`, extract the shared "load config → require API key → load catalog snapshot → `analyze_card` with unexpected-exception fallback" steps out of `UploadPhoto` into one private helper, and make `UploadPhoto` call it; verify the existing `test_upload_photo_*` tests in `picture_service/tests/test_picture_scanner_service.py` still pass unchanged.
- [x] 2.2 Implement `ReanalyzePhoto`: validate `photo_id`/`collection_id` (`INVALID_ARGUMENT`), `NOT_FOUND` when the photo isn't in that collection, read the sidecar and derive the verified pin via `_verified_match`, fetch bytes from `PhotoStore`, run the shared helper, then write the result via `set_from_analysis_result` and return `CardEntry`; verify with tests for blank ids, missing photo, photo only in another collection, and a photo with no sidecar getting one created.
- [x] 2.3 Handle failure semantics: `FAILED_PRECONDITION` for no API key and `UNAVAILABLE` for an unreachable catalog, and abort `UNAVAILABLE` on `is_transport_failure` before writing anything; verify with tests that in each case the existing sidecar (status, fields, scanned-at) is byte-for-byte unchanged, and that a content-level failure is recorded as `failed` with an error message and returned normally.
- [x] 2.4 Re-read the sidecar after the analysis completes and copy that record's `review_status` onto the result (not the pre-analysis value); verify with a test whose fake model changes the photo's review status mid-analysis and asserts the final sidecar carries the changed value, plus tests that `incorrect`/`verified` review statuses survive and that a `verified` photo keeps its series and card number while an `unreviewed` one takes the new match.
- [x] 2.5 Verify re-analysis is not skipped for an existing `ok` sidecar and that the new run replaces `detected`, `derived`, and `scanned_at_utc`, then that a following `ListCards`/`GetCardDetails` reflect it; run `uv run pytest` in `picture_service/` and confirm the whole suite passes.

## 3. Web client and test host

- [x] 3.1 Add `ReanalyzePhotoAsync(string photoId, CancellationToken)` to `NinjagoScanner.Web/Services/PictureServiceClient.cs` that attaches the collection id, calls the new RPC, and returns the mapped `CardListItem` via the existing `ToCardListItem`; verify with a test in `NinjagoScanner.Web.Tests/Services/` (in the style of `PictureServiceClientDeletePhotoTests`) that it sends the photo id and collection id and maps the response.
- [x] 3.2 Add a `ReanalyzePhoto` override to the fake in `NinjagoScanner.Web.Tests/Fixtures/PictureServiceTestHost.cs` that updates its in-memory card and can be told to fail with a given status code; verify the 3.1 test and a failure-status test (`RpcException` propagates from the client) pass with it.

## 4. Review page

- [ ] 4.1 Extract the duplicated "reload groups, re-sync card-number drafts, re-locate current group by key else clamp" block from `RunPhotoActionAsync` and `ReassignSeriesAsync` in `Review.razor` into one helper; verify `dotnet test NinjagoScanner.Web.Tests` still passes and the Review page's existing behavior (status set, series reassign, delete) is unchanged in a manual run.
- [ ] 4.2 Add `reanalyzingPhotoIds` (a `HashSet<string>`) and `reanalysisErrors` (a `Dictionary<string, string>`), and make `IsBusy` also true for ids in the set; verify a tile whose id is in the set has all of its change-making controls disabled while a neighbouring tile's are not.
- [ ] 4.3 Add the "Neu analysieren" / "Analysiere..." button to `review-photo-actions` and a `ReanalyzeAsync(photo)` handler: clear the photo's error, add it to the set, call `PictureServiceClient.ReanalyzePhotoAsync`, evict `photoDetails[photoId]` (and re-fetch it if the tile is expanded), run the helper from 4.1, and always remove the id from the set in `finally`; verify in a manual run against the local stack that the button shows progress, the tile updates afterwards, and expanded details show the new scanned-at time.
- [ ] 4.4 Catch failures in `ReanalyzeAsync`, store a German error message (distinguishing `Unavailable`/`FailedPrecondition` from other failures) in `reanalysisErrors`, render it on the tile, leave the tile's data unchanged, and clear it on the next attempt; verify by stopping PictureService's Gemini access (e.g. unset `GEMINI_API_KEY`) and confirming the message appears only on that tile and disappears on retry.
- [ ] 4.5 Add any styling the button or error message needs to `NinjagoScanner.Web/wwwroot/app.css`, reusing `review-btn` and existing status colors; verify visually at desktop and phone width that the row of action buttons doesn't overflow the tile.

## 5. Docs and integration

- [x] 5.1 Update `openspec/GLOSSARY.md`'s "AI Analysis" entry to cover the on-demand single-photo case (or add a "Re-analysis" term) and note that it preserves Review Status; verify the glossary reads consistently with the specs.
- [ ] 5.2 Run `openspec validate review-card-reanalysis --strict`, `dotnet test NinjagoScanner.slnx`, and `uv run pytest` in `picture_service/`, then run the full local stack once and re-analyze a `verified`, an `incorrect` and a `failed` photo end to end, confirming review status is unchanged in each and the `failed` photo's status updates.
