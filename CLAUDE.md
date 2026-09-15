# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

Root solution is `NinjagoScanner.slnx` (not a `.sln`).

```powershell
# Build everything (2 services + 2 test projects)
dotnet build NinjagoScanner.slnx

# Run all tests
dotnet test NinjagoScanner.slnx

# Run a single test project
dotnet test NinjagoScanner.Web.Tests

# Run a single test by name (any test project)
dotnet test NinjagoScanner.Web.Tests --filter "FullyQualifiedName~CollectionQueryServiceGalleryTests"

# Run one service (each is independently runnable; cwd into its folder first)
Set-Location NinjagoScanner.CatalogService; dotnet run   # http://localhost:5073
Set-Location NinjagoScanner.Web; dotnet run
```

PictureService is Python (`picture_service/`, `uv`-managed, outside `NinjagoScanner.slnx`) — ported from a C# implementation (`NinjagoScanner.PictureService/`, removed after cutover) per `openspec/changes/picture-service-python-rewrite/`.

```powershell
# picture_service/ (Python) - build/test are separate from the dotnet commands above
Set-Location picture_service
uv sync                              # install dependencies
uv run python scripts/gen_proto.py   # generate gRPC stubs (not committed - do this before running/testing)
uv run pytest                        # run tests
uv run python -m picture_service.main   # run the service - http://localhost:8080 (PORT env var)
```

For the Web app to have full functionality (Gemini scan, catalog data), `CatalogService` and `picture_service` must also be running. VS Code has a `Launch All (CatalogService + PictureService + Web)` compound launch config (PictureService via the Python launch entry — requires the `ms-python.debugpy` extension), and a `build`/`watch`/`publish` task set, in `.vscode/`.

If `dotnet build` fails on the Web project with a locked `NinjagoScanner.Web.exe`, an instance of the app is still running — stop it first.

Gemini API key for PictureService (required for scanning): env vars only (`GEMINI_API_KEY`, `GEMINI_MODEL`) — see `picture_service/src/picture_service/config.py`. Default model is `gemini-2.5-flash`.

.NET tests use xunit; `dotnet test NinjagoScanner.slnx` covers CatalogService and Web. `NinjagoScanner.Web.Tests` project-references the CatalogService and Web app projects and spins up in-process test hosts for both (`Fixtures/CatalogServiceTestHost.cs`, `Fixtures/PictureServiceTestHost.cs`) rather than mocking the gRPC calls — `PictureServiceTestHost` is a hand-written fake implementing the generated `CardPictureServiceBase` directly, decoupled from the real PictureService implementation (the `.proto` file, at `NinjagoScanner.Web/Protos/picture_service.proto`, is the only shared contract). `picture_service/`'s own `uv run pytest` suite is entirely separate and not part of `dotnet test`.

## Local observability

All three services export OpenTelemetry traces/metrics/logs via OTLP/HTTP-protobuf; when deployed
they point at Grafana Cloud (`OTEL_EXPORTER_OTLP_ENDPOINT`/`HEADERS` set as Fly secrets). To view
them locally instead, without touching Grafana, run `docker compose up -d` (repo root) to start
the Aspire Dashboard, then set `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:18890` in the
terminal you run a service from — see `local-observability/README.md`.

## Architecture

Three independently runnable services, communicating over gRPC (`Protos/*.proto`). CatalogService and Web are .NET 10, in one solution (`NinjagoScanner.slnx`), compiled via `Grpc.Tools`. PictureService is Python (`picture_service/`, `grpc.aio`, `uv`-managed, outside the .NET solution) — ported from a C# implementation (`NinjagoScanner.PictureService/`, removed after cutover; see `openspec/changes/picture-service-python-rewrite/`). Neither Web nor CatalogService needed any code change for the port — the `.proto` file is the only contract between PictureService and the rest of the system.

