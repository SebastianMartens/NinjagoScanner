## 1. CatalogService: rarity

- [x] 1.1 Add `rarity` to `CatalogCardEntry` in the CatalogService proto (next free field number) and to `CatalogContracts.cs`; verify `dotnet build NinjagoScanner.slnx` succeeds
- [x] 1.2 One-time migration: with a throw-away script, insert `"Rarity": "limited"` after each `"Class": "limited edition"` line and `"Rarity": "common"` after every other `"Class"` line in all `cardInfos/*.json`; verify `git diff` shows only added `Rarity` lines (same count as `Class` lines) and the JSON still parses
- [x] 1.3 Read `Rarity` in `CatalogRepository` like `Class` (category-level, inherited, card-level override, fail fast when missing or outside `common`/`limited`/`legendary`) and map it in `CardCatalogGrpcService`; verify new catalog tests: inheritance, card override, missing → load failure, unknown → load failure, and in the real data every `limited edition` card is `limited` and all others `common`
- [x] 1.4 Verify the `Web.Tests` CatalogServiceTestHost-based tests still pass (`dotnet test NinjagoScanner.Web.Tests`)

## 2. PictureService: remove sidecar rarity

- [x] 2.1 In `NinjagoScanner.Web/Protos/picture_service.proto` delete `rarity` from `CardEntry` and `UpdateSidecarRequest`, add `reserved` entries for numbers 6 / 7 and the name; regenerate stubs (`uv run python scripts/gen_proto.py`) and verify the Python and .NET builds compile
- [x] 2.2 Remove `rarity` from `models.py` (Judged section / record), `sidecar_table.py` mapping and `picture_scanner_service.py` (request handling, card entry); verify `uv run pytest` passes with the rarity assertions in `test_models.py`, `test_sidecar_table.py`, `test_picture_scanner_service.py` and `test_card_analysis.py` updated
- [x] 2.3 Add a sidecar-table test that reading an item with a legacy `Rarity` attribute succeeds and the value is neither exposed nor written back on save

## 3. PictureService: analysis pipeline

- [x] 3.1 Add `rarity_hint` to `ATTRIBUTE_DETECTION_PROMPT` (observed hints only: LE number prefix, special finish; empty if none); verify with a stage 1 test that the prompt contains the attribute and no `rarity` classification instruction
- [x] 3.2 Add the `rarity` rule (offering only `common` | `limited`) to `DERIVED_ATTRIBUTES_PROMPT` and a `CARD_RARITIES` constant (`common`, `limited`, `legendary`) in `models.py`; drop out-of-set values in stage 2 post-processing like `class`; verify tests for recognized, unrecognized (e.g. `rare`) and absent rarity
- [x] 3.3 Populate `card_class` and new `card_rarity` on `CatalogCardInfo` in `catalog_client.load_catalog_cards` (empty → `None`) and remove the stale "not yet shipped" comment/docstring; verify a catalog-client test with a faked `ListAllCards` response
- [x] 3.4 Add the +10 rarity score to `_score_card` in `card_analysis_stage_3.py` (no penalty, no candidate by rarity alone, max 110) and update its docstring; verify tests: all four agree → 110, tie-break by rarity, rarity-only → no match, mismatch keeps number points, unknown derived rarity gives no signal
- [x] 3.5 Add tests that class scoring is effective now (class points awarded; number with a different class earns nothing) using catalog cards that carry a class; verify `uv run pytest` is green

## 4. Web: data path

- [x] 4.1 Add a rarity constants/helper in Web (`common`/`limited`/`legendary`, German labels "Normal"/"Limited"/"Legendär") and carry catalog rarity on the catalog card model used by `CatalogServiceClient`; verify a unit test of the helper
- [x] 4.2 Remove `Rarity` from `PictureServiceClient` mapping (`update.Rarity`, `entry.Rarity`) and the sidecar-derived `Rarity` properties on `CardListItem`, `GalleryCardItem`, `CollectionCardDetails`; expose the catalog card's rarity instead in `CollectionQueryService` (lines ~99, ~317); verify `dotnet build` and the `CollectionQueryService*Tests` with catalog-based rarity assertions
- [x] 4.3 Update `PictureServiceTestHost` (drop `Rarity`), test JSON fixtures (`"Rarity": ...`), and add a `limited`-class catalog card to the test catalog; verify `dotnet test NinjagoScanner.Web.Tests` passes

## 5. Web: pages

- [x] 5.1 `Collection.razor`: remove the sidecar form, `sidecarDraft`, save handler and `UpdateSidecar` call; add the "Im Review öffnen" button (`/review?series=&card=`, shown only when the card has photos) and a read-only rarity line; verify with a bUnit/page test or manual run that no inputs remain and the button navigates to the card's review group
- [x] 5.2 `Review.razor`: show the group's catalog rarity in the details "Seltenheit" line (empty for unmatched); update the stale comment near line 670; verify manually and with the existing review tests
- [x] 5.3 `Gallery.razor` / `CardTagHelper.cs`: derive the tag from catalog rarity (`limited` → "Limited", `legendary` → "Legendär", `common` → none); verify `CollectionQueryServiceGalleryTests` and a tag-helper unit test
- [x] 5.4 `UnlockOverlay.razor`, `UnlockCelebration.cs`, `GamificationCelebrationCenter.cs`, `Upload.razor:419`, `Review.razor:698`: pass the matched catalog card's rarity; map colours for `limited` and `legendary` plus a default and show the chip only for non-`common`; verify a manual upload of a limited card and a review correction show the "Limited" chip

## 6. Web: gamification

- [x] 6.1 `GamificationService`: count `LimitedEditionOwnedCards` from distinct owned catalog cards with rarity `limited`; count `LegendaryOwnedCards` from catalog rarity `legendary` the same way, and remove `IsRarity` and the sidecar rarity in `PhotoFacts`; verify updated `GamificationServiceTests` (limited counted once per card, common not counted, sidecar rarity ignored, `first-legendary` unlocks with a legendary catalog card in the test fixture and stays locked without one)
- [x] 6.2 `Achievement.cs`: keep `first-legendary` and `limited-hunter` with unchanged ids, goals and XP; verify `dotnet test NinjagoScanner.slnx` is green

## 7. Docs and wrap-up

- [x] 7.1 Update `openspec/GLOSSARY.md`: add a **Rarity** entry (`common`/`limited`, catalog-owned, derived from Class), fix **Class** ("not a rarity indicator" → relation to rarity), remove rarity from **Sidecar** and **Detected Text**; verify wording against the glossary's style
- [x] 7.2 Update `README.md` line about sidecar contents (remove rarity) and any CLAUDE.md mention if present; verify with a repo search for `rarity` outside archive showing only intended usages
- [x] 7.3 Run `openspec validate catalog-owned-rarity --strict`, `dotnet test NinjagoScanner.slnx` and `uv run pytest` in `picture_service/`; verify all pass
