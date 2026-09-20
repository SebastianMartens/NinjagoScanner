## 1. Stage 2

- [x] 1.1 Add `card_name_en` to the derived-attributes prompt in `prompts.py` and fix the `card_name` hint typo

## 2. Stage 3

- [x] 2.1 Read `card_name_en` in `match_catalog` and pass it through `resolve_card` / `_score_card`
- [x] 2.2 Score a card name as the max similarity over `card_name` and `card_name_en`; update the module docstring

## 3. Tests

- [x] 3.1 German name matches an English-only catalog through `card_name_en`
- [x] 3.2 No translation keeps the previous (failed) behaviour
- [x] 3.3 A catalog German name still matches via `card_name`
- [x] 3.4 Translation does not override the class-mismatch rule
- [x] 3.5 Run `uv run pytest` in `picture_service/`

## 4. Verify

- [ ] 4.1 Re-analyze a few real German card photos and confirm `card_name_en` is sensible and the card resolves
