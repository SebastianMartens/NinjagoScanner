## Context

See proposal.md - Why/What Changes for motivation. This section covers the two things that make
the implementation non-trivial: the recursive JSON parser's behavior, and the actual
category-to-class mapping across all 16 series files.

`NinjagoScanner.CatalogService/Catalog/CatalogRepository.cs`'s `EnumerateCardEntries` walks each
series' JSON recursively. It has no fixed schema for "a category" - any object property that
isn't in `ShouldTrackCategory`'s exclusion list (`Jahr`, `SortOrder`, `Logo`, `Thema`,
`Besonderheiten`, `Sondereditionen`, `Kategorien`, anything starting with `Serie`) is treated as
one more segment of a nested category path, and the walker recurses into its value regardless of
whether that value is an object or an array. A leaf is recognized only by shape: an object with
both `Karten-Nr.` and an object-valued `Name`. This is why today's flat
`"Heroes": [ {"Karten-Nr.": 1, "Name": {...}}, ... ]` works with zero per-category configuration -
`Heroes` becomes a path segment, then the walker recurses straight into the array and finds leaf
objects.

## Goals / Non-Goals

**Goals:**
- Add `Class` to every category entry without changing any existing `Category` label the parser
  produces today (card counts, category display strings, and `sort_order` must be unchanged for
  every existing card).
- Keep the reshape mechanical and low-risk to apply across 16 files.

**Non-Goals:**
- Getting every ambiguous category's class exactly right on the first pass - the mapping below is
  a deliberate first cut per the user's direction; it can be revised later without another
  proposal (`.openspec.yaml`/spec changes aren't needed to tweak a single category's declared
  class in a `cardInfos/*.json` file, only to change the fixed class *set*).
- Normalizing or deduplicating the underlying `Category` taxonomy itself - that stays exactly as
  it is today.

## Decisions

### Reshape: wrap each category's card array, add two new reserved property names

```json
"Heroes": { "Class": "character", "Karten": [ {"Karten-Nr.": 1, "Name": {...}}, ... ] }
```

`Karten` (not `Cards`) matches the file's existing German property naming (`Jahr`,
`Besonderheiten`, `Sondereditionen`, `Kategorien`, `Karten-Nr.`).

Categories that contain nested sub-categories instead of a direct card array (`Puzzle_Cards`,
which holds one array per puzzle, and `Character_Cards`, which holds `Good_Guys`/`Villains`)
declare `Class` once on that top-level category object and do **not** get a `Karten` wrapper:

```json
"Puzzle_Cards": { "Class": "puzzle-piece", "Puzzle_One": [ ... ], "Puzzle_Two": [ ... ] }
```

The parser passes the nearest enclosing `Class` down the walk, so every card below inherits it;
a card reached with no enclosing `Class` fails loading (see "Fail-fast" below). The "same
category → same class" invariant is therefore checked on the top-level category name.

Both `Class` and `Karten` **must** be added to `ShouldTrackCategory`'s exclusion list. Without
this, the walker treats `Class` as an empty nested category (harmless but pointless - its value is
a string, so recursion yields nothing) and, critically, treats `Karten` as a real nested category
segment - every card under `Heroes` would end up labeled `"Heroes / Karten"` instead of `"Heroes"`,
silently corrupting every category label in the catalog. This is the single highest-risk step in
the change; a test against the existing card-count/category-label snapshot (see Risks) is the
guard for it.

Alternative considered: a separate `cardInfos/card_classes.json` lookup file, referenced by all
series files, avoiding both the reshape and the parser risk entirely. Rejected per explicit user
direction (explore-mode conversation) in favor of inline-per-file, accepting the duplication and
parser-change risk for simplicity of "the class lives right next to the category it describes."

### Fixed class set and category mapping

