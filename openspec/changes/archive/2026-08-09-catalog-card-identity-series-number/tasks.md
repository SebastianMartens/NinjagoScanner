## 1. Documentation

- [x] 1.1 Replace the stale "RESOLVED" HTML comment in `openspec/specs/catalog-service-card-catalog/spec.md` (between `## Purpose` and `## Requirements`) with a short note that series + card number is now confirmed unique catalog-wide, or remove the comment entirely.
- [x] 1.2 Rewrite `openspec/GLOSSARY.md`'s **Card** entry: identity is `(series, card number)`; drop the "not sufficient to identify a card" language.
- [x] 1.3 Rewrite `openspec/GLOSSARY.md`'s **Category** entry: it's a grouping/display attribute, no longer part of a card's identity; remove the Serie 2 #4 ambiguity example (or reframe it as historical/resolved).
- [x] 1.4 Rewrite `openspec/GLOSSARY.md`'s **Card Number** entry: unique within a series (not "only within a category").
- [x] 1.5 Rewrite `openspec/GLOSSARY.md`'s **Owned Copies** entry: remove the note about a photo being counted as owned by two cards sharing series+card number.

## 2. Code

- [x] 2.1 Simplify `CardCatalogService.GetCollectionCardDetailsAsync` (`NinjagoScanner.Web/Services/CardCatalogService.cs`) to accept and match on `series` + `cardNumber` only, dropping the `category`/`cardName` parameters.
- [x] 2.2 Update the call site in `NinjagoScanner.Web/Components/Pages/Collection.razor` (`GetCollectionCardDetailsAsync(card.Series, card.Category, card.CardNumber, card.CardName)`) to the new two-argument form.

## 3. Tests

- [x] 3.1 Update or add a `NinjagoScanner.CatalogService.Tests` case asserting `(series, card number)` uniqueness holds across the shipped `cardInfos/*.json` data (regression guard against reintroducing a collision).
- [x] 3.2 Update any Web-side test/usage asserting the old 4-argument `GetCollectionCardDetailsAsync` signature. (No Web test project exists in this repo; the only usage was the `Collection.razor` call site already updated in 2.2.)

## 4. Data cleanup (flagged, not required for this change)

- [x] 4.1 Fix the mislabeled split entry in `series_4.json`: `XXXL149` currently reads `"Name": "Fire Dragon"` but should read `"Ice Dragon"` (it was split from the original `{"Karten-Nr.": "148, 149", "Name": "Ice Dragon"}` entry alongside `XXXL148`).
