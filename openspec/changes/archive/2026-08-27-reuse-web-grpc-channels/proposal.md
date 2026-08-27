## Why

`NinjagoScanner.Web`'s `CatalogServiceClient` and `PictureServiceClient`
each open a brand-new `GrpcChannel` inside every single method, rather than
sharing one channel per target service. Every call — including calls fired
sequentially by the same page load — pays fresh HTTP/2 connection setup to
a Fly `.internal` hostname. This morning's N+1 fix (commit `b59d2ec`)
already reduced *call count* for download URLs, but each remaining call
still pays its own connection-setup cost, contributing to reports that
"most pages are slow."

## What Changes

- `CatalogServiceClient` and `PictureServiceClient` each create one
  long-lived `GrpcChannel` per target service address (at construction,
  since both are already registered as DI singletons in `Program.cs`) and
  reuse it across all calls, instead of opening a new channel per call.
- Method signatures and behavior are unchanged — this is purely an
  internal connection-management change.
- Use `add-opentelemetry-observability` (must land first) to capture a
  "before" trace showing repeated channel-setup cost per page load and an
  "after" trace showing it gone, as evidence the fix works.

## Capabilities

### New Capabilities
- `web-grpc-client-connection-reuse`: the requirement that
  `NinjagoScanner.Web`'s gRPC service clients reuse a long-lived channel
  per target service rather than opening a new connection per call.

### Modified Capabilities
(none — no externally observable behavior changes; existing capability
specs describing what each page does or shows are unaffected)

## Impact

- **Affected code**: `NinjagoScanner.Web/Services/CatalogServiceClient.cs`,
  `NinjagoScanner.Web/Services/PictureServiceClient.cs`.
- **Depends on**: `add-opentelemetry-observability` — this change's
  validation approach requires tracing to exist first; do not implement
  before that change is deployed.
- **No breaking change**: internal connection management only, same public
  method signatures and behavior.
