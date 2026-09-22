## 1. Proto contract

- [ ] 1.1 Remove `CardEntry.download_url` and add the bounded `GetPhotoDownloadUrls` RPC (request: `collection_id` + repeated `photo_id`; response: repeated `{photo_id, download_url}`, omitting unresolvable IDs) to `NinjagoScanner.Web/Protos/picture_service.proto`, and apply the identical change to `picture_service`'s copy of the same `.proto` file, verifying both files stay byte-for-byte identical per repo convention.
- [ ] 1.2 Regenerate gRPC stubs on both sides (`dotnet build NinjagoScanner.slnx` for Web; `uv run python scripts/gen_proto.py` for `picture_service`) and verify both build cleanly.

## 2. PictureService

- [ ] 2.1 Remove the presign call from `ListCards`'s per-photo loop in `picture_scanner_service.py` (`CardEntry` no longer carries a URL), and verify `uv run pytest` for existing `ListCards` tests updated to not assert on `download_url`.
- [ ] 2.2 Implement the `GetPhotoDownloadUrls` handler: resolve each requested photo ID via `PhotoStore.create_download_url` (or, if `picture-service-nonblocking-presign` has landed, whatever it wraps that call with), omitting any photo ID with no stored bytes from the response instead of failing the call.
- [ ] 2.3 Add tests for `GetPhotoDownloadUrls` covering: several valid IDs resolved in one call, a mix of valid and stale/missing IDs (missing ones omitted, others still resolved), an empty request list, and a request scoped to `collection_id` not resolving another collection's photo ID — verify `uv run pytest` passes.

## 3. Web: PictureServiceClient

- [ ] 3.1 Add `PictureServiceClient.GetDownloadUrlsAsync(IEnumerable<string> photoIds)` wrapping the new RPC, returning a photoId → url lookup; remove `download_url` reads from `ToCardListItem`/`GetCardsAsync`. Verify `dotnet build NinjagoScanner.slnx` succeeds.
- [ ] 3.2 Update `PictureServiceTestHost` (`NinjagoScanner.Web.Tests/Fixtures/`) to implement the new RPC handler (mirroring PictureService's omit-on-missing behavior) so in-process tests can exercise it, and verify existing fixture-dependent tests still pass.

## 4. Web: Review page

- [ ] 4.1 Update `ReviewSession`/`Review.razor` to resolve download URLs for the currently displayed group's photos that don't already have one, on initial load, on navigation (next/previous, restart-from-beginning), and after a local edit changes the current group's membership — verify via a test that an already-resolved photo's URL is never re-requested (stability requirement) and that a newly-entered photo (e.g. after a series reassignment) gets one.
- [ ] 4.2 Verify `dotnet test NinjagoScanner.Web.Tests --filter "FullyQualifiedName~Review"` passes, including updated tests asserting the bounded call is scoped to the displayed group only.

## 5. Web: Gallery page

- [ ] 5.1 Update `CollectionQueryService.GetGalleryCardsAsync` to resolve download URLs only for the matched photo of each catalog card within the selected series (using the already-computed match), instead of reading `entry.DownloadUrl` from an unscoped `ListCardEntriesAsync` call.
- [ ] 5.2 Verify `dotnet test NinjagoScanner.Web.Tests --filter "FullyQualifiedName~Gallery"` passes, including a test asserting switching the selected series does not resolve URLs for the previously selected series' photos.

## 6. Web: Collection detail view

- [ ] 6.1 Update `CollectionQueryService.BuildCardPhotosAsync` to resolve download URLs via the new bounded call for the one card's matched photo IDs, instead of reading `entry.DownloadUrl`.
- [ ] 6.2 Verify `dotnet test NinjagoScanner.Web.Tests --filter "FullyQualifiedName~CollectionCardDetails"` (or the closest matching existing test filter) passes.

## 7. Full verification

- [ ] 7.1 Run `dotnet build NinjagoScanner.slnx` and `dotnet test NinjagoScanner.slnx` and verify both succeed end to end.
- [ ] 7.2 Run `uv run pytest` in `picture_service/` and verify it succeeds end to end.
- [ ] 7.3 Manually verify (local run per CLAUDE.md, or against the real data used earlier in this investigation) that opening `/review` and `/gallery` each issue exactly one bounded `GetPhotoDownloadUrls` call per group/series shown, not one call per photo in the whole collection.
