## Why

Testing the full stack locally is hard today: PictureService's local launch config points at the **production** S3 bucket and DynamoDB table (via `AWS_PROFILE=terraform`), the services must be started and wired by hand, observability needs a manual `OTEL_EXPORTER_OTLP_ENDPOINT`, and there is no known, repeatable data set. We want one command that brings up the whole system against disposable local AWS (moto), seeded with deterministic data, with traces visible in the Aspire Dashboard, and with a hard guarantee it cannot touch prod.

## What Changes

- Add a `motoserver/moto` service to `docker-compose.yml` next to the Aspire Dashboard (S3 + DynamoDB).
- Add `dev.ps1` with `up` / `down` / `status` / `logs <service>` / `reset`: starts compose, seeds, then launches CatalogService, picture_service and Web as detached background processes (logs in `.dev/logs`, PIDs in `.dev/pids`); sets `AWS_ENDPOINT_URL`, dummy credentials and `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:18890` automatically.
- `dev.ps1` refuses to start if `AWS_PROFILE` (or real AWS credentials) is set in the shell, so the local stack can never reach prod S3/DynamoDB.
- picture_service honours an optional `AWS_ENDPOINT_URL` when creating S3 and DynamoDB clients (unset = current behaviour).
- Web gains a `dotnet run -- seed` mode that migrates and seeds a **separate local SQLite file** (never `NinjagoScanner.Web/Data/users.db`) with fixed users (e.g. `alice`/`bob`, known password), a friendship and a pending trade, using fixed collection IDs.
- A moto seed step creates the bucket and table and loads fixtures for the same collection IDs.
- Committed `testdata/`: ~20–30 real card photos (optionally downscaled to ~800px) plus curated `sidecars.json` (labels from a one-time real Gemini run via a helper under `scripts/`, plus hand-edited edge cases: failed, uncertain, incorrect, unmapped, duplicate, unreviewed).
- Gemini at runtime stays real and opt-in via `GEMINI_API_KEY`; there is **no fake analyzer** and no test-only branch in production code. Without a key, analysis fails with a clear message.
- Update CLAUDE.md / README / `local-observability/README.md` with the new workflow.

## Capabilities

### New Capabilities
- `local-dev-environment`: Cross-cutting one-command local stack (compose, `dev.ps1` lifecycle, seeding, fixtures, observability wiring, prod-safety guard).
- `picture-service-aws-endpoint-override`: picture_service uses an optional `AWS_ENDPOINT_URL` for its S3 and DynamoDB clients.
- `web-local-seed-mode`: Web `seed` command that creates deterministic users, friendship and trade data in a configurable local database.

### Modified Capabilities
<!-- None: existing requirements are unchanged; endpoint override defaults to off. -->

## Impact

- `docker-compose.yml`, new `dev.ps1`, new `scripts/` helpers, new `testdata/` (binary fixtures, ~5–7 MB), `.gitignore` (`.dev/`, local DB).
- `picture_service/src/picture_service/main.py` (and `config.py`): client creation with optional endpoint URL.
- `NinjagoScanner.Web/Program.cs` (+ a seeder class): `seed` argument handling; DB path already configurable via `WebConfig.ResolveAuthDatabasePath`.
- Docs: CLAUDE.md, README, local-observability README.
- No gRPC/proto changes; no production behaviour change when `AWS_ENDPOINT_URL` is unset.
