## Why

`ListCards` resolves a pre-signed S3 download URL for every photo in a collection via `PhotoStore.create_download_url`, which calls aioboto3's `generate_presigned_url`. That call does no network I/O — it's local HMAC signing — and aiobotocore does not make it actually asynchronous: it's a plain synchronous method inherited from `botocore.client.BaseClient`, just `await`ed. It therefore runs synchronously on PictureService's single asyncio event loop and fully blocks it for its duration, so no other coroutine on that process — another user's upload, a scan in progress, another collection's `ListCards` — can make progress while it runs.

Measured directly against a real 10,354-photo production collection (manual OTel spans, console-exporter-verified): presigning all of them in one `ListCards` call took 4.62s. For that entire window, this isn't just "the page is slow for the person loading it" — it's "nobody else on this Fly machine can be served at all." That's a shared-tenancy availability problem, distinct from and worse than the page's own load time.

This is deliberately narrow: it does not reduce how much work presigning does, or how many URLs get resolved per request — a separate, already-planned change (`scope-photo-download-urls`) addresses that by resolving URLs only for photos actually displayed. This change only stops that work from blocking everyone else while it runs.

## What Changes

- `PhotoStore.create_download_url` runs the presign call off the event loop (e.g. via `asyncio.to_thread`) instead of directly awaiting the underlying synchronous SDK call, so PictureService's event loop stays free to service other concurrent RPCs while one request resolves many URLs.
- The fix lives at this one shared low-level call site rather than special-casing `ListCards`'s loop, so every caller — today `GetPhotoDownloadUrl` (low risk, one call) and `ListCards` (the actual risk, up to thousands of calls per request) — benefits uniformly, as does any future caller.
- No proto change. No Web-side change. Fully internal to PictureService.

## Capabilities

### New Capabilities
- `picture-service-nonblocking-presign`: PictureService resolves pre-signed download URLs without blocking its single event loop, so other concurrent RPCs are still serviced while one request resolves many URLs at once.

### Modified Capabilities
(none — the observable RPC contracts are unchanged; only the blocking behavior of an internal call is fixed)

## Impact

- `picture_service/src/picture_service/photo_store.py`: `create_download_url` implementation.
- `picture_service/tests/`: existing `photo_store`/`picture_scanner_service` tests updated as needed; a new concurrency-behavior test added (a fake/slow presign function plus asyncio, asserting interleaving — not just an end-to-end wall-clock number).
- No changes to `NinjagoScanner.Web`, no proto changes, no deployment ordering constraints — deploys to PictureService alone, independent of the companion `scope-photo-download-urls` change.
