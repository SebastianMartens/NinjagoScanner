## Context

See proposal.md - Why for motivation.

Today, `PictureScannerGrpcService` (registered by `AddGrpc()`/`MapGrpcService`, so a new instance per RPC call) calls `SidecarStore`'s static file-I/O methods directly: `ReadRecordAsync` in `ListCards`, and `WriteAsync`/`WriteRecordAsync` in `Scan`, `UpdateSidecar`, `UpdateSetName`, `UpdateReviewStatus`, and `MigrateSidecars`. There is no shared state between calls today, so every `ListCards` call re-reads every sidecar from disk regardless of whether anything changed.

`NinjagoScanner.Web`'s `CardCatalogService` is the sole consumer of these RPCs and is itself the only writer of sidecar data in this system (aside from `Scan`, which is also invoked through this same service) - no other process modifies sidecar files.

## Goals / Non-Goals

**Goals:**
- Eliminate repeated disk reads/parses of unchanged sidecars across calls within a `PictureService` process's lifetime.
- Guarantee the cache never returns data older than the app's own most recent write to that sidecar.
- Keep `SidecarStore` as pure file I/O; caching is an additive layer in front of it, not mixed into it.

**Non-Goals:**
- Parallelizing or otherwise speeding up the first (cold-start) population of the cache - the first `ListCards` call after a process start still reads all sidecars from disk sequentially, as today.
- Caching "this image has no sidecar file yet" - that check (`File.Exists`) stays exactly as it is; the cache only holds sidecars that have actually been read or written.
- Detecting sidecar files modified outside this application (there are none - the app is the sole writer).
- Any persistence or cross-process sharing of the cache - it is purely in-memory, per `PictureService` process.

## Decisions

**1. Cache shape: `ConcurrentDictionary<string, SidecarRecord?>` keyed by the resolved absolute sidecar file path.**
`PictureScannerGrpcService` instances are created per-call and may run concurrently, so the cache itself must be a thread-safe singleton. `ConcurrentDictionary` gives per-key thread safety without hand-rolled locking. Keying by the full resolved path (not just the file name) keeps entries correctly separated across different `card_photos_directory` overrides, which every relevant RPC already accepts per-request.

*Alternative considered:* ASP.NET Core's `IMemoryCache`. Rejected - it brings TTL/eviction machinery this doesn't need (entries should live for the whole process lifetime, no expiration), for no benefit over a plain dictionary. Since sidecars are expected to be replaced by a real database later, keeping this layer as small as possible matters more than reaching for a more "official" caching abstraction.

**2. A new singleton `SidecarCache` class sits between `PictureScannerGrpcService` and `SidecarStore`, rather than adding caching logic inside `SidecarStore`.**
This keeps `SidecarStore` as pure, easily-testable file I/O. `SidecarCache` owns the caching policy (populate-on-miss for reads, write-through for writes) and is the one place that would need to change or be deleted when sidecars move to a database. Registered with `builder.Services.AddSingleton<SidecarCache>()` in `PictureService`'s `Program.cs` and injected into `PictureScannerGrpcService`'s constructor - this works even though `PictureScannerGrpcService` itself is scoped per call, since DI resolves the singleton once and shares it across every call.

**3. Write-through, not invalidate-then-refetch.**
Every write RPC already has the fully-formed new record in hand immediately after persisting it (`SidecarStore.WriteAsync`/`WriteRecordAsync` take the record as a parameter). Setting the cache entry directly to that value is one dictionary write with no extra disk I/O, versus invalidating and paying for a disk read on the next `ListCards` call. It also means there's a single place - the write path - that ever changes what a cache entry holds, with no separate invalidation logic that could drift out of sync.

**4. Read failures (corrupt/unparseable sidecar JSON) are not cached.**
`SidecarCache`'s read-through path only stores successfully parsed records. A sidecar that fails to parse is retried from disk on every subsequent read, same as today's behavior - it is not "poisoned" into a permanently cached failure state that could only be cleared by a write. This trades a small amount of repeated work in a rare edge case (corrupt files) for simplicity and avoids ever serving a stale failure after the file is fixed.

## Risks / Trade-offs

- **[Risk]** Two overlapping requests writing the same sidecar concurrently is a pre-existing read-modify-write race (not introduced or worsened by this change) → **Mitigation**: none added here, out of scope per explicit decision; whichever write finishes last also wins the cache write, so the cache stays consistent with whatever ends up on disk.
- **[Risk]** A persistently corrupt sidecar causes a wasted disk read/parse attempt on every `ListCards` call, forever → **Mitigation**: matches current behavior exactly (already re-read and re-parsed every call today); this change doesn't make it worse, it just doesn't speed up an already-rare edge case.
- **[Risk]** Memory growth from caching every sidecar's parsed content for the process lifetime → **Mitigation**: ~3,800 small JSON records is a few MB at most, negligible next to the 1.8 GB of photos already on disk; no eviction needed.

## Migration Plan

Purely additive, in-process change with no data migration and no external contract change. Deploy by restarting the `PictureService` process; rollback is reverting the code change and restarting again.
