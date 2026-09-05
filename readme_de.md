# NinjagoScanner

*[English version](readme.md)*

## Was ist NinjagoScanner?

Sammelst du Ninjago-Sammelkarten? NinjagoScanner hilft dir, alle deine Karten zu ordnen!

Mach einfach ein Foto von einer Karte. Die App schaut sich das Bild an und erkennt ganz allein, welche Karte es ist. Kein Tippen, kein Suchen, kein Raten.

Jede gescannte Karte landet in deiner eigenen Sammlung. Du kannst:

- **Alle deine Karten sehen** an einem Ort, als kleine, ordentliche Bilder.
- **Pruefen, welche Karten du schon hast** — ist das Puzzle schon vollständig?
- **Herausfinden, welche Karten dir noch fehlen** aus einer Serie.
- **Eine Karte korrigieren**, falls die App sich mal irrt, damit deine Sammlung stimmt.
- **Fotos vom Handy hochladen**, direkt dort, wo du gerade mit deinen Karten sitzt.

Keine unordentlichen Kartenstapel mehr auf dem Tisch. Kein Blättern mehr durch Ordner, um zu checken, was du hast. NinjagoScanner hält deine ganze Sammlung ordentlich, durchsuchbar und macht Spass — für Kartenfans jeden Alters.

---

## Entwickler-Anleitung

Der Rest dieses Dokuments beschreibt, wie das Projekt aufgebaut ist und wie du es selbst zum Laufen bringst.

Dieses Repository enthaelt drei eigenstaendig lauffaehige .NET-10-Services, die per gRPC kommunizieren, jeweils mit einem passenden xunit-Testprojekt, alle ueber eine Solution gebaut.

- `NinjagoScanner.CatalogService`: gRPC-Microservice, der die Katalogdaten (`cardInfos/*.json`, im Service-Projekt enthalten) besitzt — Serien, Kategorien, Karten. Weiss nichts von Fotos oder Scanning.
- `NinjagoScanner.PictureService`: gRPC-Microservice, der die Gemini-basierte KI-Analyse der Kartenfotos durchfuehrt und den Fotospeicher (ein S3-Bucket) sowie die Sidecar-Datensaetze (eine DynamoDB-Tabelle) besitzt. Fragt fuer den Serien-/Kartenabgleich ueber einen eigenen gRPC-Client den CatalogService — liest `cardInfos` nie lokal. Der einzige Service, der jemals AWS-Zugangsdaten besitzt.
- `NinjagoScanner.Web`: die Blazor-Server-Anwendung, die tatsaechlich genutzt wird — Kartenkacheln, vollstaendige Listen-/Filter-/Detailansicht, Galerie, mobiler Foto-Upload, Foto-Review, Login und eine Info-Seite.

Dazu kommt `NinjagoScanner.CardFotosMigration`, ein einmalig auszufuehrendes, rein kopierendes Konsolentool zur Migration eines alten lokalen `cardFotos`-Ordners (Bild + `.json`-Sidecar pro Foto) nach S3 + DynamoDB — nicht Teil des normalen Entwicklungs-/Run-Kreislaufs.

Die Projektmappe im Root ist `NinjagoScanner.slnx` (keine `.sln`-Datei).

### Projektstruktur

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

### Voraussetzungen

- .NET SDK 10
- Ein Gemini-API-Key fuer den PictureService
- Fuer alles ueber lokales Ausprobieren hinaus: ein AWS-Konto mit einem S3-Bucket und einer DynamoDB-Tabelle — der PictureService hat keinen lokalen Dateisystem-Fallback (siehe [infra/](infra/README.md))

### Kartenfotos und Sidecar-Daten

Die Bilddateien liegen in einem S3-Bucket, mit einer generierten Foto-ID als Schluessel (`photos/<photo_id>`). Sidecar-Datensaetze — das KI-Analyseergebnis plus alle manuellen Korrekturen (Serie, Kartennummer, Seltenheit, Review-Status usw.) — liegen in einer DynamoDB-Tabelle, ein Eintrag pro Foto-ID. Zur Laufzeit wird nichts mehr im lokalen Dateisystem geschrieben; der PictureService ist der einzige Service, der mit S3/DynamoDB spricht, ausschliesslich ueber `PhotoStore.cs` / `SidecarTable.cs`.

Ein aelteres lokales Ordnerlayout (`cardFotos/<bild>` plus eine Sidecar-Datei `cardFotos/<bild>.json`) stammt aus der Zeit davor und wird von keinem laufenden Service mehr gelesen oder geschrieben. `NinjagoScanner.CardFotosMigration` existiert einzig dafuer, einen solchen Ordner einmalig nach S3 + DynamoDB zu migrieren.

