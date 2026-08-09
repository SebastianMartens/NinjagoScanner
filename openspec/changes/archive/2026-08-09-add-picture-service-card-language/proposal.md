## Why

Collectors' physical collections include the same catalog card (same series, same card number, same artwork) printed in different languages, e.g. a German-language and an English-language copy of the same card. Nothing in the system today records or surfaces that: sidecars have no language field, Gemini isn't asked to detect it, and there's no way to tell an owned photo's printed language apart from any other.

## What Changes

- Add a `Language` field (`de` | `en` | `unknown`) to the PictureService sidecar model, alongside existing detected fields like `CardName`/`Rarity`.
- Extend the Gemini analysis prompt and response JSON schema so a fresh analysis detects and reports the card's printed language from the photo.
- `ListCards` reports a resolved `Language` for every card entry: the sidecar's stored value when present, otherwise defaulted to German (`de`) — covering both a card with no sidecar yet and a sidecar written before this field existed. Existing sidecar files are **not** bulk-rewritten or re-analyzed to backfill this.
- An explicit `Language` of `unknown` (a real analysis that couldn't determine the language) is preserved as-is and never silently overwritten by the German default.
- `UpdateSidecar` accepts and persists an explicit `Language` value like every other editable sidecar field, so a person can correct the detected/defaulted value.
- The Web Collection Overview detail pane's sidecar edit form gains a Language control (a picker over German/English/Unknown, not free text), pre-filled with the resolved value.

**Non-goals / explicitly out of scope:**
- Card identity stays `(SeriesName, CardNumber)`. `Language` does not become part of a card's identity, does not introduce a new ownership/completion target, and does not affect `OwnedCopies` or duplicate detection — owning a German and an English copy of the same card is still one owned card with two matching photos, exactly as multi-photo cards already work today.
- No generic "variant kind" concept (foil/special editions/etc.) is introduced — this change covers language only.
- `NinjagoScanner.CatalogService` (the `cardInfos/*.json` data, `CatalogCardItem`, `ListAllCards` dedup/identity logic) is untouched. The pre-existing card-identity TODO in `catalog-service-card-catalog`'s spec remains deferred and is orthogonal to this change.

## Capabilities

### New Capabilities
(none — this change only modifies existing capabilities)

### Modified Capabilities
- `picture-service-gemini-analysis`: the analysis request/response schema and normalization logic gain a detected `Language` (`de`/`en`/`unknown`).
- `picture-service-sidecar-editing`: `UpdateSidecar` also accepts and overwrites the `Language` field, following the same rules as its other editable fields.
- `picture-service-card-listing`: `ListCards` reports a resolved `Language` per `CardEntry`, defaulting absent values to German rather than leaving them empty.
- `web-collection-overview`: the sidecar edit form in the card detail pane gains a `Language` field.

## Impact

- **NinjagoScanner.PictureService**: `ScannerModels.cs` (new `Languages` constants, `Language` field on `CardAnalysisResult` and `GeminiCardPayload`), `SidecarStore.cs` (`Language` on the lenient `SidecarRecord`), `GeminiApiService.cs` (prompt/JSON schema + response mapping), `Services/PictureScannerGrpcService.cs` (`ToCardEntry` default resolution, `UpdateSidecar` field handling).
- **gRPC contract**: `picture_service.proto`'s `CardEntry` and `UpdateSidecarRequest` messages gain a new `language` field (new field number, not a renumber); the proto copies under `NinjagoScanner.Web` are kept in sync.
- **NinjagoScanner.Web**: generated proto client usage and the Web-side sidecar/card models gain `Language`; the Collection Overview detail pane's sidecar edit form gains a Language picker.
- **No changes** to `NinjagoScanner.CatalogService` or any `cardInfos/*.json` data.
