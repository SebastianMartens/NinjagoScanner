# NinjagoScanner

Dieses Repository enthaelt drei .NET-10-Projekte fuer das Erfassen und Anzeigen von Lego-Ninjago-Sammelkarten.

- `NinjagoScanner.PictureService`: Eigenstaendiger gRPC-Microservice fuer die Bildanalyse mit Gemini und das Schreiben von Sidecar-JSON-Dateien.
- `NinjagoScanner.Web`: Blazor-Webanwendung zur Anzeige der Karten als Kacheln und in Tabellenform.
- `NinjagoScanner.CatalogService`: Eigenstaendiger gRPC-Microservice, der die Katalogdaten (`cardInfos/*.json`) besitzt und als API bereitstellt.

Die Projektmappe im Root ist `NinjagoScanner.slnx`.

## Projektstruktur

```text
NinjagoScanner/
|-- cardFotos/
|-- NinjagoScanner.PictureService/
|-- NinjagoScanner.Web/
|-- NinjagoScanner.slnx
```

## Voraussetzungen

- .NET SDK 10
- Ein Gemini-API-Key fuer den PictureService

## Kartenbilder

Der gemeinsame Bildordner ist `cardFotos` im Repository-Root.

In diesem Ordner liegen:

- die Bilddateien, zum Beispiel `IMG_20260707_162946.jpg`
- die Sidecar-Dateien, zum Beispiel `IMG_20260707_162946.jpg.json`

## PictureService

Projektpfad:

- [NinjagoScanner.PictureService/NinjagoScanner.PictureService.csproj](c:/sma/github/NinjagoScanner/NinjagoScanner.PictureService/NinjagoScanner.PictureService.csproj)

Der PictureService ist ein eigenstaendiger gRPC-Microservice.
Die Webanwendung startet den Gemini-Scan ueber einen gRPC-Aufruf (`PictureScanner/Scan`) gegen diesen Service.

### Starten

```powershell
Set-Location c:\sma\github\NinjagoScanner\NinjagoScanner.PictureService
dotnet run
```

### gRPC-Endpunkte

- `PictureScanner/Scan`

Konfigurierbare Service-Adresse (auf Seite der Webanwendung):

- `PictureService:Address`
- `PICTURE_SERVICE_ADDRESS`

Default-Adresse:

- `http://localhost:5169`

### Gemini konfigurieren

Empfohlen ueber User Secrets:

```powershell
Set-Location c:\sma\github\NinjagoScanner\NinjagoScanner.PictureService
dotnet user-secrets set "Gemini:ApiKey" "DEIN_KEY"
dotnet user-secrets set "Gemini:Model" "gemini-2.5-flash"
```

Alternativ ueber Umgebungsvariablen:

```powershell
$env:GEMINI_API_KEY="DEIN_KEY"
$env:GEMINI_MODEL="gemini-2.5-flash"
```

### Verhalten bei `cardFotos`

Der PictureService sucht standardmaessig in dieser Reihenfolge nach dem Bildordner:

1. `cardFotos` direkt neben der EXE
2. `cardFotos` im aktuellen Arbeitsverzeichnis
3. `..\cardFotos` relativ zum aktuellen Arbeitsverzeichnis
4. Entwicklungs-Fallback relativ zur Build-Ausgabe

Zusatzlich kann der Bildordner explizit gesetzt werden:

- `CardPhotos:Directory`
- `CARD_PHOTOS_DIRECTORY`

### Build

```powershell
Set-Location c:\sma\github\NinjagoScanner\NinjagoScanner.PictureService
dotnet build
```

Der Build-Ordner liegt standardmaessig unter:

- `NinjagoScanner.PictureService\bin\Debug\net10.0`

Wichtig: Die Sidecar-Dateien werden im konfigurierten `cardFotos`-Ordner geschrieben.

## Webanwendung

Projektpfad:

- [NinjagoScanner.Web/NinjagoScanner.Web.csproj](c:/sma/github/NinjagoScanner/NinjagoScanner.Web/NinjagoScanner.Web.csproj)

### Entwicklung starten

Fuer den vollen Funktionsumfang (inkl. Gemini-Scan und Katalogdaten) muessen `NinjagoScanner.PictureService` und `NinjagoScanner.CatalogService` zusaetzlich laufen.