- **NinjagoScanner.CatalogService** — owns the reference catalog data (`Series`, `Category`, `Card`, `Series Metadata`) loaded from `cardInfos/*.json` inside the service project (copied to output on build). Doesn't know about photos or scanning. Exposes `CardCatalog` gRPC service: `ListSeries`, `GetSeries`, `ListAllCards`, `GetSeriesMetadata`, `GetServiceInfo`.
- **PictureService** (`picture_service/`, Python) — manages card photos and their sidecars: runs Gemini-based AI analysis via `langchain-google-genai` (`gemini_service.py`), reads/writes photo bytes in an S3 bucket keyed by a generated photo ID (`photo_store.py`, via `aioboto3`), reads/writes sidecar records in a DynamoDB table (`sidecar_table.py`, cached in `sidecar_cache.py`), and applies manual sidecar edits. Consults CatalogService via its own gRPC client (`catalog_client.py`) for series/card matching — never reads `cardInfos` locally. Exposes `CardPictureService` gRPC service (`picture_scanner_service.py`): `Scan`, `UploadPhoto` (client-streaming; assigns the photo ID, stores the bytes, triggers AI analysis), `GetPhotoDownloadUrl` (short-lived pre-signed S3 GET URL), `ListCards`, `UpdateSidecar`/`UpdateSetName`/`UpdateCardNumber`/`UpdateCardLanguage`/`UpdateReviewStatus`, `MigrateSidecars`, `DeletePhoto`. The only service that ever holds AWS credentials.
- **NinjagoScanner.Web** — the Blazor Server app (Interactive Server render mode) people actually use: card tiles (`/`, Overview), full list/filter/detail (`/collection`), gallery (`/gallery`), mobile photo upload (`/upload`), photo review (`/review`), and an about page (`/about`). Talks to both other services over gRPC directly from server-rendered page code, through three single-purpose classes in `Services/`: `CatalogServiceClient.cs` (CatalogService only), `PictureServiceClient.cs` (PictureService only — upload, sidecar/review CRUD, download URLs), and `CollectionQueryService.cs` (matches/groups/sorts photo data against catalog data for the overview, gallery, series summary, card details, and review pages, composed from the other two clients rather than calling either gRPC service itself). Never touches `cardInfos`, sidecar records, or AWS directly — photo bytes and download URLs both come from PictureService. Also owns the canonical copy of `picture_service.proto` (`NinjagoScanner.Web/Protos/`), which `picture_service/`'s proto codegen reads from directly.

Data flow: photo bytes live in an S3 bucket, keyed by a generated photo ID (`photos/<collection_id>/<photo_id>`); sidecar records live in a DynamoDB table, one item per photo ID within a collection (see `photo_store.py`/`sidecar_table.py`). A card photo is "owned" by a catalog card when its sidecar's `SeriesName` + `CardNumber` match a catalog entry (see Owned Copies / Unmapped Photo in the glossary). Analysis Status (`pending`/`ok`/`uncertain`/`failed`) is machine-set by AI Analysis; Review Status (`unreviewed`/`verified`/`incorrect`) is a separate, human-only judgment — nothing sets both.

Service addresses are resolved through a layered config: explicit config key/env var → default (see `WebConfig.cs` in `NinjagoScanner.Web`, `BffConfig`'s former counterpart). Default addresses: CatalogService `http://localhost:5073`, PictureService `http://localhost:8080` (`PORT` env var). PictureService's S3 bucket/DynamoDB table names and AWS region are resolved the same way (`resolve_photos_bucket_name`/`resolve_sidecar_table_name`/`resolve_aws_region` in `config.py`) — no local-filesystem fallback; all must be configured via env vars (`PHOTOS_BUCKET_NAME`, `SIDECAR_TABLE_NAME`, `AWS_REGION`). Note `aioboto3.Session()` doesn't read `AWS_REGION` on its own (botocore only checks `AWS_DEFAULT_REGION` by default) — `main.py` passes `resolve_aws_region()` in explicitly as `region_name`.

Internal modules/classes in PictureService (e.g. `photo_store.py`, `sidecar_table.py`, `sidecar_cache.py`) aren't exposed as a public API — Python has no `InternalsVisibleTo` equivalent, so this is convention (leading-underscore submodules where relevant) rather than an enforced boundary.

Hosting: all three services run as Fly.io apps in one Fly organization, connected over Fly's private network (6PN / `*.internal` DNS) — only `NinjagoScanner.Web` gets a public Fly IP. See each project's `fly.toml` and `infra/README.md`.

## Domain vocabulary & spec workflow

This repo uses **OpenSpec** for spec-driven changes (`openspec/` — `specs/`, `changes/`, `GLOSSARY.md`, `config.yaml`). Specs live in one shared `openspec/specs/` directory but capability names are prefixed by owning project (`catalog-service-*`, `picture-service-*`, `web-*`); unprefixed only for genuinely cross-cutting specs like the gRPC contracts. Use the `openspec-*` skills/commands for proposing, applying, and archiving changes rather than editing specs by hand.

`openspec/GLOSSARY.md` defines the project's ubiquitous language (Series, Card, Sidecar, Analysis Status, Review Status, Owned Copies, Unmapped Photo, etc.) — read it before naming new concepts in code, UI, or specs, and keep usage consistent with it.