### CatalogService

Projektpfad:

- [NinjagoScanner.CatalogService/NinjagoScanner.CatalogService.csproj](NinjagoScanner.CatalogService/NinjagoScanner.CatalogService.csproj)

Verwaltet den Serien-/Kartenkatalog unabhaengig als eigene Komponente. Die JSON-Dateien liegen innerhalb des Service-Projekts in `NinjagoScanner.CatalogService/cardInfos` und werden beim Build in die Ausgabe kopiert.

#### Starten

```powershell
Set-Location NinjagoScanner.CatalogService
dotnet run
```

Laeuft standardmaessig unter `http://localhost:5073`.

#### gRPC-Endpunkte (`CardCatalog`)

- `ListSeries`
- `GetSeries`
- `ListAllCards`
- `GetSeriesMetadata`
- `GetServiceInfo`

Konfiguration des Datenordners optional ueber:

- `Catalog:Directory`
- `CATALOG_DIRECTORY`

Konfigurierbare Adresse (genutzt von PictureService und Web):

- `CatalogService:Address`
- `CATALOG_SERVICE_ADDRESS`

Default-Adresse: `http://localhost:5073`

### PictureService

Projektpfad:

- [NinjagoScanner.PictureService/NinjagoScanner.PictureService.csproj](NinjagoScanner.PictureService/NinjagoScanner.PictureService.csproj)

Eigenstaendiger gRPC-Microservice. Fuehrt die Gemini-basierte KI-Analyse der Kartenfotos durch und besitzt den Fotospeicher (S3) sowie die Sidecar-Datensaetze (DynamoDB).

#### Starten

```powershell
Set-Location NinjagoScanner.PictureService
dotnet run
```

Laeuft standardmaessig unter `http://localhost:5169`.

#### gRPC-Endpunkte (`CardPictureService`)

- `Scan` — Massen-Backfill/Admin-Operation: analysiert jedes Foto in S3, das noch keinen Sidecar-Datensatz hat
- `UploadPhoto` — Client-Streaming; vergibt die generierte Foto-ID, speichert die Bilddaten in S3, stoesst die KI-Analyse an
- `GetPhotoDownloadUrl` — kurzlebige, vorsignierte S3-GET-URL fuer eine einzelne Foto-ID
- `ListCards`, `GetCardDetails`
- `UpdateSidecar`, `UpdateSetName`, `UpdateCardNumber`, `UpdateCardLanguage`, `UpdateReviewStatus`
- `MigrateSidecars` — repariert Sidecar-Datensaetze in einem aelteren Format
- `DeletePhoto`

Konfigurierbare Adresse (auf Seite der Webanwendung):

- `PictureService:Address`
- `PICTURE_SERVICE_ADDRESS`

Default-Adresse: `http://localhost:5169`

#### Gemini konfigurieren

Empfohlen ueber User Secrets:

```powershell
Set-Location NinjagoScanner.PictureService
dotnet user-secrets set "Gemini:ApiKey" "DEIN_KEY"
dotnet user-secrets set "Gemini:Model" "gemini-2.5-flash"
```

Alternativ ueber Umgebungsvariablen:

```powershell
$env:GEMINI_API_KEY="DEIN_KEY"
$env:GEMINI_MODEL="gemini-2.5-flash"
```

#### Speicher konfigurieren (S3 + DynamoDB)

Es gibt keinen lokalen Dateisystem-Fallback — beides muss konfiguriert sein, und AWS-Zugangsdaten muessen ueber die Standard-Credential-Chain des AWS SDK aufloesbar sein (Umgebungsvariablen, Shared-Credentials-Datei, uebernommene Rolle usw.):

- `Storage:PhotosBucketName` / `PHOTOS_BUCKET_NAME` — der S3-Bucket fuer die Bilddaten
- `Storage:SidecarTableName` / `SIDECAR_TABLE_NAME` — die DynamoDB-Tabelle fuer die Sidecar-Datensaetze

Siehe [infra/](infra/README.md) fuer das Terraform, das beides in AWS bereitstellt.

#### Build

```powershell
Set-Location NinjagoScanner.PictureService
dotnet build
```

Der Build-Ordner liegt standardmaessig unter:

- `NinjagoScanner.PictureService\bin\Debug\net10.0`

### Webanwendung

Projektpfad:

- [NinjagoScanner.Web/NinjagoScanner.Web.csproj](NinjagoScanner.Web/NinjagoScanner.Web.csproj)

