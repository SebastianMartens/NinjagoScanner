## Purpose

Allows PictureService to talk to an S3/DynamoDB-compatible emulator for local development without changing behavior in deployed environments.

## ADDED Requirements

### Requirement: Optional AWS endpoint override
PictureService SHALL use the URL in the `AWS_ENDPOINT_URL` environment variable as the endpoint for its S3 and DynamoDB clients when set, and SHALL use the default AWS endpoints when it is unset.

#### Scenario: Endpoint set
- **WHEN** `AWS_ENDPOINT_URL=http://localhost:5000` is set at startup
- **THEN** photo storage and sidecar operations go to that endpoint

#### Scenario: Endpoint unset
- **WHEN** `AWS_ENDPOINT_URL` is not set
- **THEN** behavior is unchanged from the existing configuration

#### Scenario: Pre-signed URLs reach the emulator
- **WHEN** the endpoint is set and a download URL is requested
- **THEN** the returned pre-signed URL is fetchable by the browser (host reachable from the host machine)
