## 1. Provider-neutral status messages

- [x] 1.1 Update `ScanStatusMessageFormatter` wording ("Analyse fertig: …", "Analyse vorzeitig abgebrochen (Analysedienst wiederholt nicht erreichbar): …", fallback "Analyse konnte nicht gestartet werden.") and its doc comment so it points at the Upload page. Verify that no string in the file contains "Gemini".
- [x] 1.2 Update `ScanStatusMessageFormatterTests` to the new texts and add an assertion that the normal and early-stop messages don't contain "Gemini". Verify with `dotnet test NinjagoScanner.Web.Tests --filter "FullyQualifiedName~ScanStatusMessageFormatterTests"`.

## 2. Upload page

- [x] 2.1 In the batch section of `Upload.razor`, add the "Nicht analysierte Fotos analysieren" button (in progress: "Analyse laeuft..."). It calls `PictureServiceClient.ScanAsync(disposeCancellation.Token)`, shows `ScanStatusMessageFormatter.BuildMessage(...)` or "Analyse fehlgeschlagen: …" on exception, and is disabled while `isScanning || isBatchRunning`. Verify by building the solution with `dotnet build NinjagoScanner.slnx`.
- [x] 2.2 Replace the batch help text's reference to the Overview's "Gemini-Suche starten" with a pointer to the new button below. Verify that `grep -n Gemini NinjagoScanner.Web/Components/Pages/Upload.razor` returns nothing.

## 3. Overview page

- [x] 3.1 Remove the scan button, `isScanning`/`scanStatusMessage` state and `StartGeminiScanAsync` from `Overview.razor`, and drop the `PictureServiceClient` injection if it's no longer used. Replace the lede "Starte hier den Gemini-Scan neuer Kartenfotos." with a scan-neutral sentence about the collection overview. Verify that `grep -n "Gemini\|Scan" NinjagoScanner.Web/Components/Pages/Overview.razor` returns nothing and the solution builds.

## 4. Verification

- [x] 4.1 Run `dotnet test NinjagoScanner.slnx` and verify it passes.
- [ ] 4.2 Run the app. Verify that `/` shows no analysis button, and that on `/upload` the analysis button sits in the batch section, is disabled during a batch upload, and shows the summary message after a run.
