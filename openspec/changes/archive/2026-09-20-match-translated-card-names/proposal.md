## Why

Stage 3 matches the derived `card_name` against catalog card names, but most catalog names exist in English only (German names are present only for series 3 and 8NL, and CatalogService sends one name per card: German if present, else English). A German card such as "Feuer-Drache" is therefore compared to "Fire Dragon", which shares almost no letters, so the name earns no points and German cards are not found.

## What Changes

- Stage 2 (derived attributes) additionally derives `card_name_en`: the English name of the card - the official English Ninjago name if known, otherwise a faithful translation; an English card name is repeated; omitted when there is no card name. Also fixes the `card_name` derivation hint, which wrongly said "take over card number".
- Stage 3 scores a catalog card's name against both `card_name` and `card_name_en` and uses the higher similarity. Stage 3 stays deterministic and makes no LLM call; the translation is produced by stage 2's existing text-only call.
- When stage 2 yields no `card_name_en`, stage 3 behaves exactly as before.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `picture-service-derived-attributes`: stage 2 additionally derives `card_name_en`.
- `picture-service-catalog-matching`: the card-name score component considers the English name (`card_name_en`) in addition to `card_name`, taking the better similarity.

## Impact

- `picture_service/src/picture_service/prompts.py` (stage 2 prompt) and `card_analysis_stage_3.py` (`match_catalog`, `resolve_card`, `_score_card`); tests in `picture_service/tests/test_card_analysis_stage_3.py`.
- No proto, CatalogService or Web change. `card_name_en` is stored in the sidecar's Derived section like any other derived attribute; existing sidecars gain it only when re-analyzed.
- Match quality now depends on the model returning a correct English name.
