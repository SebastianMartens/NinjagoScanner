## Purpose

Ensures `NinjagoScanner.Web`'s gRPC clients reuse a long-lived connection
per target service instead of paying connection-setup cost on every call,
so a page load that fires several backend calls doesn't pay that cost
repeatedly.

## ADDED Requirements

### Requirement: One channel per target service, reused across calls
`NinjagoScanner.Web` SHALL maintain exactly one long-lived gRPC channel per
target service (`NinjagoScanner.CatalogService`,
`NinjagoScanner.PictureService`) and reuse it for every call to that
service, rather than establishing a new channel per call.

#### Scenario: Multiple calls in one page load
- **WHEN** a single page load in `NinjagoScanner.Web` makes more than one
  call to the same backend service (e.g. listing cards, then fetching
  download URLs)
- **THEN** those calls share the same underlying gRPC channel rather than
  each establishing a new connection

#### Scenario: Verifying via a trace
- **WHEN** a person loads a page that makes multiple backend calls while
  distributed tracing is active
- **THEN** the resulting trace does not show repeated connection-setup
  spans for calls to the same target service within that page load

### Requirement: Behavior unchanged for callers
Reusing a channel SHALL NOT change the observable behavior, return values,
or error handling of any existing `CatalogServiceClient` or
`PictureServiceClient` method.

#### Scenario: Existing call site behaves identically
- **WHEN** any page or component calls an existing method on
  `CatalogServiceClient` or `PictureServiceClient`
- **THEN** it receives the same result (success or error) it would have
  received before this change, only without the per-call connection setup
