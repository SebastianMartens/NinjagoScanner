# NinjagoScanner

*[Deutsche Version](readme_de.md)*

## What is NinjagoScanner?

Do you collect Ninjago trading cards? NinjagoScanner helps you keep all your cards organized!

Just take a photo of a card. The app looks at the picture and figures out which card it is — all by itself. No typing, no searching, no guessing.

Every card you scan gets added to your own collection. You can:

- **See all your cards** in one place, as neat little pictures.
- **Check which cards you already have** — is my puzzle complete?
- **Find out which cards you're still missing** from a series.
- **Fix a card** if the app got it wrong, so your collection stays correct.
- **Upload photos from your phone**, right from where you're sitting with your cards.

No more messy piles of cards on the table. No more flipping through folders to check what you have. NinjagoScanner keeps your whole collection tidy, searchable, and easy to enjoy — for card fans of any age.

---

## Developer guide

The rest of this document covers how the project is built, and how to run it yourself.

This repository contains three independently runnable .NET 10 services that communicate over gRPC, each with a matching xunit test project, all built via one solution.

- `NinjagoScanner.CatalogService`: gRPC microservice that owns the reference catalog data (`cardInfos/*.json`, shipped inside the service project) — series, categories, cards. Doesn't know about photos or scanning.
- `NinjagoScanner.PictureService`: gRPC microservice that runs Gemini-based AI analysis on card photos and owns photo storage (an S3 bucket) and sidecar records (a DynamoDB table). Consults CatalogService over its own gRPC client for series/card matching — never reads `cardInfos` locally. The only service that ever holds AWS credentials.
- `NinjagoScanner.Web`: the Blazor Server app people actually use — card tiles, full list/filter/detail, gallery, mobile photo upload, photo review, sign-in, and an about page.

There's also `NinjagoScanner.CardFotosMigration`, a one-time, copy-only console tool for migrating an old local `cardFotos/` folder (image + `.json` sidecar per photo) into S3 + DynamoDB — not part of the normal dev/run loop.

The solution at the root is `NinjagoScanner.slnx` (not a `.sln`).

### Project structure

```text
NinjagoScanner/
|-- NinjagoScanner.CatalogService/
|-- NinjagoScanner.CatalogService.Tests/
|-- NinjagoScanner.PictureService/
|-- NinjagoScanner.PictureService.Tests/
|-- NinjagoScanner.Web/
|-- NinjagoScanner.Web.Tests/
|-- NinjagoScanner.CardFotosMigration/
|-- infra/
|-- openspec/
|-- NinjagoScanner.slnx
```

### Prerequisites

- .NET SDK 10
- A Gemini API key for PictureService
- For anything beyond local exploration: an AWS account with an S3 bucket and a DynamoDB table — PictureService has no local-filesystem fallback (see [infra/](infra/README.md))

### Card photos and sidecar data

Photo bytes live in an S3 bucket, keyed by a generated photo ID (`photos/<photo_id>`). Sidecar records — the AI analysis result plus any manual corrections (series, card number, rarity, review status, etc.) — live in a DynamoDB table, one item per photo ID. Nothing is written to the local filesystem at runtime; PictureService is the only service that talks to S3/DynamoDB, exclusively through `PhotoStore.cs` / `SidecarTable.cs`.

### CatalogService

Project path:

- [NinjagoScanner.CatalogService/NinjagoScanner.CatalogService.csproj](NinjagoScanner.CatalogService/NinjagoScanner.CatalogService.csproj)

Manages the series/card catalog independently as its own component. The JSON files live inside the service project at `NinjagoScanner.CatalogService/cardInfos` and are copied to the output on build.

#### Starting

```powershell
Set-Location NinjagoScanner.CatalogService
dotnet run
```

Listens at `http://localhost:5073` by default.

#### gRPC endpoints (`CardCatalog`)

- `ListSeries`
- `GetSeries`
- `ListAllCards`
- `GetSeriesMetadata`
- `GetServiceInfo`

Data folder configuration optionally via:

- `Catalog:Directory`
- `CATALOG_DIRECTORY`

Configurable address (consumed by PictureService and Web):

- `CatalogService:Address`
- `CATALOG_SERVICE_ADDRESS`

Default address: `http://localhost:5073`

### PictureService

Project path:

- [NinjagoScanner.PictureService/NinjagoScanner.PictureService.csproj](NinjagoScanner.PictureService/NinjagoScanner.PictureService.csproj)

Standalone gRPC microservice. Runs Gemini-based AI analysis on card photos, and owns photo storage (S3) and sidecar records (DynamoDB).

#### Starting

```powershell
Set-Location NinjagoScanner.PictureService
dotnet run
```

Listens at `http://localhost:5169` by default.

#### gRPC endpoints (`CardPictureService`)

- `Scan` — bulk backfill/admin operation: analyzes every photo in S3 that has no sidecar record yet
- `UploadPhoto` — client-streaming; assigns the generated photo ID, stores the bytes in S3, triggers AI analysis
- `GetPhotoDownloadUrl` — short-lived pre-signed S3 GET URL for a single photo ID
- `ListCards`, `GetCardDetails`
- `UpdateSidecar`, `UpdateSetName`, `UpdateCardNumber`, `UpdateCardLanguage`, `UpdateReviewStatus`
- `MigrateSidecars` — repairs sidecar records written in an older shape
- `DeletePhoto`

