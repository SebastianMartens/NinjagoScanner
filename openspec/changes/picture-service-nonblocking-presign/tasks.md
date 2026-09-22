## 1. Implementation

- [ ] 1.1 Change `PhotoStore.create_download_url` (`picture_service/src/picture_service/photo_store.py`) to run `generate_presigned_url` via `asyncio.to_thread` instead of directly `await`ing it, and verify `uv run pytest` still passes for existing `photo_store`/`picture_scanner_service` tests unchanged in behavior.

## 2. Verification

- [ ] 2.1 Add a concurrency test proving the event loop is not blocked: drive `create_download_url` with a fake/slow S3 client (e.g. a presign stub with a `time.sleep` to simulate CPU-bound work) and, with `asyncio.gather`, assert a concurrent, independent coroutine completes before the slow presign call returns - verify the test fails against the old direct-`await` implementation and passes against the new one.
- [ ] 2.2 Add or extend a test asserting `create_download_url`'s return value and not-found error behavior are unchanged, and verify `uv run pytest` passes.
- [ ] 2.3 Manually verify against a large collection, using the `list_cards.build_entries` OTel span already added to `ListCards` (see its `list_cards.presign_seconds` attribute), that a concurrent `GetPhotoDownloadUrl` call for an unrelated photo, issued while a large `ListCards` call is presigning, returns promptly instead of waiting for `ListCards` to finish.
