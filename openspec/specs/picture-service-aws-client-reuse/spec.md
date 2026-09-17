# picture-service-aws-client-reuse Specification

## Purpose

Ensures PictureService reuses one long-lived AWS SDK client per backing store (S3, DynamoDB) instead of paying client/connection setup cost on every call, so a listing request touching many photos doesn't pay that cost once per photo.

## Requirements

### Requirement: One client per backing store, reused across calls

PictureService SHALL maintain exactly one long-lived AWS SDK client per backing store (the S3 photo bucket, the DynamoDB sidecar table) for the lifetime of the process, and reuse it for every call to that store, rather than establishing a new client per call.

#### Scenario: Listing many photos in one request

- **WHEN** a single `ListCards` request generates download URLs for many photos in the same collection
- **THEN** those S3 operations share the same underlying client rather than each establishing a new one

#### Scenario: Verifying via a trace

- **WHEN** a person loads a page that triggers a `ListCards` call while distributed tracing is active
- **THEN** the resulting trace does not show repeated client/connection-setup spans for calls to the same backing store within that request

### Requirement: Behavior unchanged for callers

Reusing a client SHALL NOT change the observable behavior, return values, or error handling of any existing PictureService RPC.

#### Scenario: Existing RPC behaves identically

- **WHEN** any caller invokes an existing `CardPictureService` RPC
- **THEN** it receives the same result (success or error) it would have received before this change, only without the per-call client setup cost
