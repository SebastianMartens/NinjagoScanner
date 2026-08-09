## Context

`CardCatalogService.GetReviewGroupsAsync` (`NinjagoScanner.Web/Services/CardCatalogService.cs:311-371`) currently builds review groups by keying photos on the raw, un-normalized sidecar `SetName`/`CardNumber` strings (only `ToUpperInvariant`-ed), and routes a photo to the catch-all bucket solely when its `SetName` doesn't match a known series name — it never validates `CardNumber` against the catalog.

The same file already has normalized matching for the collection page: `NormalizeSeriesKey`/`NormalizeCardNumber` (lines ~569-630), which strip underscores/dashes/spaces, collapse "Next Level" → "NL", and strip leading zeros from purely numeric card numbers. `LoadCardsFromCatalogServiceAsync` (line 418) already loads the full catalog card list (series, category, card number, card name, sort order) via gRPC `ListAllCards`, which is what the collection page uses to build its normalized lookup.

Card identity is now confirmed as `(series name, card number)` only (see the archived `catalog-card-identity-series-number` change) — category plays no role in identity or lookup.

## Goals / Non-Goals

**Goals:**
- Reuse the existing normalized catalog lookup (not a second, divergent implementation) so review-page grouping and collection-page ownership matching treat sidecar variants the same way.
- Resolve and expose a catalog card name per matched review group for header display.
- Keep the review page's existing "photo-driven, not catalog-driven" grouping model — no enumeration of unmatched catalog cards.

**Non-Goals:**
- Reworking `NormalizeSeriesKey`/`NormalizeCardNumber` themselves.
- Changing the collection page, `CatalogRepository`, or any gRPC contract.
- Persisting the resolved catalog card name onto the sidecar — it stays a read-time lookup, computed fresh each time groups are built.

## Decisions

**Reuse the collection page's normalized-key lookup for review grouping, rather than introducing a separate matching rule.** Building a normalized `Dictionary<(string series, string cardNumber), CatalogCardEntry>` from `LoadCardsFromCatalogServiceAsync`'s output (keyed the same way `NormalizeSeriesKey`/`NormalizeCardNumber` already key collection-page lookups) lets `GetReviewGroupsAsync` do a single dictionary lookup per photo: found → group key is the catalog entry's canonical `(SeriesName, CardNumber)` and its `CardName` is attached to the group; not found → photo goes to catch-all. This is a small, local change confined to `GetReviewGroupsAsync` and keeps the two pages' notion of "does this photo belong to this catalog card" consistent, which directly fixes the raw-string-grouping inconsistency noted in the proposal.

Alternative considered: catalog-led grouping (enumerate `ListAllCards`, left-join sidecars). Rejected — see proposal.md "Why"/"What Changes"; it would change the review page from a scan inbox into a catalog browser and contradicts the spec's existing "a group exists iff a photo produces it" invariant, which this change intentionally preserves (only tightening what "produces it" means).

**Catch-all routing now requires a full catalog match (series AND card number), not just a known series name.** Previously a photo with a valid series but a typo'd/unknown card number got its own single-photo group; now it lands in catch-all alongside blank/unknown-series photos, since it has no catalog card to resolve a name for and no group header would make sense without one. This is the behavior change captured in the modified "Groups are ordered by known series order, then card number" requirement.

**`CardReviewGroup` gets a new `CardName` field (nullable), populated only for matched groups.** The catch-all group leaves it `null`; `Review.razor`'s `GroupTitle` renders it only when present.

## Risks / Trade-offs

- [Risk] Grouping now depends on the full catalog card list being loaded (`ListAllCards`), not just `ListSeries` — a CatalogService outage during `GetReviewGroupsAsync` would fail the whole review page rather than degrading gracefully. → Mitigation: `LoadCardsFromCatalogServiceAsync` is already a hard dependency of the collection page today, so this doesn't introduce a new failure mode, just extends an existing one to a second page.
- [Risk] Previously-separate single-photo groups (valid series, bad card number) silently merge into catch-all, which could make a reviewer overlook a photo that used to be easy to spot as its own group. → Mitigation: catch-all remains fully visible and reviewable, per the proposal's explicit "unmatched photos are never hidden" requirement; this is a display grouping change only, not a data loss.
