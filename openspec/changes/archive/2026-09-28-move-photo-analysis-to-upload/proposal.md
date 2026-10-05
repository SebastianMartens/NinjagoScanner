## Why

The Overview page (`/`) still leads with a "Gemini-Suche starten" button, even though AI Analysis now runs automatically in most cases: after a single-photo upload, and when a person re-analyzes one photo during review. The only case that still needs a manual batch analysis is photos stored through the batch upload on `/upload`, so the button belongs next to that batch upload rather than on the landing page. Its label also names the AI provider, which ties the UI to an implementation detail that may change.

## What Changes

- Remove the scan button, its in-progress label and its status message from the Overview page. The Overview lede no longer talks about starting a scan.
- Add a batch-analysis action to the batch upload section of `/upload`. It starts the same PictureService scan as before (it analyzes photos that have not been analyzed yet), disables itself while running, and shows the same processed/skipped/uncertain/failed summary, the configuration-error message, or the early-stop indication.
- The action is labeled without naming any AI provider ("Nicht analysierte Fotos analysieren", in progress "Analyse laeuft..."). The status messages no longer mention Gemini either; the early-stop message says the analysis service was repeatedly unreachable.
- The action is disabled while a batch upload is running.
- The batch section's help text points to this action instead of to the Overview page.

## Capabilities

### New Capabilities
None.

### Modified Capabilities
- `web-overview`: the requirement "A manual Gemini scan can be triggered" is removed; the Overview no longer offers any analysis action.
- `web-photo-upload`: adds a requirement for the provider-neutral batch-analysis action in the batch upload section. The "Batch upload never triggers analysis" requirement now points to that action on `/upload` instead of to the Overview page.

## Impact

- `NinjagoScanner.Web/Components/Pages/Overview.razor`: scan button, `StartGeminiScanAsync` and the related state are removed, and the lede is updated.
- `NinjagoScanner.Web/Components/Pages/Upload.razor`: new analysis button, state and status message in the batch section, plus updated help text.
- `NinjagoScanner.Web/Services/ScanStatusMessageFormatter.cs` and its tests: provider-neutral wording, and the doc comment points at the Upload page.
- No gRPC/proto, PictureService or CatalogService changes: `PictureServiceClient.ScanAsync` is reused unchanged.