Blazor-Server-Anwendung (Interactive-Server-Rendering).

#### Entwicklung starten

Fuer den vollen Funktionsumfang (inkl. Gemini-Scan und Katalogdaten) muessen `NinjagoScanner.PictureService` und `NinjagoScanner.CatalogService` zusaetzlich laufen.

```powershell
Set-Location NinjagoScanner.Web
dotnet run
```

VS Code hat eine `Launch All (CatalogService + PictureService + Web)`-Compound-Launch-Konfiguration in `.vscode/`, die alle drei zusammen startet.

#### Verfuegbare Seiten

- `/` — Uebersicht: Kartenkacheln mit Bildvorschau
- `/collection` — vollstaendige Listen-/Filter-/Detailansicht
- `/gallery` — Galerieansicht
- `/upload` — mobiler Foto-Upload
- `/review` — Foto-Review (Analysis Status vs. menschlicher Review Status)
- `/about` — Info-Seite

Die Anmeldung nutzt ASP.NET Core Identity (Benutzername + Passwort). Der Pfad der Benutzerdatenbank ist konfigurierbar:

- `Auth:DatabasePath`
- `AUTH_DATABASE_PATH`

Default: `Data/users.db` unter Windows, `/data/users.db` sonst.

#### Mobiler Upload (Android)

1. Starte die Webanwendung auf einem Rechner im lokalen Netzwerk (z. B. `dotnet run --urls "http://0.0.0.0:5000"`), mit CatalogService und PictureService ebenfalls laufend und erreichbar.
2. Oeffne die App auf dem Android-Handy ueber die LAN-Adresse des Rechners.
3. Melde dich an, gehe auf `/upload` und waehle Kamera oder Galerie.
4. Das Foto wird per Stream an den PictureService (`UploadPhoto`) uebertragen, der es in S3 speichert und automatisch den Gemini-Scan anstoesst — kein manueller Scan-Schritt noetig.

Optional kann die maximale Upload-Dateigroesse konfiguriert werden:

- `CardPhotos:MaxUploadBytes`
- `CARD_PHOTOS_MAX_UPLOAD_BYTES`

### Root-Build

Das gesamte Repository kann ueber die Solution im Root gebaut werden:

```powershell
dotnet build NinjagoScanner.slnx
```

### Tests

```powershell
dotnet test NinjagoScanner.slnx
```

Die Tests nutzen xunit. `NinjagoScanner.Web.Tests` referenziert alle drei Anwendungsprojekte und startet In-Process-Testhosts fuer CatalogService/PictureService (`Fixtures/CatalogServiceTestHost.cs`, `Fixtures/PictureServiceTestHost.cs`), statt die gRPC-Aufrufe zu mocken. Ein einzelnes Testprojekt: `dotnet test NinjagoScanner.Web.Tests`; ein einzelner Test nach Name: `dotnet test NinjagoScanner.Web.Tests --filter "FullyQualifiedName~SomeTestName"`.

### Typische Probleme

#### 1. Gemini-Fehler `429 TooManyRequests`

Das ist ein Quota- oder Billing-Thema der Gemini-API, nicht zwingend ein Codefehler.

#### 2. Gemini-Fehler `404 NotFound`

Das kann auf einen veralteten Modellnamen hinweisen. Aktueller Default ist:

- `gemini-2.5-flash`

#### 3. Webprojekt baut nicht wegen gesperrter EXE

Wenn `dotnet build` im Webprojekt mit einer gesperrten `NinjagoScanner.Web.exe` fehlschlaegt, laeuft meistens noch eine Instanz der App. Die laufende App beenden und dann erneut bauen.

### Produktionsinfrastruktur

Alle drei Services laufen als Fly.io-Apps in einer Fly-Organisation, verbunden ueber Flys privates Netzwerk (6PN / `*.internal`-DNS) — nur `NinjagoScanner.Web` bekommt eine oeffentliche Fly-IP. Siehe die `fly.toml` jedes Projekts sowie [infra/README.md](infra/README.md).

Der Speicher (der S3-Foto-Bucket, die DynamoDB-Sidecar-Tabelle und der IAM-User, mit dem der PictureService darauf zugreift) wird per Terraform in `infra/` verwaltet. Die Compute-Ressourcen sind nicht Terraform-verwaltet — sie werden direkt ueber `flyctl`/`fly.toml` pro Projekt konfiguriert.

Um zu pruefen, welche AWS-Speicherressourcen noch existieren:

```powershell
./infra/scripts/health-check.ps1
```

Erfordert die AWS CLI im PATH, konfiguriert mit Zugangsdaten fuer dieses Konto.
