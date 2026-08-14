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

This repository contains three .NET 10 projects for capturing and displaying Lego Ninjago trading cards.

- `NinjagoScanner.PictureService`: Standalone gRPC microservice for image analysis with Gemini and writing sidecar JSON files.
- `NinjagoScanner.Web`: Blazor web application for displaying the cards as tiles and in table form.
- `NinjagoScanner.CatalogService`: Standalone gRPC microservice that owns the catalog data (`cardInfos/*.json`) and exposes it as an API.

The solution at the root is `NinjagoScanner.slnx`.

### Project structure

```text
NinjagoScanner/
|-- cardFotos/
|-- NinjagoScanner.PictureService/
|-- NinjagoScanner.Web/
|-- NinjagoScanner.slnx
```

### Prerequisites

- .NET SDK 10
- A Gemini API key for the PictureService

### Card photos

The shared photo folder is `cardFotos` at the repository root.

This folder contains:

- the image files, for example `IMG_20260707_162946.jpg`
- the sidecar files, for example `IMG_20260707_162946.jpg.json`

### PictureService

Project path:

- [NinjagoScanner.PictureService/NinjagoScanner.PictureService.csproj](c:/sma/github/NinjagoScanner/NinjagoScanner.PictureService/NinjagoScanner.PictureService.csproj)

The PictureService is a standalone gRPC microservice.
The web application triggers the Gemini scan via a gRPC call (`PictureScanner/Scan`) against this service.

#### Starting

```powershell
Set-Location c:\sma\github\NinjagoScanner\NinjagoScanner.PictureService
dotnet run
```

#### gRPC endpoints

- `PictureScanner/Scan`

Configurable service address (on the web application side):

- `PictureService:Address`
- `PICTURE_SERVICE_ADDRESS`

Default address:

- `http://localhost:5169`

#### Configuring Gemini

Recommended via user secrets:

```powershell
Set-Location c:\sma\github\NinjagoScanner\NinjagoScanner.PictureService
dotnet user-secrets set "Gemini:ApiKey" "YOUR_KEY"
dotnet user-secrets set "Gemini:Model" "gemini-2.5-flash"
```

Alternatively via environment variables:

```powershell
$env:GEMINI_API_KEY="YOUR_KEY"
$env:GEMINI_MODEL="gemini-2.5-flash"
```

#### Behavior for `cardFotos`

By default, the PictureService looks for the photo folder in this order:

1. `cardFotos` directly next to the EXE
2. `cardFotos` in the current working directory
3. `..\cardFotos` relative to the current working directory
4. Development fallback relative to the build output

The photo folder can also be set explicitly:

- `CardPhotos:Directory`
- `CARD_PHOTOS_DIRECTORY`

#### Build

```powershell
Set-Location c:\sma\github\NinjagoScanner\NinjagoScanner.PictureService
dotnet build
```

The build output folder is located by default at:

- `NinjagoScanner.PictureService\bin\Debug\net10.0`

Important: the sidecar files are written to the configured `cardFotos` folder.

### Web application

Project path:

- [NinjagoScanner.Web/NinjagoScanner.Web.csproj](c:/sma/github/NinjagoScanner/NinjagoScanner.Web/NinjagoScanner.Web.csproj)

#### Starting development

For full functionality (including the Gemini scan and catalog data), `NinjagoScanner.PictureService` and `NinjagoScanner.CatalogService` must also be running.

```powershell
Set-Location c:\sma\github\NinjagoScanner\NinjagoScanner.Web
dotnet run
```

The app is then reachable locally, typically at a URL such as:

- `http://localhost:xxxx`

#### Available pages

- `/` : card view as tiles with image preview and details
- `/table` : tabular view with grouping and filtering
- `/upload` : mobile photo upload directly to `cardFotos`

#### Mobile upload (Android)

1. Start the web application on the machine on the local network (e.g. `dotnet run --urls "http://0.0.0.0:5000"`).
2. Open the app on the Android phone via the machine's LAN address.
3. Go to `/upload` and choose camera or gallery.
4. The image is saved directly to `cardFotos`.
5. Then start the Gemini scan manually as usual.

Optionally, the maximum upload file size can be configured:

- `CardPhotos:MaxUploadBytes`
- `CardPhotosMaxUploadBytes`
- `CARD_PHOTOS_MAX_UPLOAD_BYTES`

#### Behavior for `cardFotos`

The web application uses a configurable photo folder and by default tries to find the shared `cardFotos` folder outside of `bin`.

Configuration order:

1. `CardPhotos:Directory`
2. `CardPhotosDirectory`
3. `NINJAGO_CARD_PHOTOS_DIR`
4. `CARD_PHOTOS_DIRECTORY`

If nothing is set, the nearest existing `cardFotos` folder is searched for in the parent directories (preferring one outside `bin`).

### Root build

The entire repository can be built via the solution at the root:

```powershell
Set-Location c:\sma\github\NinjagoScanner
dotnet build
```

### Catalog microservice (gRPC)

Project path:

- [NinjagoScanner.CatalogService/NinjagoScanner.CatalogService.csproj](NinjagoScanner.CatalogService/NinjagoScanner.CatalogService.csproj)

The service manages the series catalog independently as its own component.
The JSON files live inside the service project at `NinjagoScanner.CatalogService/cardInfos` and are copied to the output on build.

#### Starting

```powershell
Set-Location c:\sma\github\NinjagoScanner\NinjagoScanner.CatalogService
dotnet run
```

#### gRPC endpoints

- `CardCatalog/ListSeries`
- `CardCatalog/GetSeries`
- `CardCatalog/GetServiceInfo`

Data folder configuration optionally via:

- `Catalog:Directory`
- `CATALOG_DIRECTORY`

#### Usage by PictureService and Web

The PictureService scanner and the web application no longer read catalog data locally from `cardInfos`,
but exclusively via gRPC from the CatalogService.

Configurable service address:

- `CatalogService:Address`
- `CATALOG_SERVICE_ADDRESS`

Default address:

- `http://localhost:5073`

### Common issues

#### 1. Gemini error `429 TooManyRequests`

This is a quota or billing issue with the Gemini API, not necessarily a code error.

#### 2. Gemini error `404 NotFound`

This can indicate an outdated model name. The current default is:

- `gemini-2.5-flash`

#### 3. Web project fails to build due to a locked EXE

If `dotnet build` in the web project fails with a locked `NinjagoScanner.Web.exe`, an instance of the app is usually still running. Stop the running app and then build again.

### Sensible next extensions

1. Sorting in the table view via column header
2. Re-scanning individual cards directly from the web interface
3. Introducing a configurable card folder for the web project as well
