# observability Specification

## Purpose

Gives every user-facing request an end-to-end, cross-service trace and
service-level performance metrics, so page slowness can be attributed to a
specific hop (Blazor rendering, a gRPC call, a DynamoDB/S3 call) instead of
guessed at from plain text logs.

## Requirements

### Requirement: End-to-end request tracing
The system SHALL produce a single distributed trace correlating all work
done across `NinjagoScanner.Web`, `NinjagoScanner.CatalogService`, and
`NinjagoScanner.PictureService` in service of one user-facing request,
whenever that request causes calls into more than one of those services.

#### Scenario: Page load spanning two services
- **WHEN** a person loads a page in `NinjagoScanner.Web` that calls
  `NinjagoScanner.PictureService` over gRPC to satisfy the request
- **THEN** the resulting trace contains spans from both
  `NinjagoScanner.Web` and `NinjagoScanner.PictureService`, correlated
  under one trace ID

#### Scenario: Page load spanning all three services
- **WHEN** a person loads a page in `NinjagoScanner.Web` that calls both
  `NinjagoScanner.CatalogService` and `NinjagoScanner.PictureService` to
  satisfy the request
- **THEN** the resulting trace contains spans from all three services,
  correlated under one trace ID, showing their relative timing

### Requirement: Per-hop span coverage
The system SHALL emit a span for each inbound gRPC/HTTP request handled by
a service, each outbound gRPC call made by `NinjagoScanner.Web`'s service
clients, and each outbound HTTP call `NinjagoScanner.PictureService` makes
to AWS (S3, DynamoDB), each with enough detail (operation name, duration,
success/failure) to identify where time was spent.

#### Scenario: Slow AWS call is visible in a trace
- **WHEN** `NinjagoScanner.PictureService` makes a call to DynamoDB or S3
  while handling a request
- **THEN** the trace for that request includes a span showing that call's
  duration, distinguishable from the gRPC handling time around it

#### Scenario: gRPC client call overhead is visible
- **WHEN** `NinjagoScanner.Web` makes an outbound gRPC call to
  `NinjagoScanner.CatalogService` or `NinjagoScanner.PictureService`
- **THEN** the trace includes a span for that outbound call, so its
  duration can be compared against the corresponding inbound span recorded
  by the receiving service

### Requirement: Service-level performance metrics
Each of the three services SHALL emit request-duration metrics for the
gRPC/HTTP endpoints it hosts, queryable independently of any single trace,
so that trends (e.g. a page's typical vs. worst-case load time) can be
observed over time rather than only per-request.

#### Scenario: Metrics available without a specific trace
- **WHEN** an operator wants to know a service's request-duration
  distribution over the last hour
- **THEN** that information is available from exported metrics without
  needing to locate any individual trace

### Requirement: Non-blocking telemetry export
Telemetry export failures or unavailability of the telemetry backend SHALL
NOT cause a user-facing request to fail or be materially delayed.

#### Scenario: Telemetry backend unreachable
- **WHEN** the configured OTLP endpoint is unreachable from a service
- **THEN** requests handled by that service still complete normally,
  with telemetry for that period simply not delivered

### Requirement: Externally configured telemetry export
Each service SHALL read its telemetry export destination and credentials
from configuration (environment variables / secrets), not from values
hardcoded in source, so the destination can differ between environments
without a code change.

#### Scenario: Export destination configured per deployment
- **WHEN** a service starts with its OTLP endpoint and authentication
  configured via environment variables
- **THEN** it exports telemetry to that configured destination without
  requiring a code change or rebuild

### Requirement: Log export to observability backend
Each of the three services SHALL export logs emitted through `ILogger`
(at the levels the service already logs at) to the same OTLP-based
observability backend used for traces and metrics, so log output is
queryable in that backend rather than only in per-machine stdout capture.

#### Scenario: Log line reaches the observability backend
- **WHEN** a service logs a message via `ILogger` at a level the service's
  logging configuration allows through
- **THEN** that log entry is exported via OTLP to the configured
  observability backend, in addition to remaining visible via the
  platform's existing stdout log capture

#### Scenario: Log export destination configured per deployment
- **WHEN** a service starts with its OTLP endpoint and authentication
  configured via environment variables
- **THEN** it exports logs to that configured destination without
  requiring a code change or rebuild, using the same configuration source
  as its trace and metric export

### Requirement: Log correlation with trace context
When a log is emitted while a distributed trace is active for the request
being handled, the exported log entry SHALL carry that trace's identifiers
so the log can be located from the trace and vice versa.

#### Scenario: Log emitted during a traced request
- **WHEN** a service logs a message via `ILogger` while handling a request
  that is part of an active distributed trace
- **THEN** the exported log entry includes that trace's trace ID and span
  ID, so it can be found starting from the corresponding trace view

#### Scenario: Log emitted outside any active trace
- **WHEN** a service logs a message via `ILogger` with no active trace
  (e.g. during startup, before any request is being handled)
- **THEN** the log entry is still exported to the observability backend,
  simply without trace/span identifiers attached
