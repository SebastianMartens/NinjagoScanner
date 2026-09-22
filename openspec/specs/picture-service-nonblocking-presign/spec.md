# picture-service-nonblocking-presign Specification

## Purpose

Ensures PictureService resolves pre-signed S3 download URLs without blocking its single asyncio event loop, so a request resolving many URLs at once does not stall every other concurrent request on the same process.

## Requirements

### Requirement: Presigning does not block concurrent requests

Resolving a pre-signed download URL SHALL NOT block PictureService's event loop for the duration of the underlying signing work, so other concurrent RPCs continue to be serviced while one request resolves many download URLs at once.

#### Scenario: A concurrent request is not delayed by a large batch of presigning

- **WHEN** one request is resolving download URLs for many photos in a collection
- **AND** a concurrent, unrelated RPC (e.g. `GetPhotoDownloadUrl` for a different photo, or `ListCards` for a different collection) arrives while that resolution is still in progress
- **THEN** the concurrent RPC completes without waiting for the first request's URL resolution to finish

### Requirement: Behavior unchanged for callers

Making presigning non-blocking SHALL NOT change the observable behavior, return values, or error handling of any existing PictureService RPC that resolves a download URL.

#### Scenario: A resolved URL is unchanged

- **WHEN** any caller resolves a photo's download URL, whether via `GetPhotoDownloadUrl` or as part of `ListCards`
- **THEN** it receives the same pre-signed URL (and the same error behavior for a missing photo) it would have received before this change, only without blocking other concurrent requests while it is produced
