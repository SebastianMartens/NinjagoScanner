## Why

The collection now includes a few Polish-language cards, but the app's language handling is a closed `de`/`en`/`unknown` set baked into the Gemini prompt, the sidecar model, and the Collection page's language selector — there is no way to record or select Polish anywhere. Separately, Gemini's language detection is not perfectly reliable, and today there is no way to check or correct a photo's detected language from the Review page (the page most used for correcting Gemini's mistakes); the only place to fix a language is the Collection page's full sidecar edit form, several clicks away from the review workflow.

## What Changes

- Add `pl` (Polish) as a fourth recognized language value alongside `de`, `en`, and `unknown`, updated everywhere the closed set is currently enforced: the Gemini prompt/schema and response normalization, the two hand-duplicated `Languages` constant classes (PictureService and Web), and the Collection page's language `<select>`.
- Add a new single-field `UpdateCardLanguage` RPC to the Picture Service (proto + `PictureScannerGrpcService` handler + `CardCatalogService` client method), following the existing `UpdateCardNumber`/`UpdateSetName`/`UpdateReviewStatus` pattern, so a photo's language can be corrected without overwriting its other sidecar fields.
- Map `Language` onto `CardListItem` (currently missing) so the Review page has access to each photo's detected/stored language.
- Add a language dropdown to each photo tile on the Review page, pre-filled with that photo's current language (defaulting to German, matching existing default rules), offering German/English/Polish/Unknown; changing it saves immediately via `UpdateCardLanguage` and updates only that photo's `Language`, leaving grouping, `ReviewStatus`, and every other field unchanged (language is not part of the series+card-number identity/grouping key, so no group reload/reposition is needed).

## Capabilities

### New Capabilities
(none — this extends existing capabilities)

### Modified Capabilities
- `picture-service-gemini-analysis`: the detected-language normalization requirement changes from a closed `de`/`en`/`unknown` set to `de`/`en`/`pl`/`unknown`.
- `picture-service-sidecar-editing`: adds a fifth single-field RPC, `UpdateCardLanguage`, matching the shape of the other three field-scoped RPCs (creates a pending sidecar if none exists; otherwise updates only `Language`).
- `web-collection-list`: the sidecar edit form's Language control gains a fourth option, Polish, alongside German, English, and Unknown.
- `web-card-review-flow`: adds a requirement that each photo tile provides an inline language control, pre-filled with the photo's current language, that saves via `UpdateCardLanguage` on change without affecting grouping or other fields.

## Impact

- **Picture Service**: `ScannerModels.cs` (`Languages` constants, add `Polish = "pl"`), `GeminiApiService.cs` (prompt text, JSON schema, `NormalizeLanguage`), `Services/PictureScannerGrpcService.cs` (new `UpdateCardLanguage` handler), `Protos/picture_service.proto` (new RPC + request/response messages).
- **Web**: `Models/CardListItem.cs` (`Languages` constants + new `Language` property), `Protos/picture_service.proto` (mirrored proto changes), `Services/CardCatalogService.cs` (`ToCardListItem` mapping, new `UpdateCardLanguageAsync` client method), `Components/Pages/Collection.razor` (add Polish option to existing `<select>`), `Components/Pages/Review.razor` (new inline language dropdown per photo tile, plus code-behind save/draft-sync logic mirroring the card-number control).
- **Tests**: new `PictureScannerGrpcServiceUpdateCardLanguageTests.cs` in `NinjagoScanner.PictureService.Tests`, mirroring the existing `UpdateCardNumber` test file.
- **Specs/glossary**: `openspec/GLOSSARY.md` Language entry needs its enumerated values updated to include Polish (handled via the glossary-update skill after this change, per repo convention).
- No database/catalog schema changes: language remains purely descriptive on the sidecar/`CardEntry` and does not affect series+card-number uniqueness or grouping (confirmed by the existing Language glossary entry).
