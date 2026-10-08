# local-dev-environment Specification

## Purpose

Provides a one-command local stack (all services, disposable local AWS, observability, deterministic data) so the full system can be tested without touching production resources.

## Requirements

### Requirement: One-command lifecycle
The system SHALL provide a `dev.ps1` script with `up`, `down`, `status`, `logs <service>` and `reset` commands. `up` SHALL start the local AWS emulator and the Aspire Dashboard, seed data, and launch CatalogService, PictureService and Web as detached background processes writing logs to `.dev/logs` and PIDs to `.dev/pids`.

#### Scenario: Starting the stack
- **WHEN** a developer runs `./dev.ps1 up` on a clean checkout with Docker running
- **THEN** all three services are running and reachable on their default addresses, the emulator and dashboard containers are up, and seeded data is visible in the Web app

#### Scenario: Stopping the stack
- **WHEN** a developer runs `./dev.ps1 down`
- **THEN** the three service processes are terminated and the containers are stopped

#### Scenario: Resetting data
- **WHEN** a developer runs `./dev.ps1 reset`
- **THEN** the emulated S3 and DynamoDB data and the local Web database are wiped and reseeded to the fixture state

### Requirement: Production safety guard
The system SHALL refuse to start the local stack when `AWS_PROFILE` or real AWS credentials are present in the invoking shell, and SHALL point all AWS clients at the local emulator with dummy credentials.

#### Scenario: AWS profile set
- **WHEN** `AWS_PROFILE` is set in the shell and the developer runs `./dev.ps1 up`
- **THEN** the script exits with a non-zero status and an explanatory message, and starts nothing

### Requirement: Automatic observability
The system SHALL configure every locally started service to export telemetry to the local Aspire Dashboard without manual environment setup.

#### Scenario: Traces visible
- **WHEN** a page is loaded in the locally running Web app
- **THEN** a trace spanning the involved services is visible in the Aspire Dashboard at `http://localhost:18888`

### Requirement: Deterministic fixture data
The system SHALL seed the emulated S3 and DynamoDB with committed fixture photos and sidecars, including edge-case states (failed, uncertain, incorrect, unmapped, duplicate, unreviewed), under fixed collection IDs matching the seeded Web users.

#### Scenario: Seeded collection browsable
- **WHEN** a developer logs in as a seeded user after `up`
- **THEN** that user's collection shows the fixture photos with their sidecar states, and photo images load

### Requirement: Gemini is opt-in
The local stack SHALL use the real Gemini API only when `GEMINI_API_KEY` is provided; it SHALL NOT substitute a fake analyzer.

#### Scenario: Upload without a key
- **WHEN** a photo is uploaded locally and no `GEMINI_API_KEY` is set
- **THEN** analysis fails with a clear message and seeded data remains usable
