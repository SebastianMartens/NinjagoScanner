## Context

See proposal.md for motivation. Current state: PictureService builds one `aioboto3` S3 client and DynamoDB resource in `main.py` from `resolve_aws_region()`; the VS Code launch config targets prod via `AWS_PROFILE=terraform`. Web's auth DB path is already configurable (`WebConfig.ResolveAuthDatabasePath`, `AUTH_DATABASE_PATH`). Aspire Dashboard already runs via `docker-compose.yml` (OTLP/HTTP on `localhost:18890`). Photos live under `photos/<collection_id>/<photo_id>`; sidecars are one DynamoDB item per photo ID per collection.

## Goals / Non-Goals

**Goals:**
- One command (`./dev.ps1 up`) gives a working, seeded, observable stack that cannot reach prod.
- Deterministic, committed fixture data usable by humans and by me driving the app.

**Non-Goals:**
- No fake Gemini analyzer / no test-only branches in production code.
- No change to the automated `dotnet test` / `pytest` suites (they keep their own fakes).
- No replacement of the VS Code launch configs (they stay for debugging).
- No Linux/macOS script (PowerShell only, matching the dev machine).

## Decisions

- **Moto as a compose service (`motoserver/moto`, port 5000)** over `moto_server` via uv: same lifecycle as Aspire, no extra Python process. Alternative: in-process moto, rejected (not a separate server for three processes).
- **`AWS_ENDPOINT_URL` passed as `endpoint_url=` to `session.client`/`session.resource`** in `main.py`, read via a small `resolve_aws_endpoint_url()` in `config.py` (returns `None` when unset). Explicit rather than relying on botocore's implicit support so aioboto3 behaviour is certain and testable. Pre-signed URLs then embed `localhost:5000`, which the browser can reach.
- **Seeding split by store, one set of fixed IDs** in a shared `testdata/manifest.json` (collection IDs, user names): a Python seed script (`scripts/seed_moto.py`, run with `uv run` in `picture_service`, reusing its deps) creates bucket/table and loads `testdata/photos/*` + `testdata/sidecars.json`; Web's `seed` mode creates users/friendship/trade. Alternative: write SQLite rows from Python, rejected because Identity password hashing and migrations must match Web's code.
- **Web `seed` mode** in `Program.cs`: if `args` contains `seed`, build the host, run migrations + `LocalSeeder`, then exit before `app.Run()`. Guard: refuse when the resolved DB path equals the default `Data/users.db`. `dev.ps1` sets `AUTH_DATABASE_PATH=.dev/web/users.db`.
- **`dev.ps1` process model**: `Start-Process` detached with redirected stdout/stderr to `.dev/logs/<svc>.log`, PID in `.dev/pids/<svc>`; `down` kills by PID tree; `status` checks PID liveness + port. Env set per child process, not in the caller's shell. Services started from built outputs (`dotnet run --no-build` after one `dotnet build`; `uv run python -m picture_service.main`) with PictureService on port 8090 as in the launch config.
- **Prod guard**: before anything, fail if `AWS_PROFILE`, `AWS_ACCESS_KEY_ID`, or `AWS_SESSION_TOKEN` is set in the caller's shell; children get `AWS_ACCESS_KEY_ID=test`, `AWS_SECRET_ACCESS_KEY=test`, `AWS_ENDPOINT_URL`, and `AWS_PROFILE` explicitly cleared.
- **Fixture labels** come from a one-time `scripts/label_fixtures.py` that runs the real Gemini analysis over the chosen photos and writes `testdata/sidecars.json`, which is then hand-curated (edge-case rows). Photos are copied/downscaled (~800px) by `scripts/pick_fixtures.py`. Both are dev helpers, not part of `up`.
- **Gemini**: `GEMINI_API_KEY` is passed through from the caller's shell/`picture_service/.env` if present; nothing else.

## Risks / Trade-offs

- [Moto S3 pre-signed URL host/signature mismatch] → verify in tasks that `/review` and `/gallery` images load; use path-style addressing if needed.
- [Moto DynamoDB feature gaps vs real] → keep table definition in one place mirrored from infra; note drift risk in README.
- [Fixture photos are real personal card photos committed to git] → they are card photos only (no people); pick and check manually before commit.
- [Detached Windows processes orphaned if `down` is skipped] → `up` first runs `down` logic for stale PIDs; `status` reveals leftovers.
- [Repo grows ~5–7 MB] → accepted; downscale to keep small.