Configurable address (on the Web application side):

- `PictureService:Address`
- `PICTURE_SERVICE_ADDRESS`

Default address: `http://localhost:5169`

#### Configuring Gemini

Recommended via user secrets:

```powershell
Set-Location NinjagoScanner.PictureService
dotnet user-secrets set "Gemini:ApiKey" "YOUR_KEY"
dotnet user-secrets set "Gemini:Model" "gemini-2.5-flash"
```

Alternatively via environment variables:

```powershell
$env:GEMINI_API_KEY="YOUR_KEY"
$env:GEMINI_MODEL="gemini-2.5-flash"
```

#### Configuring storage (S3 + DynamoDB)

There is no local-filesystem fallback — both of these must be configured, and AWS credentials must be resolvable via the standard AWS SDK credential chain (env vars, shared credentials file, an assumed role, etc.):

- `Storage:PhotosBucketName` / `PHOTOS_BUCKET_NAME` — the S3 bucket for photo bytes
- `Storage:SidecarTableName` / `SIDECAR_TABLE_NAME` — the DynamoDB table for sidecar records

See [infra/](infra/README.md) for the Terraform that provisions both in AWS.

#### Build

```powershell
Set-Location NinjagoScanner.PictureService
dotnet build
```

The build output folder is located by default at:

- `NinjagoScanner.PictureService\bin\Debug\net10.0`

### Web application

Project path:

- [NinjagoScanner.Web/NinjagoScanner.Web.csproj](NinjagoScanner.Web/NinjagoScanner.Web.csproj)

Blazor Server app (Interactive Server render mode).

#### Starting development

For full functionality (including the Gemini scan and catalog data), `NinjagoScanner.PictureService` and `NinjagoScanner.CatalogService` must also be running.

```powershell
Set-Location NinjagoScanner.Web
dotnet run
```

VS Code has a `Launch All (CatalogService + PictureService + Web)` compound launch config in `.vscode/` that starts all three together.

#### Available pages

- `/` — Overview: card tiles with image preview
- `/collection` — full list/filter/detail view
- `/gallery` — gallery view
- `/upload` — mobile photo upload
- `/review` — photo review (Analysis Status vs. human Review Status)
- `/about` — about page

Sign-in uses ASP.NET Core Identity (username + password). The user database path is configurable:

- `Auth:DatabasePath`
- `AUTH_DATABASE_PATH`

Default: `Data/users.db` on Windows, `/data/users.db` elsewhere.

#### Mobile upload (Android)

1. Start the web application on a machine on the local network (e.g. `dotnet run --urls "http://0.0.0.0:5000"`), with CatalogService and PictureService also running and reachable.
2. Open the app on the Android phone via the machine's LAN address.
3. Sign in, go to `/upload`, and choose camera or gallery.
4. The photo is streamed to PictureService (`UploadPhoto`), which stores it in S3 and triggers the Gemini scan automatically — no manual scan step needed.

Optionally, the maximum upload file size can be configured:

- `CardPhotos:MaxUploadBytes`
- `CARD_PHOTOS_MAX_UPLOAD_BYTES`

### Root build

The entire repository can be built via the solution at the root:

```powershell
dotnet build NinjagoScanner.slnx
```

### Tests

```powershell
dotnet test NinjagoScanner.slnx
```

Tests use xunit. `NinjagoScanner.Web.Tests` project-references all three app projects and spins up in-process test hosts for CatalogService/PictureService (`Fixtures/CatalogServiceTestHost.cs`, `Fixtures/PictureServiceTestHost.cs`) rather than mocking the gRPC calls. Run a single test project with `dotnet test NinjagoScanner.Web.Tests`, or a single test by name with `dotnet test NinjagoScanner.Web.Tests --filter "FullyQualifiedName~SomeTestName"`.

### Common issues

#### 1. Gemini error `429 TooManyRequests`

This is a quota or billing issue with the Gemini API, not necessarily a code error.

#### 2. Gemini error `404 NotFound`

This can indicate an outdated model name. The current default is:

- `gemini-2.5-flash`

#### 3. Web project fails to build due to a locked EXE

If `dotnet build` in the web project fails with a locked `NinjagoScanner.Web.exe`, an instance of the app is usually still running. Stop the running app and then build again.

### Production infrastructure

All three services run as Fly.io apps in one Fly organization, connected over Fly's private network (6PN / `*.internal` DNS) — only `NinjagoScanner.Web` gets a public Fly IP. See each project's `fly.toml` and [infra/README.md](infra/README.md).

Storage (the S3 photo bucket, the DynamoDB sidecar table, and the IAM user PictureService uses to reach them) is managed by Terraform in `infra/`. Compute is not Terraform-managed — it's configured directly via `flyctl`/`fly.toml` per project.

To check which AWS storage resources still exist, run:

```powershell
./infra/scripts/health-check.ps1
```

Requires the AWS CLI on PATH, configured with credentials for that account.
