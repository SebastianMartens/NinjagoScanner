## 1. PictureService endpoint override

- [x] 1.1 Add `resolve_aws_endpoint_url()` to `picture_service/config.py` and unit-test set/unset cases in `tests/test_config.py` (`uv run pytest tests/test_config.py` passes)
- [x] 1.2 Pass the endpoint to `session.client("s3")` and `session.resource("dynamodb")` in `main.py`; verify `uv run pytest` passes and behaviour is unchanged when unset

## 2. Web seed mode

- [x] 2.1 Add `LocalSeeder` creating fixed users (`alice`/`bob`, known password), collections with fixed IDs, a friendship and a pending trade; verify with a test using a temp SQLite file in `NinjagoScanner.Web.Tests` (`dotnet test` passes, including idempotent re-run)
- [x] 2.2 Handle the `seed` argument in `Program.cs` (migrate, seed, exit before `app.Run()`) and refuse the default `Data/users.db` path; verify with a manual run and a test for the refusal

## 3. Fixtures

- [x] 3.1 Add `scripts/pick_fixtures.py` to copy/downscale ~20–30 chosen photos from `cardFotos` into `testdata/photos/`; verify files exist and total size is under ~8 MB
- [x] 3.2 Add `scripts/label_fixtures.py` (real Gemini run) producing `testdata/sidecars.json`; run once and verify entries exist for every photo
- [x] 3.3 Curate `sidecars.json` with edge-case rows (failed, uncertain, incorrect, unmapped, duplicate, unreviewed) and write `testdata/manifest.json` with fixed collection IDs/usernames; verify by reading the files

## 4. Moto seeding and compose

- [x] 4.1 Add `motoserver/moto` (port 5000) to `docker-compose.yml`; verify `docker compose up -d` makes `http://localhost:5000` respond
- [x] 4.2 Add `scripts/seed_moto.py` creating the bucket and sidecar table and loading fixtures; verify via boto3 listing against moto and that re-running is idempotent

## 5. dev.ps1

- [x] 5.1 Implement `up` with the prod-safety guard (fails when `AWS_PROFILE`/AWS credentials set), compose start, seeding of both stores, and detached service launch with logs/PIDs under `.dev/`; verify all three ports respond
- [x] 5.2 Implement `down`, `status`, `logs <service>` and `reset`; verify each, including stale-PID handling
- [x] 5.3 Set `OTEL_EXPORTER_OTLP_ENDPOINT`, `AWS_ENDPOINT_URL` and dummy credentials per child process; verify traces appear in the Aspire Dashboard and no AWS_PROFILE leaks to children
- [x] 5.4 Add `.dev/` to `.gitignore`

## 6. End-to-end verification and docs

- [x] 6.1 Log in as seeded user, verify overview, `/gallery` and `/review` images load via pre-signed moto URLs, friends and pending trade appear, and a cross-service trace shows in Aspire
- [x] 6.2 Verify upload without `GEMINI_API_KEY` fails with a clear message and with a key analyzes successfully
- [x] 6.3 Update CLAUDE.md, README and `local-observability/README.md` with the `dev.ps1` workflow; verify the commands documented match the script
