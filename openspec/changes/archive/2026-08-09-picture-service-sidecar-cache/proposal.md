## Why

With ~3,800 photos in `cardFotos/`, `PictureService`'s `ListCards` RPC reads and JSON-parses one sidecar file per photo from disk, sequentially, on every call. `NinjagoScanner.Web` calls `ListCards` on every page load and again after every single user action (confirming a review status, reassigning a series, saving a sidecar all trigger a full reload), making the web UI sluggish at real library sizes.

## What Changes

- Add an in-memory `SidecarCache` in `NinjagoScanner.PictureService` that serves sidecar content without hitting disk once a file has been read or written during the process lifetime.
- `ListCards` reads through the cache: a cache hit skips disk entirely; a cache miss reads the sidecar file once and populates the cache.
- Every RPC that writes a sidecar (`Scan`, `UpdateSidecar`, `UpdateSetName`, `UpdateReviewStatus`, `MigrateSidecars`) updates the cache with the just-written record immediately after the disk write succeeds, so later reads never observe stale data.
- `SidecarStore` remains pure file I/O; the cache is a layer in front of it used by `PictureScannerGrpcService`.
- Out of scope for this change: parallelizing the first (cold-start) read of all sidecars after a process restart, and caching "no sidecar file exists yet" to avoid `File.Exists` checks.

## Capabilities

### New Capabilities
- `picture-service-sidecar-cache`: in-memory caching of sidecar file contents, keyed per sidecar file, kept consistent with disk via write-through updates on every sidecar mutation.

### Modified Capabilities
_None._ The external behavior of `ListCards` and the sidecar-writing RPCs (what data they return/persist) is unchanged — this change is behavior-preserving and affects only how sidecar data is read internally.

## Impact

- `NinjagoScanner.PictureService`: new `SidecarCache` class registered as a singleton in `Program.cs`, injected into `PictureScannerGrpcService`. `ListCards`, `Scan`, `UpdateSidecar`, `UpdateSetName`, `UpdateReviewStatus`, and `MigrateSidecars` route sidecar reads/writes through it instead of calling `SidecarStore` directly.
- No changes to `NinjagoScanner.Web`, `NinjagoScanner.CatalogService`, the gRPC proto contracts, or the on-disk sidecar file format.