```powershell
Set-Location c:\sma\github\NinjagoScanner\NinjagoScanner.Web
dotnet run
```

Danach ist die App lokal erreichbar, typischerweise unter einer URL wie:

- `http://localhost:xxxx`

### Verfuegbare Seiten

- `/` : Kartenansicht als Kacheln mit Bildvorschau und Details
- `/table` : tabellarische Ansicht mit Gruppierung und Filter
- `/upload` : mobiler Foto-Upload direkt nach `cardFotos`

### Mobiler Upload (Android)

1. Starte die Webanwendung auf dem Rechner im lokalen Netzwerk (z. B. `dotnet run --urls "http://0.0.0.0:5000"`).
2. Oeffne die App auf dem Android-Handy ueber die LAN-Adresse des Rechners.
3. Gehe auf `/upload` und waehle Kamera oder Galerie.
4. Das Bild wird direkt in `cardFotos` gespeichert.
5. Starte danach wie gewohnt manuell den Gemini-Scan.

Optional kann die maximale Upload-Dateigroesse konfiguriert werden:

- `CardPhotos:MaxUploadBytes`
- `CardPhotosMaxUploadBytes`
- `CARD_PHOTOS_MAX_UPLOAD_BYTES`

### Verhalten bei `cardFotos`

Die Webanwendung nutzt einen konfigurierbaren Bildordner und versucht standardmaessig den gemeinsamen `cardFotos`-Ordner ausserhalb von `bin` zu finden.

Konfigurationsreihenfolge:

1. `CardPhotos:Directory`
2. `CardPhotosDirectory`
3. `NINJAGO_CARD_PHOTOS_DIR`
4. `CARD_PHOTOS_DIRECTORY`

Wenn nichts gesetzt ist, wird der naechste vorhandene `cardFotos`-Ordner in den uebergeordneten Verzeichnissen gesucht (mit Praeferenz ausserhalb von `bin`).

## Root-Build

Das gesamte Repository kann ueber die Solution im Root gebaut werden:

```powershell
Set-Location c:\sma\github\NinjagoScanner
dotnet build
```

## Catalog Microservice (gRPC)

Projektpfad:

- [NinjagoScanner.CatalogService/NinjagoScanner.CatalogService.csproj](NinjagoScanner.CatalogService/NinjagoScanner.CatalogService.csproj)

Der Service verwaltet den Serienkatalog unabhaengig als eigene Komponente.
Die JSON-Dateien liegen innerhalb des Service-Projekts in `NinjagoScanner.CatalogService/cardInfos` und werden beim Build in die Ausgabe kopiert.

### Starten

```powershell
Set-Location c:\sma\github\NinjagoScanner\NinjagoScanner.CatalogService
dotnet run
```

### gRPC-Endpunkte

- `CardCatalog/ListSeries`
- `CardCatalog/GetSeries`
- `CardCatalog/GetServiceInfo`

Konfiguration des Datenordners optional ueber:

- `Catalog:Directory`
- `CATALOG_DIRECTORY`

### Nutzung durch PictureService und Web

PictureService-Scanner und Webanwendung lesen die Katalogdaten nicht mehr lokal aus `cardInfos`,
sondern ausschliesslich ueber gRPC vom CatalogService.

Konfigurierbare Service-Adresse:

- `CatalogService:Address`
- `CATALOG_SERVICE_ADDRESS`

Default-Adresse:

- `http://localhost:5073`

## Typische Probleme

### 1. Gemini-Fehler `429 TooManyRequests`

Das ist ein Quota- oder Billing-Thema der Gemini-API, nicht zwingend ein Codefehler.

### 2. Gemini-Fehler `404 NotFound`

Das kann auf einen veralteten Modellnamen hinweisen. Aktueller Default ist:

- `gemini-2.5-flash`

### 3. Webprojekt baut nicht wegen gesperrter EXE

Wenn `dotnet build` im Webprojekt mit einer gesperrten `NinjagoScanner.Web.exe` fehlschlaegt, laeuft meistens noch eine Instanz der App. Die laufende App beenden und dann erneut bauen.

## Naechste sinnvolle Erweiterungen

1. Sortierung in der Tabellenansicht per Spaltenkopf
2. Re-Scan einzelner Karten direkt aus der Weboberflaeche
3. Konfigurierbaren Kartenordner auch fuer das Webprojekt einfuehren