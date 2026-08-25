# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

Root solution is `NinjagoScanner.slnx` (not a `.sln`).

```powershell
# Build everything (all 3 services + 3 test projects)
dotnet build NinjagoScanner.slnx

# Run all tests
dotnet test NinjagoScanner.slnx

# Run a single test project
dotnet test NinjagoScanner.Web.Tests

# Run a single test by name (any test project)
dotnet test NinjagoScanner.Web.Tests --filter "FullyQualifiedName~CardCatalogServiceGalleryTests"

# Run one service (each is independently runnable; cwd into its folder first)
Set-Location NinjagoScanner.CatalogService; dotnet run   # http://localhost:5073
Set-Location NinjagoScanner.PictureService; dotnet run   # http://localhost:5169
Set-Location NinjagoScanner.Web; dotnet run
```

For the Web app to have full functionality (Gemini scan, catalog data), `CatalogService` and `PictureService` must also be running. VS Code has a `Launch All (CatalogService + PictureService + Web)` compound launch config, and a `build`/`watch`/`publish` task set, in `.vscode/`.

If `dotnet build` fails on the Web project with a locked `NinjagoScanner.Web.exe`, an instance of the app is still running — stop it first.

Gemini API key for PictureService (required for scanning): set via user secrets in `NinjagoScanner.PictureService` (`Gemini:ApiKey`, `Gemini:Model`) or env vars (`GEMINI_API_KEY`, `GEMINI_MODEL`). Default model is `gemini-2.5-flash`.

Tests use xunit. `NinjagoScanner.Web.Tests` project-references all three app projects and spins up in-process test hosts for CatalogService/PictureService (`Fixtures/CatalogServiceTestHost.cs`, `Fixtures/PictureServiceTestHost.cs`) rather than mocking the gRPC calls.

## Architecture

Three independently runnable .NET 10 services, one solution, communicating over gRPC (`Protos/*.proto`, compiled via `Grpc.Tools`):

- **NinjagoScanner.CatalogService** — owns the reference catalog data (`Series`, `Category`, `Card`, `Series Metadata`) loaded from `cardInfos/*.json` inside the service project (copied to output on build). Doesn't know about photos or scanning. Exposes `CardCatalog` gRPC service: `ListSeries`, `GetSeries`, `ListAllCards`, `GetSeriesMetadata`, `GetServiceInfo`.
- **NinjagoScanner.PictureService** — manages card photos and their sidecars: runs Gemini-based AI analysis (`GeminiApiService.cs`), reads/writes photo bytes in an S3 bucket keyed by a generated photo ID (`PhotoStore.cs`), reads/writes sidecar records in a DynamoDB table (`SidecarTable.cs`, cached in `SidecarCache.cs`), and applies manual sidecar edits. Consults CatalogService via its own gRPC client (`CatalogGrpcClient.cs`) for series/card matching — never reads `cardInfos` locally. Exposes `CardPictureService` gRPC service: `Scan`, `UploadPhoto` (client-streaming; assigns the photo ID, stores the bytes, triggers AI analysis), `GetPhotoDownloadUrl` (short-lived pre-signed S3 GET URL), `ListCards`, `UpdateSidecar`/`UpdateSetName`/`UpdateCardNumber`/`UpdateCardLanguage`/`UpdateReviewStatus`, `MigrateSidecars`, `DeletePhoto`. The only service that ever holds AWS credentials.
- **NinjagoScanner.Web** — the Blazor Server app (Interactive Server render mode) people actually use: card tiles (`/`, Overview), full list/filter/detail (`/collection`), gallery (`/gallery`), mobile photo upload (`/upload`), photo review (`/review`), and an about page (`/about`). Talks to both other services over gRPC directly from server-rendered page code, via `Services/CardCatalogService.cs` and `Services/PictureServiceClient.cs`; never touches `cardInfos`, sidecar records, or AWS directly — photo bytes and download URLs both come from PictureService.

Data flow: photo bytes live in an S3 bucket, keyed by a generated photo ID (`photos/<photo_id>`); sidecar records live in a DynamoDB table, one item per photo ID (see `PhotoStore.cs`/`SidecarTable.cs`). A card photo is "owned" by a catalog card when its sidecar's `SeriesName` + `CardNumber` match a catalog entry (see Owned Copies / Unmapped Photo in the glossary). Analysis Status (`pending`/`ok`/`uncertain`/`failed`) is machine-set by AI Analysis; Review Status (`unreviewed`/`verified`/`incorrect`) is a separate, human-only judgment — nothing sets both.

Service addresses are resolved through a layered config: explicit config key → env var → default (see `WebConfig.cs` in `NinjagoScanner.Web`, `BffConfig`'s former counterpart). Default addresses: CatalogService `http://localhost:5073`, PictureService `http://localhost:5169`. PictureService's S3 bucket/DynamoDB table names are resolved the same way (`ScannerConfig.ResolvePhotosBucketName`/`ResolveSidecarTableName`) — no local-filesystem fallback; both must be configured.

Internal classes in PictureService (e.g. `PhotoStore`, `SidecarTable`, `SidecarCache`) are exposed to tests via `InternalsVisibleTo` rather than being made public.

Hosting: all three services run as Fly.io apps in one Fly organization, connected over Fly's private network (6PN / `*.internal` DNS) — only `NinjagoScanner.Web` gets a public Fly IP. See each project's `fly.toml` and `infra/README.md`.

## Domain vocabulary & spec workflow

This repo uses **OpenSpec** for spec-driven changes (`openspec/` — `specs/`, `changes/`, `GLOSSARY.md`, `config.yaml`). Specs live in one shared `openspec/specs/` directory but capability names are prefixed by owning project (`catalog-service-*`, `picture-service-*`, `web-*`); unprefixed only for genuinely cross-cutting specs like the gRPC contracts. Use the `openspec-*` skills/commands for proposing, applying, and archiving changes rather than editing specs by hand.

`openspec/GLOSSARY.md` defines the project's ubiquitous language (Series, Card, Sidecar, Analysis Status, Review Status, Owned Copies, Unmapped Photo, etc.) — read it before naming new concepts in code, UI, or specs, and keep usage consistent with it.
