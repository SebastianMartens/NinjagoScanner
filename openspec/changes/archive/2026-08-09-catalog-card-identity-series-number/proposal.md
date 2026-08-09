## Why

The catalog previously allowed the same series + card number to be assigned to two different cards in different categories — e.g. Serie 2 #4 "Ultra Kai Airjitzu" existed once under `Good_Guys` and again under `XXL_Cards`. This was documented as a deliberate, accepted ambiguity: `catalog-service-card-catalog`'s spec carried a "RESOLVED" comment concluding category must stay part of a card's identity, and `web-collection-list` / `web-card-review-flow` both carry a "Known limitation" paragraph about series+card-number not being a safe matching key.

The XXL/XXXL card numbers in `series_2.json`, `series_3.json`, and `series_4.json` have now been re-prefixed (e.g. `4` → `XXL4`) so every number is series-unique regardless of category. Scanning all 16 `cardInfos/series_*.json` files confirms this: 3915 total card entries, zero duplicate `(series name, card number)` pairs, both within any single series and across the whole catalog. Series name + card number can now serve as a card's identity key, so the specs and glossary built around the old ambiguity are stale and the code paths that defensively routed around it can be simplified.

## What Changes

- Redefine a catalog card's identity as `(series name, card number)`. Category remains a real, displayed/filterable attribute of a card but is no longer required to distinguish two catalog cards.
- Remove the "Known limitation" caveats from `web-collection-list` (`OwnedCopies` matching) and `web-card-review-flow` (review grouping): a photo's `SetName` + `CardNumber` match now identifies exactly one catalog card, never more than one.
- Update `catalog-service-card-catalog`'s "Duplicate card entries are collapsed" requirement to describe identity as series + card number, dropping the now-unneeded category/card-name qualifiers from that requirement's own wording (the underlying raw-data dedup safety net stays; see design.md).
- Update `GLOSSARY.md`'s **Card**, **Category**, **Card Number**, and **Owned Copies** entries to reflect that series + card number is now a sufficient, unique identifier, and that category is descriptive/grouping metadata rather than an identity component.
- Simplify `CardCatalogService.GetCollectionCardDetailsAsync` (Web) to look up a catalog card by series + card number instead of requiring category and card name as disambiguating guards, since they're no longer needed to get a unique match.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `catalog-service-card-catalog`: card identity is now series name + card number; the "Duplicate card entries are collapsed" requirement's wording changes accordingly.
- `web-collection-list`: removes the "Known limitation" note on `OwnedCopies` — series + card number matching is now always unambiguous.
- `web-card-review-flow`: removes the "Known limitation" note on review grouping — a series + card number group now always corresponds to exactly one catalog card.

## Impact

- **Data**: already fixed (uncommitted changes to `series_2.json`, `series_3.json`, `series_4.json`), confirmed unique by this change's investigation.
- **Docs**: `openspec/GLOSSARY.md` (Card, Category, Card Number, Owned Copies entries) and the stale "RESOLVED" comment in `openspec/specs/catalog-service-card-catalog/spec.md`.
- **Web**: `NinjagoScanner.Web/Services/CardCatalogService.cs` (`GetCollectionCardDetailsAsync` signature/lookup), `NinjagoScanner.Web/Components/Pages/Collection.razor` (call site passing category/card name).
- **CatalogService**: no functional change required — `CatalogRepository`'s existing raw-entry dedup key (series, category, card number, card name) is a strict superset of the new identity key, so it never merges two genuinely distinct cards; it stays as a defensive safety net against literal duplicate JSON rows (see design.md).
- **Tests**: `NinjagoScanner.CatalogService.Tests` and any Web tests asserting the old category-qualified lookup signature.
