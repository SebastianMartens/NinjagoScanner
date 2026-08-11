## Context

See proposal.md - Why/What Changes for motivation. This section covers the
current code the design touches.

Card-number ordering is implemented independently in four places today:

- `NinjagoScanner.CatalogService/Catalog/CatalogRepository.cs`'s private
  `ToSortKey(string)`, used once in `LoadSnapshot()` to order the `Cards`
  snapshot behind `ListAllCards`. Input is already normalized (uppercase,
  non-alphanumeric stripped, leading zeros removed) by the time it reaches
  `ToSortKey`, since `ExtractSeriesCards` normalizes each card's number first.
- `NinjagoScanner.Web/Services/CardCatalogService.cs`'s private
  `ToSortKey(string)`, a near-identical duplicate, used in
  `GetCollectionOverviewAsync` and `GetGalleryCardsAsync` to re-sort cards
  after joining catalog data with photo ownership. Input here is also already
  normalized (via that file's own `NormalizeCardNumber`).
- `NinjagoScanner.Web/Components/Pages/Collection.razor`'s
  `BuildCardNumberSortKey`/`TryExtractNumber`, used only as the QuickGrid
  `SortBy` for the manually-clickable "Nr." column. This one extracts the
  first digit run found anywhere in the string (not necessarily a leading
  prefix) and doesn't group numeric before alphanumeric by design.
- `NinjagoScanner.Web/Components/Pages/CardsTable.razor`'s row grouping,
  which orders by `card.CardNumber` with a plain
  `StringComparer.OrdinalIgnoreCase` — no numeric awareness at all. Its input,
  `CardListItem.CardNumber`, is populated via `NormalizeNullable` only (trimmed,
  nulled if blank) — **not** run through the uppercase/strip-non-alphanumeric
  normalization the other three call sites rely on, since it comes straight
  from a photo sidecar rather than the catalog.

All four independently hardcode (or, for CardsTable, entirely lack) the same
concept: "numeric card numbers sort before alphanumeric ones." Only `LE` and
`XXL` are recognized as alphanumeric prefixes in the two `ToSortKey` copies;
anything else falls into an undifferentiated last group.

## Goals / Non-Goals

**Goals:**
- One canonical rule, generalized to any alphabetic prefix, not just `LE`/`XXL`.
- Within the Web project, one shared implementation so the three Web call
  sites cannot drift out of sync with each other again.
- Make the rule robust to unnormalized input, since CardsTable's call site
  doesn't pre-normalize.

**Non-Goals:**
- No shared library between `NinjagoScanner.CatalogService` and
  `NinjagoScanner.Web` — they're independently deployed processes with no
  existing shared project, and introducing one just for a sort-key function
  is disproportionate. The two projects keep independent (but now equivalent)
  implementations, same as today.
- No change to how card numbers are normalized/stored (`NormalizeCardNumber`,
  sidecar format, proto contracts) — only to the ordering derived from them.

## Decisions

**Canonical algorithm**: normalize the input (uppercase, strip non-alphanumeric
characters — reusing each project's existing normalization building blocks),
then:
1. If the whole value is digits → numeric group, key `"0-{value:D6}"`.
2. Else if it matches `^(?<prefix>[A-Za-z]+)(?<number>\d+)$` → alphanumeric
   group, key `"1-{prefix}-{number:D6}"` (prefix compared as text first, so
   `LE` sorts before `OTHER` sorts before `XXL`; number zero-padded so it
   compares numerically within the same prefix).
3. Else → fallback group, key `"9-{value}"`, same as today's catch-all.

Concatenating the group digit ahead of the rest means a plain
`StringComparer.Ordinal(IgnoreCase)` on the resulting key reproduces the
intended three-tier order without a custom `IComparer`.

*Alternative considered*: keep enumerating known prefixes (add `OTHER`,
etc., to the existing if/else chain). Rejected — the proposal's whole point is
that new prefixes should sort correctly without a code change every time one
appears in the catalog.

**Shared Web helper, duplicated CatalogService copy**: add
`NinjagoScanner.Web/Services/CardNumberSorting.cs` (internal static class) and
have `CardCatalogService.cs`, `Collection.razor`, and `CardsTable.razor` all
call `CardNumberSorting.BuildSortKey(string?)`. `CatalogRepository.cs` keeps
its own private `ToSortKey`, rewritten in place to the same algorithm — see
Non-Goals for why no cross-project sharing.

**The shared helper normalizes its own input** rather than assuming
pre-normalized data, specifically so `CardsTable.razor` can pass
`CardListItem.CardNumber` (sidecar-sourced, not catalog-normalized) directly
without a separate normalization step at that call site.

## Risks / Trade-offs

- [Risk] `"OTHER1"`-style card numbers (any prefix other than `LE`/`XXL`
  already present in real data) will visibly move from the trailing
  catch-all group into the alphanumeric group, changing their position in
  Collection, the table, and Gallery → *Mitigation*: this is the explicit,
  intended fix (see proposal.md's **BREAKING** note); the existing hardcoded
  test asserting the old placement is updated as part of this change.
- [Risk] `CardsTable.razor`'s row grouping previously did no digit-aware
  sorting at all ("10" before "2"); fixing it changes existing, if arguably
  already-wrong, visible ordering on that page → *Mitigation*: accepted per
  the user's explicit "always, in all places" scope decision; it brings that
  page in line with the same rule Collection and Gallery already followed for
  their catalog-derived orderings.
