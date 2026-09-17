## Context

`PhotoStore` and `SidecarTable` (`picture_service/src/picture_service/{photo_store,sidecar_table}.py`) each hold a shared `aioboto3.Session` (constructed once in `main.py`'s `_serve`) but call `self._session.client("s3", ...)` / `self._session.resource("dynamodb", ...)` fresh inside every method, wrapped in `async with`. Each entry/exit constructs and tears down a new `aiohttp` session (connector, SSL context) for that one call. `ListCards` (`picture_scanner_service.py`) calls `PhotoStore.create_download_url` once per photo in a sequential, unconcurrent loop, so a collection with thousands of photos pays full client construction cost thousands of times per request. The old C# implementation (`PhotoStore.cs`, removed in the Python rewrite) injected a single `IAmazonS3` and reused it for the app's lifetime — this behavior was lost in the port. See proposal.md - Why.

## Goals / Non-Goals

**Goals:**
- Restore one-client-per-store-per-process reuse for S3 and DynamoDB access in PictureService, matching the old C# behavior and the precedent already set on the Web side (`web-grpc-client-connection-reuse`).
- Keep the fix scoped to client lifecycle — no change to request/response shapes, caching behavior, or error handling.

**Non-Goals:**
- Parallelizing the `ListCards` per-photo loop (concurrent `create_download_url`/`sidecar_cache.get` calls). Once client construction is no longer per-call, presigned URL generation is local computation (no network round trip), so the sequential loop's remaining cost is expected to be small; revisit only if a trace after this fix still shows it as dominant.
- Connection pooling tuning (max connections, keep-alive timeouts) beyond aioboto3's defaults.
- Changing how `SidecarCache` warms or caches records — untouched by this change.

## Decisions

**Open the S3 client and DynamoDB resource once in `main.py`, pass the opened objects into `PhotoStore`/`SidecarTable`.**
`aioboto3`'s `session.client(...)`/`session.resource(...)` are async context managers; the natural place to open them for the process lifetime is `_serve`, alongside constructing `PhotoStore`/`SidecarTable` themselves, using `contextlib.AsyncExitStack` so both are cleanly closed on shutdown regardless of which one fails to open. `PhotoStore`/`SidecarTable` change from storing a `Session` + building a client per call, to storing an already-open client/resource directly and using it in every method body (dropping the `async with self._client() as s3:` / `async with self._resource() as dynamodb:` wrapper).

- Alternative considered: give each class its own lazily-initialized client (open on first use, cache thereafter). Rejected — adds lazy-init/locking complexity for no benefit over opening eagerly at startup, since both stores are needed immediately once the gRPC server starts serving.
- Alternative considered: a connection-pooling wrapper/proxy that hides open/close entirely inside `PhotoStore`/`SidecarTable` (each opens its own client internally on construction, closes on an explicit `aclose()`). Rejected in favor of `main.py` owning the `AsyncExitStack` — keeps shutdown ordering explicit and in one place, consistent with how `main.py` already owns `server.stop(grace=5)`.

**Scope: S3 and DynamoDB clients only, not the `aioboto3.Session` itself.**
The `Session` is already constructed once and passed in; it was never the problem. Only the per-call `client()`/`resource()` construction is being eliminated.

## Risks / Trade-offs

- [A long-lived `aiohttp` connector can go stale after long idle periods or if the target endpoint's connection is reset server-side] → `aiobotocore`/`aiohttp` retry transient connection errors at the request level by default; if idle staleness proves an issue in practice, a bounded connector lifetime (analogous to `SocketsHttpHandler.PooledConnectionLifetime` on the Web side) can be added later — not needed to fix the reported regression.
- [Tests that construct `PhotoStore`/`SidecarTable` directly need an open client/resource, not just a `Session`] → update `picture_service/`'s test fixtures to open the client/resource in an async fixture (or use a fake client) rather than passing a bare `Session`.
- [Startup/shutdown ordering: if opening either client fails, the service must not partially start] → use `AsyncExitStack` so a failure during startup unwinds whatever was already opened before `_serve` propagates the error.

## Migration Plan

No data migration. Deploy as a normal PictureService release (Fly.io); rollback is a normal redeploy of the previous image if needed. Validate with a before/after trace of a `ListCards`-heavy page load (`/collection` or `/gallery`) on a collection with many photos, confirming the repeated per-photo client/connection-setup spans are gone — same validation approach as `fix-web-duplicate-prerender-fetch`/`reuse-web-grpc-channels` used for the Web-side regressions.
