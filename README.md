# NinjagoScanner

Dieses Repository enthaelt drei .NET-10-Projekte fuer das Erfassen und Anzeigen von Lego-Ninjago-Sammelkarten.

- `NinjagoScanner.Console`: Konsolenanwendung fuer die Bildanalyse mit Gemini und das Schreiben von Sidecar-JSON-Dateien.
- `NinjagoScanner.Web`: Blazor-Webanwendung zur Anzeige der Karten als Kacheln und in Tabellenform.
- `NinjagoScanner.Desktop`: WinUI-Desktopanwendung mit einem einzelnen Fenster und eingebettetem WebView fuer die Webanwendung.

Die Projektmappe im Root ist `NinjagoScanner.slnx`.

## Projektstruktur

```text
NinjagoScanner/
|-- cardFotos/
|-- NinjagoScanner.Console/
|-- NinjagoScanner.Desktop/
|-- NinjagoScanner.Web/
|-- NinjagoScanner.slnx
```

## Voraussetzungen

- .NET SDK 10
- Ein Gemini-API-Key fuer die Konsolenanwendung

## Kartenbilder

Der gemeinsame Bildordner ist `cardFotos` im Repository-Root.

In diesem Ordner liegen:

- die Bilddateien, zum Beispiel `IMG_20260707_162946.jpg`
- die Sidecar-Dateien, zum Beispiel `IMG_20260707_162946.jpg.json`

## Konsolenanwendung

Projektpfad:

- [NinjagoScanner.Console/NinjagoScanner.Console.csproj](c:/sma/github/NinjagoScanner/NinjagoScanner.Console/NinjagoScanner.Console.csproj)

### Entwicklung starten

```powershell
Set-Location c:\sma\github\NinjagoScanner\NinjagoScanner.Console
dotnet run
```

### Gemini konfigurieren

Empfohlen ueber User Secrets:

```powershell
Set-Location c:\sma\github\NinjagoScanner\NinjagoScanner.Console
dotnet user-secrets set "Gemini:ApiKey" "DEIN_KEY"
dotnet user-secrets set "Gemini:Model" "gemini-2.5-flash"
```

Alternativ ueber Umgebungsvariablen:

```powershell
$env:GEMINI_API_KEY="DEIN_KEY"
$env:GEMINI_MODEL="gemini-2.5-flash"
```

### Verhalten bei `cardFotos`

Die Konsolenanwendung sucht standardmaessig in dieser Reihenfolge nach dem Bildordner:

1. `cardFotos` direkt neben der EXE
2. `cardFotos` im aktuellen Arbeitsverzeichnis
3. `..\cardFotos` relativ zum aktuellen Arbeitsverzeichnis
4. Entwicklungs-Fallback relativ zur Build-Ausgabe

Zusatzlich kann der Bildordner explizit gesetzt werden:

- `CardPhotos:Directory`
- `CARD_PHOTOS_DIRECTORY`

### Publish

```powershell
Set-Location c:\sma\github\NinjagoScanner\NinjagoScanner.Console
dotnet publish -c Release
```

Der Publish-Ordner liegt standardmaessig unter:

- `NinjagoScanner.Console\bin\Release\net10.0\publish`

Wichtig: `cardFotos` wird beim Build und Publish automatisch mitkopiert und liegt danach direkt neben der EXE im Publish-Ordner.

## Webanwendung

Projektpfad:

- [NinjagoScanner.Web/NinjagoScanner.Web.csproj](c:/sma/github/NinjagoScanner/NinjagoScanner.Web/NinjagoScanner.Web.csproj)

### Entwicklung starten

```powershell
Set-Location c:\sma\github\NinjagoScanner\NinjagoScanner.Web
dotnet run
```

Danach ist die App lokal erreichbar, typischerweise unter einer URL wie:

- `http://localhost:xxxx`

### Verfuegbare Seiten

- `/` : Kartenansicht als Kacheln mit Bildvorschau und Details
- `/table` : tabellarische Ansicht mit Gruppierung und Filter

### Verhalten bei `cardFotos`

Die Webanwendung liest den Ordner aktuell relativ zum Projektinhalt:

- `..\cardFotos` relativ zum Content Root der Webanwendung

Das funktioniert in der Entwicklungsstruktur dieses Repositorys direkt mit dem gemeinsamen Root-Ordner `cardFotos`.

## Desktopanwendung

Projektpfad:

- [NinjagoScanner.Desktop/NinjagoScanner.Desktop.csproj](c:/sma/github/NinjagoScanner/NinjagoScanner.Desktop/NinjagoScanner.Desktop.csproj)

### Entwicklung starten

```powershell
Set-Location c:\sma\github\NinjagoScanner\NinjagoScanner.Desktop
dotnet run
```

### Verhalten

Die Desktopanwendung besteht aus einem einzigen Fenster mit einem `WebView2`-Steuerelement. Beim Start:

1. prueft sie, ob die Webanwendung bereits unter `http://127.0.0.1:5088/` erreichbar ist
2. startet sie die Webanwendung automatisch, falls noetig
3. laedt sie die Weboberflaeche direkt im Fenster

### Konfiguration

Optional kann die Desktopanwendung explizit konfiguriert werden:

- `NINJAGO_WEB_URL` : feste URL der Webanwendung
- `NINJAGO_WEB_EXE` : expliziter Pfad zur `NinjagoScanner.Web.exe`

Ohne Konfiguration sucht die Desktopanwendung standardmaessig nach der Web-EXE in typischen Build- und Publish-Pfaden des Repositorys.

## Root-Build

Das gesamte Repository kann ueber die Solution im Root gebaut werden:

```powershell
Set-Location c:\sma\github\NinjagoScanner
dotnet build
```

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