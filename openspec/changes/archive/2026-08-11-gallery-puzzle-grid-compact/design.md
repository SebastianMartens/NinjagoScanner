## Context

Gallery renders card tiles inline in `Gallery.razor` (no separate tile
component), with grid/tile CSS in `app.css`. Puzzle sections already get a
distinct grid class (`gallery-grid-puzzle`, 3 columns) via
`GallerySection.IsPuzzle`, computed from a category-name prefix check. There
is no `CardType`/category enum anywhere else in the codebase — puzzle-ness is
purely this one boolean flag already flowing into the markup. See
proposal.md - Why for the motivation.

## Goals / Non-Goals

**Goals:**
- Pick concrete values for the puzzle grid's gap and tile corner radius, and
  decide what a puzzle placeholder tile shows instead of the card name.
- Keep every change scoped to markup/CSS already conditioned on
  `section.IsPuzzle` — no new data model or flag.

**Non-Goals:**
- Changing the lightbox (enlarged photo view) - it keeps showing the card
  name, per proposal.md - Impact.
- Introducing a `CardType` enum or otherwise formalizing puzzle detection -
  out of scope; the existing string-prefix check is untouched.
- Extracting the inline tile markup into a component - not needed for this
  change and would enlarge the diff unnecessarily.

## Decisions

**Puzzle placeholder fallback shows the card number, not blank.**
The existing placeholder tile has nothing to display once the name is
removed (no photo, no name). Alternatives considered: leave the tile
visually empty (just the diagonal-stripe background), or show a generic
"?" glyph. Showing `card.CardNumber` was chosen because the caption below a
puzzle photo tile already carries "Nr. X" as the only visible text, so
placeholders stay visually consistent with photo tiles in the same grid
and pieces remain distinguishable before they're scanned.

**Puzzle photo tiles drop the caption element entirely.**
Revised after review: the caption bar (`gallery-tile-caption`) is omitted
outright for puzzle photo tiles rather than kept and trimmed to "Nr. X" -
a puzzle tile shows only the image itself, matching how the puzzle
placeholder already shows only its graphic (with the number inside the
graphic, not in a separate caption). This is a stronger cut than the
original decision here, which kept a shared caption-shaped element across
photo/placeholder for layout consistency; that consistency turned out not
to matter once neither branch renders a caption at all.

**Gap: `0.35rem` for puzzle grids, unchanged `1rem` for standard grids.**
The proposal asks for "a small gap, not big" rather than zero, matching a
real puzzle's near-touching pieces without tiles overlapping their own
shadow/border. `0.35rem` is applied only on `.gallery-grid-puzzle` (both the
base rule and the existing narrow-viewport override), so `.gallery-grid`'s
shared `gap: 1rem` continues to serve standard grids untouched.

**Corner radius: `4px` for puzzle tiles via a new `.gallery-tile-puzzle`
class, unchanged `16px` for standard tiles.**
Rather than parameterizing `.gallery-tile`'s existing `border-radius`, a
second class is added and applied alongside `gallery-tile` on puzzle tiles
only (`class="gallery-tile gallery-tile-puzzle gallery-tile-photo"`, etc.),
mirroring how `gallery-grid-standard`/`gallery-grid-puzzle` already
coexist with the shared `gallery-grid` class. This keeps the standard tile
rule untouched and the puzzle override local to one small class.

## Risks / Trade-offs

- [Puzzle placeholder now shows a number where it used to show a full name]
  → Acceptable per proposal.md - What Changes; number is the same piece of
  data already shown for photo tiles' captions, so no new data plumbing is
  needed and nothing is lost that isn't visible elsewhere on the same tile
  set.
- [Two Razor branches (photo/placeholder) each need their puzzle-vs-standard
  conditional for caption content] → Small, local `if (section.IsPuzzle)`
  checks already exist for the grid class; extending the same pattern to
  caption content keeps the change mechanically consistent with existing
  code rather than introducing a new abstraction.
