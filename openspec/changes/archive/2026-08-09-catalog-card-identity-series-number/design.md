## Context

See proposal.md - Why for the motivation. Relevant current state:

- `CatalogRepository.ExtractSeriesCards` (CatalogService) dedups raw JSON card entries per series using the key `seriesName|category|normalizedNumber|cardName` (a 4-tuple), and independently verified (this change) that the live data now has zero `(series, card number)` collisions across all 16 `cardInfos/series_*.json` files (3915 entries).
- `CardCatalogService.GetCollectionCardDetailsAsync` (Web) requires `series`, `category`, `cardNumber`, and `cardName` as lookup parameters, added when a series+cardNumber match could be ambiguous. `BuildOwnershipKey` (same file, used for `OwnedCopies`) already keys on series+cardNumber only.
- `openspec/specs/catalog-service-card-catalog/spec.md` carries a "RESOLVED" HTML comment (outside any requirement) documenting the old ambiguity by example (Serie 2 #4). It sits between `## Purpose` and `## Requirements`, so it is not touched by the delta-spec sync mechanism and needs a direct edit.
- `openspec/GLOSSARY.md`'s Card / Category / Card Number / Owned Copies entries assert series+card-number is *not* a sufficient identifier. These are living documentation, not versioned specs, so they're updated directly rather than through a delta.

## Goals / Non-Goals

**Goals:**
- Make the documentation (specs + glossary) match the now-true invariant: `(series name, card number)` uniquely identifies a catalog card.
- Remove the one piece of production code (`GetCollectionCardDetailsAsync`) that was written specifically to route around the old ambiguity, now that it's unnecessary.

**Non-Goals:**
- Changing `CatalogRepository`'s raw-entry dedup key. It stays a 4-tuple (see Decisions below) — this is a robustness property, not a spec requirement, and doesn't need to shrink just because the key that's now sufficient for identity got smaller.
- Removing `Category` from the data model, proto contracts, or UI. It remains a genuine, displayed/filterable/grouping attribute of a card (see GLOSSARY.md's Category entry) — only its role in *identity* changes.
- Adding runtime validation/logging that would flag a future `(series, card number)` collision. The catalog is manually maintained and trusted, consistent with how `SortOrder` is handled (see the `fix-series-sort-order` change) — out of scope here unless the user asks for it separately.

## Decisions

**Keep `CatalogRepository`'s raw dedup key as the 4-tuple (series, category, card number, card name) rather than shrinking it to (series, card number).**
Shrinking it would change behavior, not just documentation: if two raw JSON entries ever again share series+card number but differ in category or name (a data bug), the narrower key would silently keep only the first and drop the second, whereas the current 4-tuple keeps both as distinct entries (surfacing the bug instead of hiding it, since `ListAllCards`'s only exclusion rule is "no card number or no card name" — a resurfaced duplicate is visible, not silently discarded). The 4-tuple is strictly more conservative: it can never merge two truly distinct cards, and it still collapses literal formatting-only duplicates (e.g. `"1"` vs `"01"`, same name), which is the scenario the existing test (`GetSnapshot_CollapsesIdenticalCardEntries_IntoOne`) covers. No code or test change needed here.

**Simplify `GetCollectionCardDetailsAsync(series, category, cardNumber, cardName)` to `GetCollectionCardDetailsAsync(series, cardNumber)`.**
Category and card name were only ever guards against an ambiguous match; with the identity invariant confirmed, a series+cardNumber match is already unique, so the extra parameters are redundant. Simplifying removes a call site (`Collection.razor`) that has to thread `category`/`cardName` through just to satisfy a signature. Alternative considered: leave the signature as-is since it still works — rejected because carrying dead disambiguation parameters through a public-ish service method invites the next reader to assume they're still load-bearing, and the proposal already documents the invariant they were protecting against.

**Update the stale "RESOLVED" comment in `catalog-service-card-catalog/spec.md` and the four GLOSSARY.md entries as direct edits, not through the delta-spec mechanism.**
The comment lives outside any `### Requirement:` block and GLOSSARY.md isn't a versioned spec capability at all, so `openspec sync`/archive wouldn't touch either even after this change's delta specs are archived. Both get rewritten directly as part of this change's task list.

## Risks / Trade-offs

- [A future catalog data edit reintroduces a series+card-number collision without anyone noticing, since there's no automated check] → Accepted per Non-Goals; the 4-tuple dedup in `CatalogRepository` still means a genuine collision produces two visible, non-silently-merged `ListAllCards` entries rather than data loss, so the failure mode is "visible duplicate" not "silent corruption."
- [`series_4.json`'s uncommitted fix mislabels one split card: `{"Karten-Nr.": "148, 149", "Name": "Ice Dragon"}` was split into `XXXL148 "Ice Dragon"` and `XXXL149 "Fire Dragon"` — the second name looks like a copy-paste error from the adjacent `XXL_Cards` entries] → Out of scope for this change (it's a data-content typo, not a uniqueness problem — `XXL149` is already unique), but flagged to the user directly; worth a one-line fix in `series_4.json` before this change is archived.

## Migration Plan

Docs-and-one-method change; no data migration, no proto/contract change, no rollback complexity. Sequence: land the catalog JSON fix (already uncommitted) → apply this change's code simplification and doc rewrites → archive.
