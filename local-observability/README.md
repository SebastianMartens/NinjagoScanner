# Local observability (Aspire Dashboard)

Runs the [.NET Aspire Dashboard](https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard/standalone)
in a container so traces/metrics/logs from a locally-running service can be inspected in a
browser, without touching Grafana Cloud (the hosted services' OTLP destination - see
`infra/README.md` and each project's `fly.toml`).

This is purely a local dev convenience. It isn't referenced by any deployed environment.

## Usage

```powershell
docker compose up -d       # from the repo root
```

Open http://localhost:18888.

Then point whichever service(s) you're running locally at it instead of Grafana Cloud, in the
same terminal you `dotnet run` / `uv run` from:

```powershell
$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:18890"
Remove-Item Env:\OTEL_EXPORTER_OTLP_HEADERS -ErrorAction SilentlyContinue   # Grafana Cloud's API key header isn't needed locally
```

All three services already export via OTLP/HTTP-protobuf (not gRPC - see the comment in each
`Program.cs` / `picture_service/main.py`), which is why the dashboard container is configured to
listen on the HTTP endpoint (18890), not just its default gRPC one (18889).

If both env vars are left unset, the exporters fall back to `http://localhost:4318` and just fail
quietly - harmless, but nothing shows up here either. Unsetting them again (or closing the
terminal) reverts to no local export; the Fly-deployed services are unaffected either way, since
they get their own `OTEL_EXPORTER_OTLP_ENDPOINT`/`HEADERS` from Fly secrets, not from your shell.

To stop it: `docker compose down` (from the repo root).
