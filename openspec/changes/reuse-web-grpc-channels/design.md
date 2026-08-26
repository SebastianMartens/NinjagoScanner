## Context

See proposal.md - Why. `CatalogServiceClient` and `PictureServiceClient`
are constructed once and registered as `AddSingleton` in `Program.cs`, but
each of their methods currently opens its own `using var channel =
GrpcChannel.ForAddress(...)` rather than using a channel owned by the
singleton instance. Target addresses are Fly `.internal` hostnames on the
6PN private network, resolved via Fly's internal DNS
(`ninjago-scanner-catalog-service.internal:8080`,
`ninjago-scanner-picture-service.internal:8080`); calls use plaintext HTTP/2
(h2c), enabled via the `Http2UnencryptedSupport` switch in `Program.cs`.

Depends on `add-opentelemetry-observability` for measurement, same as
`fix-web-duplicate-prerender-fetch` — this design assumes a "before"
baseline trace already exists.

## Goals / Non-Goals

**Goals:**
- Eliminate repeated gRPC connection-setup cost within and across calls to
  the same backend service.
- Keep the channel resilient to the target service's Fly machine
  restarting (new IP behind the same `.internal` DNS name).
- Prove the fix with trace evidence, not just code inspection.

**Non-Goals:**
- Client-side load balancing across multiple backend instances — each
  backend service currently runs as a single Fly machine; this isn't
  needed today.
- Changing retry/timeout policy — out of scope unless the reconnection
  decision below requires touching it.
- The duplicate-prerender-fetch fix or the observability instrumentation
  itself — separate changes.

## Decisions

### One channel per target address, owned by the singleton client, created at construction
`CatalogServiceClient` and `PictureServiceClient` construct their
`GrpcChannel` once in their constructor (they're already DI singletons) and
store it as a field, replacing every method-local
`GrpcChannel.ForAddress(...)` with a reference to that field. Alternative
considered: inject `GrpcChannel` via `AddGrpcClient()`/`IHttpClientFactory`
integration instead of constructing it manually — more idiomatic in a
typical ASP.NET Core app, but would mean restructuring how these two
classes are registered and constructed for a benefit (factory-managed
handler lifetime) this app doesn't need at its scale; a manually-owned
long-lived channel is simpler and sufficient here.

### Bound the channel's underlying connection lifetime to tolerate backend restarts
A single long-lived `GrpcChannel` normally keeps one pooled HTTP/2
connection open indefinitely once established. If the target Fly machine
(CatalogService or PictureService) restarts and comes back with a new
internal IP behind the same `.internal` DNS name, a channel holding the
old connection would keep failing until something forces it to
re-resolve. Configure the channel's `SocketsHttpHandler` with a bounded
`PooledConnectionLifetime` (e.g. 5 minutes) so the underlying connection is
periodically torn down and re-established (re-resolving DNS in the
process), rather than living forever. This is the standard mitigation
Microsoft's docs recommend for gRPC clients talking to a service that can
move behind stable DNS (originally written for Kubernetes pod restarts;
applies identically to a Fly machine restart). Alternative considered:
leave the default (no `PooledConnectionLifetime`, effectively unbounded) —
rejected because it would reintroduce exactly the kind of hard-to-diagnose
production incident this exploration is trying to prevent, just moved from
"slow" to "briefly broken after a backend deploy," and Fly deploys
(`flyctl deploy`) recreate the target machine on every deploy of
CatalogService/PictureService.
- Grpc.Net.Client surfaces transient connection failures as RPC exceptions
  on the in-flight call; a bounded `PooledConnectionLifetime` bounds how
  long that window of failure can last after a backend restart, it doesn't
  eliminate a brief failure window entirely. Retry policy is out of scope
  for this change (Non-Goals) — if this window proves to matter in
  practice, that's a follow-up.

### Apply the same pattern to both clients
`CatalogServiceClient` and `PictureServiceClient` have the same structural
issue and the same fix applies to both identically.

## Risks / Trade-offs

- **[Risk]** A call in flight when the target service's Fly machine
  restarts (e.g. during a deploy) fails instead of transparently retrying
  → **Mitigation**: this is no worse than today (today's per-call channel
  would fail identically mid-flight); out of scope to add retry logic here,
  but worth noting as a natural follow-up once this change's tracing shows
  whether it's a real-world problem.
- **[Risk]** A long-lived channel could mask a slow/unhealthy backend
  connection that a fresh channel per call would have naturally avoided
  (by reconnecting) → **Mitigation**: the bounded `PooledConnectionLifetime`
  addresses this by forcing periodic reconnection regardless.

## Migration Plan

- Change `CatalogServiceClient` and `PictureServiceClient` to own a shared
  channel, one at a time or together — low risk, no data/state involved.
- Before/after comparison: capture traces before and after (using tracing
  from `add-opentelemetry-observability`) for a page that fires multiple
  backend calls (e.g. `/review`) and confirm repeated connection-setup
  spans are gone.
- Rollback: revert to per-call channel creation — no state to unwind.
