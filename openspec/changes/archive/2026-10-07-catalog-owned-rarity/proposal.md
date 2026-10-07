## Why

Rarity is currently a free-text field on the sidecar that nothing ever fills: the AI analysis does not detect it, the catalog does not know it, and the only way to set it is typing it by hand in the `/collection` edit form. As a result the gallery tag, the unlock-overlay colours and the rarity achievements (`limited-hunter`, `first-legendary`) are effectively dead, and their vocabulary (`common`, `rare`, `ultra rare`, `legendary`, `limited edition`) is hardcoded in two places in Web with no shared definition. Rarity is a property of the card, not of a photo, so it belongs to the catalog.

## What Changes

- **CatalogService**: every card gets a `rarity` from a fixed set — `common`, `limited`, `legendary` — that is **stored in the catalog data** (`cardInfos/*.json`), declared like `Class` (per category, overridable per card so single rare cards can be flagged later) and exposed on `CatalogCardEntry`. The values are derived from the class **once**, by a one-time migration of the JSON files (`limited edition` → `limited`, everything else → `common`); afterwards the data is the source of truth and no longer follows the class. No card is `legendary` yet.
- **PictureService analysis pipeline**:
  - Stage 1 returns one additional attribute with purely visual hints of rarity (no interpretation).
  - Stage 2 derives a `rarity` (`common` | `limited`) from the stage 1 attributes; values outside the set are treated as absent.
  - Stage 3 uses the derived rarity as an additional scoring signal against the catalog card's rarity.
  - PictureService's catalog client starts populating the catalog card's `class` (and the new `rarity`). Today it ignores `CatalogCardEntry.class`, so stage 3's class scoring has never actually had a class to compare against.
- **BREAKING (internal contract)**: the sidecar's `rarity` field is removed — from the Judged section, `CardEntry`, `UpdateSidecarRequest` (field numbers reserved in `picture_service.proto`), the DynamoDB mapping, and all Web models. Existing stored `Rarity` values are ignored; no migration.
- **Web `/collection`**: the sidecar edit form is removed (the review page covers all sidecar editing). The detail pane instead offers a button that opens the review page on that card. The card's catalog rarity is shown read-only.
- **Web `/review`**: the "Seltenheit" line in a photo's collapsed details shows the matched catalog card's rarity instead of a sidecar value.
- **Web gallery, celebrations, gamification**: the rarity tag, the unlock overlay (chip + accent colour) and the rarity achievements are driven by the catalog card's rarity. The old free-text vocabulary is replaced by the catalog's `common` / `limited` / `legendary`; `limited-hunter` and `first-legendary` ("Legendenbrecher") count owned cards by catalog rarity (`first-legendary` stays although no card is legendary yet).
- Glossary gains a **Rarity** entry; the **Class** and **Sidecar** entries are corrected.

## Capabilities

### New Capabilities

_(none — rarity is added to existing capabilities)_

### Modified Capabilities

- `catalog-service-card-catalog`: each card carries a rarity stored in the catalog data (one-time derived from class), and `ListAllCards` returns it.
- `picture-service-attribute-detection`: stage 1 returns a visual rarity-hint attribute.
- `picture-service-derived-attributes`: stage 2 derives a `rarity` restricted to the fixed rarity set.
- `picture-service-catalog-matching`: stage 3 scores derived rarity against catalog rarity; catalog cards carry class and rarity.
- `picture-service-sidecar-sections`: the Judged section no longer has a rarity field.
- `picture-service-sidecar-editing`: `UpdateSidecar` no longer accepts or overwrites rarity.
- `picture-service-sidecar-review`: example list of editable fields no longer names rarity.
- `picture-service-card-listing`: `CardEntry` no longer carries rarity.
- `picture-service-photo-reanalysis`: re-analysis result no longer lists rarity among replaced/returned fields.
- `web-collection-list`: sidecar edit form removed; "open in review" button and read-only catalog rarity added; detail pane no longer disables "editing" for photo-less cards.
- `web-card-review`: example list of editable fields no longer names rarity.
- `web-card-review-flow`: photo details show catalog rarity; re-analysis refresh no longer lists a photo rarity.
- `web-gallery-page`: card tiles show a rarity tag from the catalog.
- `web-unlock-feedback`: card overlay chip and accent follow catalog rarity.
- `web-rank-progression`: rarity achievements follow catalog rarity; `first-legendary` is kept.

## Impact

- **CatalogService**: all `cardInfos/*.json` (one-time rarity migration), `CatalogContracts.cs`, `CatalogRepository.cs`, `CardCatalogGrpcService.cs`, `Protos/*.proto`, catalog tests.
- **PictureService (Python)**: `prompts.py`, `card_analysis_stage_1_and_2.py`, `card_analysis_stage_3.py`, `catalog_client.py`, `models.py`, `sidecar_table.py`, `picture_scanner_service.py`, proto codegen, tests.
- **Proto**: `NinjagoScanner.Web/Protos/picture_service.proto` (remove `rarity`, reserve numbers), CatalogService proto (add `rarity`).
- **Web**: `Collection.razor`, `Review.razor`, `Gallery.razor`, `Upload.razor`, `UnlockOverlay.razor`, `CardTagHelper.cs`, `CollectionQueryService.cs`, `PictureServiceClient.cs`, `GamificationService.cs`, `Achievement.cs`, models, `PictureServiceTestHost.cs` and the affected tests.
- **Data**: existing sidecar `Rarity` attributes in DynamoDB become unused (not deleted). `first-legendary` can unlock only once a card is marked `legendary` in the catalog data.
- **Docs**: `openspec/GLOSSARY.md`.
