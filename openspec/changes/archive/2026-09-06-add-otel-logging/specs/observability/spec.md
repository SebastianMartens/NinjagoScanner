## ADDED Requirements

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
