## Why

Loading card data (`/`, `/collection`, `/gallery`) got much slower after PictureService was rewritten from C# to Python. The dominant cost is in `ListCards`: for every photo, `PhotoStore.create_download_url` opens a brand-new `aioboto3` S3 client (and its underlying `aiohttp` session/connector) via `async with self._client() as s3:`, generates one presigned URL, then tears the client down — repeated sequentially, once per photo, with no concurrency. `SidecarTable` does the same per-call client construction for its DynamoDB calls. The old C# `PhotoStore`/`SidecarStore` held a single injected `IAmazonS3`/DynamoDB client for the service's lifetime and reused it (and its connection pool) across every call. For a collection with thousands of photos, paying full client/connection setup cost per photo, sequentially, is the regression — matching the pattern already fixed on the Web side in `web-grpc-client-connection-reuse`.

## What Changes

- `PhotoStore` and `SidecarTable` hold one long-lived `aioboto3` S3 client / DynamoDB resource each (opened once, e.g. at service startup) instead of opening and closing a new one on every method call.
- `main.py`'s `_serve` startup/shutdown sequence owns the lifetime of those long-lived clients (opened before the gRPC server starts, closed during shutdown alongside `server.stop`).
- No change to any RPC's request/response shape, error handling, or return values — this is a client-lifecycle fix, not a behavior change.

## Capabilities

### New Capabilities
- `picture-service-aws-client-reuse`: PictureService maintains one long-lived AWS SDK client per backing store (S3, DynamoDB) for its process lifetime and reuses it across calls, instead of establishing a new client per call.

### Modified Capabilities
(none — no existing capability's requirements change; this is the PictureService-side counterpart to the already-shipped `web-grpc-client-connection-reuse`)

## Impact

- `picture_service/src/picture_service/photo_store.py` (`PhotoStore._client`, and every method using `async with self._client() as s3:`)
- `picture_service/src/picture_service/sidecar_table.py` (`SidecarTable._resource`, and every method using `async with self._resource() as dynamodb:`)
- `picture_service/src/picture_service/main.py` (`_serve` — construct the clients once, close them on shutdown)
- No changes to `NinjagoScanner.Web`, `NinjagoScanner.CatalogService`, or the gRPC contract (`picture_service.proto`)
- Test impact: `picture_service/`'s own `uv run pytest` suite (any existing tests constructing `PhotoStore`/`SidecarTable` directly need to pass an already-open client/resource, or use an async fixture that opens/closes one)
