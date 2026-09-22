## Context

See proposal.md - Why for the measured cost and the shared-tenancy problem it causes. The relevant code: `PhotoStore.create_download_url` (`picture_service/src/picture_service/photo_store.py`) calls the aioboto3 S3 client's `generate_presigned_url`. That method is inherited unchanged from `botocore.client.BaseClient` - aiobotocore only makes the *network-calling* SDK operations (`get_object`, `list_objects_v2`, DynamoDB `query`, etc.) actually async by routing them through `AioBaseClient._make_api_call`; `generate_presigned_url` does no network call (it's local HMAC-SHA256 signing over the request parameters), so it was never wrapped, and `await self._s3.generate_presigned_url(...)` simply awaits a coroutine-shaped call to a plain synchronous method. The signing work itself runs on PictureService's one asyncio event loop thread, which is also where every other in-flight RPC's coroutine resumes - so the loop cannot service any of them until the current presign call returns.

`create_download_url` is called from two RPC handlers today: the singular `GetPhotoDownloadUrl` (one call - the blocking window is negligible) and `ListCards` (up to one call per photo in the collection - the actual risk, measured at 4.62s for 10,354 photos).

## Goals / Non-Goals

**Goals:**
- Stop a large batch of presign calls from blocking PictureService's event loop, so concurrent RPCs on the same process are serviced without waiting for it.
- Fix this once, at the shared call site, so it covers every current and future caller automatically.

**Non-Goals:**
- Reducing the total CPU time presigning costs, or the number of URLs resolved per request - that's `scope-photo-download-urls`'s job, not this change's.
- Reducing `ListCards`'s own wall-clock latency for the request that's calling it - moving work off the event loop doesn't make that work faster, it only stops it from blocking others. The calling request's own duration is expected to be roughly unchanged (see Risks below for the thread-hop overhead).

## Decisions

**Wrap the call in `asyncio.to_thread`, at the `PhotoStore.create_download_url` call site.**

`asyncio.to_thread(func, *args)` runs `func` in the default executor (a `ThreadPoolExecutor`) and awaits the result, yielding the event loop for the duration. It's the standard, minimal-surface-area way to move a known-synchronous call off the loop without introducing a new dependency or a manually-managed executor.

Alternatives considered:
- **`loop.run_in_executor(None, func, *args)` directly.** Equivalent behavior to `asyncio.to_thread` (which is a thin convenience wrapper added in Python 3.9 over exactly this call) - no meaningful difference; `asyncio.to_thread` is preferred for readability.
- **Wrap only inside `ListCards`'s loop, not in `PhotoStore`.** Rejected: it would fix the measured case but leave `photo_store.py`'s public method secretly synchronous/blocking for every other and future caller, reintroducing the same bug wherever else it's called from a hot path. Fixing it at the source is the same amount of code and removes the footgun entirely.
- **Batch/parallelize presign calls (e.g. `asyncio.gather` over threaded calls in chunks) instead of one `to_thread` per call.** Would also reduce total wall-clock time for the calling request, not just unblock others - out of scope per the Non-Goals above (that's a performance change, this is an availability fix), and it entangles this change with `scope-photo-download-urls`, which changes how many URLs get resolved in the first place. Revisit only if scoping still leaves a batch large enough to matter.

## Risks / Trade-offs

- **Thread-hop overhead per call.** Each `asyncio.to_thread` call has a small fixed cost (scheduling onto the executor, context switch) beyond the signing work itself. For `ListCards`'s loop this could very slightly increase that request's own total wall-clock time compared to today's tight synchronous loop, in exchange for no longer freezing every other request. This is the intended trade-off, not a bug - the specs above make that the change's proof (other concurrent requests complete during the window) rather than requiring the calling request itself to speed up.
- **Default executor is process-wide and unbounded-ish (min(32, cpu_count+4) workers).** A pathological number of simultaneous huge `ListCards` calls could exhaust worker threads and start queuing. Not a realistic concern at this collection's scale (thousands of photos, not concurrent requests each presigning thousands), and out of scope for this change; note it as a future watch-item rather than solving it speculatively.
- **No change in correctness, only in scheduling.** The signing computation itself is unchanged - same inputs, same outputs, same errors - so there's no behavioral migration to reason about beyond the concurrency property the new spec states.

## Migration Plan

Single-file change, no data migration, no proto/contract change. Deploy PictureService; no ordering dependency on `NinjagoScanner.Web` or on the companion `scope-photo-download-urls` change - they can land in either order or independently.

Rollback: revert the one commit: reverting `create_download_url` to a direct `await` restores today's (blocking but simpler) behavior with no other side effects.
