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
- **NinjagoScanner.PictureService** — manages card photos and their sidecars: runs Gemini-based AI analysis (`GeminiApiService.cs`), writes/reads sidecar JSON files next to each photo (`SidecarStore.cs`, cached in `SidecarCache.cs`), and applies manual sidecar edits. Consults CatalogService via its own gRPC client (`CatalogGrpcClient.cs`) for series/card matching — never reads `cardInfos` locally. Exposes `CardPictureService` gRPC service: `Scan`, `ListCards`, `UpdateSidecar`/`UpdateSetName`/`UpdateCardNumber`/`UpdateCardLanguage`/`UpdateReviewStatus`, `UploadPhoto` (client-streaming), `MigrateSidecars`, `DeletePhoto`.
- **NinjagoScanner.Web** — the Blazor Server app people actually use: card tiles (`/`, Overview), full list/filter/detail (`/collection`), grouped table (`/table`), gallery (`/gallery`), mobile photo upload (`/upload`), and photo review. Talks to both other services over gRPC via `Services/CardCatalogService.cs` and `Services/PictureServiceClient.cs`; never touches `cardInfos` or sidecar files directly.

Data flow: photos + sidecar JSON live together in the shared `cardFotos/` directory at the repo root (outside any service's `bin/`). A card photo is "owned" by a catalog card when its sidecar's `SeriesName` + `CardNumber` match a catalog entry (see Owned Copies / Unmapped Photo in the glossary). Analysis Status (`pending`/`ok`/`uncertain`/`failed`) is machine-set by AI Analysis; Review Status (`unreviewed`/`verified`/`incorrect`) is a separate, human-only judgment — nothing sets both.

Service addresses and the shared `cardFotos` directory are all resolved through a layered config: explicit config key → env var → directory-walking auto-discovery (see `ResolveCardPhotosDirectory` etc. in `NinjagoScanner.Web/Program.cs`). Default addresses: CatalogService `http://localhost:5073`, PictureService `http://localhost:5169`.

Internal classes in PictureService (e.g. `SidecarStore`, `SidecarCache`) are exposed to tests via `InternalsVisibleTo` rather than being made public.

## Domain vocabulary & spec workflow

This repo uses **OpenSpec** for spec-driven changes (`openspec/` — `specs/`, `changes/`, `GLOSSARY.md`, `config.yaml`). Specs live in one shared `openspec/specs/` directory but capability names are prefixed by owning project (`catalog-service-*`, `picture-service-*`, `web-*`); unprefixed only for genuinely cross-cutting specs like the gRPC contracts. Use the `openspec-*` skills/commands for proposing, applying, and archiving changes rather than editing specs by hand.

`openspec/GLOSSARY.md` defines the project's ubiquitous language (Series, Card, Sidecar, Analysis Status, Review Status, Owned Copies, Unmapped Photo, etc.) — read it before naming new concepts in code, UI, or specs, and keep usage consistent with it.