Seven classes, force-fit for every category that exists in `cardInfos/*.json` today. Categories
whose name is really about a base depiction (Heroes, Action_Cards, Vehicle_Cards, Puzzle_Cards,
Trap_Cards, Limited_Edition_Cards, Art_Cards) map cleanly. Roughly a third of the categories encode
a **format or rarity tier** layered on top of a depiction (`Mega_*`, `XXL_*`, `XXXL_*`, `Ultra_*`,
`Platinum_Cards`, `Gold_Cards`, `Ruby_Cards`) rather than a depiction itself - per the user's
explicit direction, these are force-fit into their closest depiction-based class now, and format/
rarity is expected to become a *separate*, runtime-detected attribute in the picture-service
pipeline (see the companion `picture-service-staged-analysis-pipeline` change) rather than a
distinct catalog `Class`. The mappings below marked "best guess" are the ones most likely to be
revisited.

| Category | Class | Notes |
|---|---|---|
| Heroes, Villains, Good_Guys | character | |
| Character_Cards | character | |
| Villains_Level_2 | character | |
| Mega_Good_Cards, Mega_Villains_Cards, Mega_Cards | character | best guess |
| XXL_Cards, XXXL_Cards | character | best guess - oversized character portraits |
| Shattered_Cards | character | best guess |
| Shadow_Cards | character | best guess |
| Action_Cards | action | |
| Evil_Magic_Cards, Spinjitzu_Cards | action | best guess - special-move cards |
| Mega_Duel_Cards, Ultra_Duel_Cards, Ultra_Double_Cards | action | best guess - two characters facing off |
| Ultra_Good_vs_Evil_Cards, Ultra_Good_vs_Evil_Double_Cards | action | best guess |
| Epic_Battle_Cards, Level_Up_Cards | action | best guess |
| Vehicle_Cards, Hero_Vehicle_Cards, Villain_Vehicle_Cards | vehicle | |
| Dragon_and_Dragon_Master_Cards | vehicle | best guess - dragon-as-mount is the dominant visual |
| Puzzle_Cards | puzzle-piece | |
| Trap_Cards | trap | |
| Limited_Edition_Cards | limited edition | |
| Platinum_Cards, Gold_Cards, Ruby_Cards | limited edition | best guess - rarity tiers |
| Art_Cards | art | |
| Additional_Cards | character | best guess - `Serie_1`-only, ambiguous, default bucket |

This table is the authoritative source for tasks.md's per-file editing pass; adjust individual
cells during implementation if a closer look at a specific series' cards suggests a better fit -
that does not require touching this design doc, only the affected `cardInfos/*.json` file(s) and
keeping the "same category → same class everywhere" invariant intact.

### Fail-fast on a missing Class

Per the new spec requirement, a category entry with no declared `Class` fails catalog loading
rather than defaulting to some fallback class - a silently-wrong default class is worse than a
startup failure that points directly at the incomplete file.

## Risks / Trade-offs

- **[Risk] The parser change silently mislabels every category if `Class`/`Karten` aren't added
  to the exclusion list** → Mitigation: add a snapshot-style test that asserts the full set of
  `(SeriesName, Category, CardNumber, CardName)` tuples `ExtractSeriesCards` produces is
  byte-for-byte identical before and after the reshape, for every series file - run it as the
  first step of implementation, before adding `Class` data.
- **[Risk] Inline-per-file duplication drifts** (e.g. `Heroes` declared `character` in one file,
  accidentally `action` in another) → Mitigation: the new "every category maps to exactly one
  class" regression test (see proposal.md).
- **[Trade-off] Several class assignments are genuine judgment calls** (see "best guess" rows
  above) → Accepted per explicit user direction; revising a single cell later is cheap.

## Migration Plan

Additive data/contract change, no runtime data to migrate (CatalogService reloads `cardInfos/`
from disk/build output; no persisted state elsewhere depends on the old shape). Rollout order:
1. Update `CatalogRepository.cs` parsing (exclusion list) and add the snapshot-equality test.
2. Reshape `cardInfos/*.json` files one at a time, running the snapshot test after each.
3. Add `Class` values per the mapping table, running the new class-consistency test.
4. Extend `CatalogContracts.cs`, `catalog.proto` (both copies), and `CardCatalogGrpcService.cs`.
No rollback concerns beyond reverting the commit - PictureService does not consume `Class` until
the companion change is implemented, so this can ship and sit unused without breaking anything.
