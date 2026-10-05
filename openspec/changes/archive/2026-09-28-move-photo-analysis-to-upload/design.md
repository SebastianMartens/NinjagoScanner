## Context

`Overview.razor` calls `PictureServiceClient.ScanAsync()` from `StartGeminiScanAsync` and shows the result through `ScanStatusMessageFormatter.BuildMessage`. `Upload.razor` already has a batch section ("Viele Fotos auf einmal hochladen") whose help text sends people to the Overview to run the analysis. The `Scan` RPC runs as one synchronous call and skips photos that already have a sidecar. The UI stays in German.

## Goals / Non-Goals

**Goals:**
- Move the manual batch analysis into the batch upload section, reusing the existing client call and formatter.
- Keep every user-facing text free of provider/model names.

**Non-Goals:**
- Starting the analysis automatically when a batch upload finishes. People keep choosing when to spend analysis quota on a large batch.
- Changing the `Scan` RPC, its skip/overwrite semantics, or the single-photo and review re-analysis flows.
- Removing "Gemini" from texts that describe the analysis rather than trigger it (for example the analysis-status hint on `/collection`, or spec wording in `web-overview`'s status breakdown). That can be a separate cleanup.

## Decisions

- **Placement: inside the existing batch `upload-card` section, below the batch results, with its own `upload-actions` row and status line.** The action belongs to the batch workflow, so a separate section would pull it out of context. Alternative considered: a third `upload-card` section. It is more visually separate but repeats the help text.
- **Labels: "Nicht analysierte Fotos analysieren" / "Analyse laeuft..."** This describes what the scan actually does (it skips photos that already have a sidecar) without naming a provider. It follows the page's existing ASCII-umlaut style ("laeuft", "Uebersicht").
- **Reuse `ScanStatusMessageFormatter`, with provider-neutral wording.** The early-stop text becomes "Analyse vorzeitig abgebrochen (Analysedienst wiederholt nicht erreichbar): … Spaeter erneut versuchen." and the normal text becomes "Analyse fertig: …". The generic fallback for configuration errors becomes "Analyse konnte nicht gestartet werden." PictureService's own configuration-error message is passed through unchanged. It may name an env var like `GEMINI_API_KEY`, which is acceptable because it is an operator-facing diagnostic, not a label. The formatter's tests are updated to match.
- **Disable the analysis action during a batch upload, but not the other way round.** Scanning while files are still arriving would report a partial picture and race with the uploads. A batch upload started during a scan does no harm: new photos are either picked up or left `notAnalyzed` for the next run, so the rule is kept one-directional and simple.
- **Cancellation: pass the page's `disposeCancellation.Token` to `ScanAsync`.** Leaving the page cancels the client-side wait, the same way the batch upload behaves. Whether PictureService finishes the scan server-side is unchanged from today's Overview behavior.

## Risks / Trade-offs

- [People used to the Overview button can't find it] → The batch help text points to the new action, and `/upload` is already in the navigation.
- [A long scan ties up the circuit while the tab is open] → Same as today on the Overview. The in-progress label and disabled state make this visible.
