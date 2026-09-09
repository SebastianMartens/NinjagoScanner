## Context

See proposal.md - Why. The standard grid is `.gallery-grid-standard` in
`NinjagoScanner.Web/wwwroot/app.css`:

```css
.gallery-grid-standard {
    grid-template-columns: repeat(5, 1fr);
}
```

Two things combine to make browser zoom-out useless today:
1. `repeat(5, 1fr)` always divides available track space into exactly 5
   equal columns — zooming out shrinks all 5 proportionally instead of
   fitting more of them.
2. The grid's ancestor `.gallery-page` has `max-width: 1500px`. Browser
   page zoom (Ctrl+Mouse Wheel) rescales how many CSS pixels fit in the
   viewport, but it does not change fixed CSS lengths like `max-width`, so
   even a responsive grid underneath a 1500px cap could never grow past
   what 1500px allows.

Both need to change together: the column rule must respond to available
width instead of being a fixed count, and the container's width ceiling
must be raised far enough that 15 columns is actually reachable when the
user zooms out.

## Goals / Non-Goals

**Goals:**
- Standard (non-puzzle) grid shows more than 5 tiles per row once more
  horizontal space is available (zoom-out or a wide viewport), capped at 15.
- Tile size at the default zoom/viewport stays visually the same as today
  (5 tiles per row at the page's current typical width).
- Tiles never shrink below a usable minimum size, even at 15-per-row.

**Non-Goals:**
- No change to the puzzle sub-group grid (stays a fixed 3 columns).
- No change to the `@media (max-width: 700px)` mobile breakpoint's fixed
  2-column layout — narrow phone viewports aren't the zoom-out use case
  this change targets, and forcing a responsive min-width there risks
  dropping to 1 column on small screens.
- No JavaScript-based zoom or resize detection — pure CSS.

## Decisions

### Use `grid-template-columns: repeat(auto-fill, minmax(<min>, 1fr))`
Standard CSS idiom for "as many equal columns as fit, each at least
`<min>` wide." This replaces the fixed `repeat(5, 1fr)` and is what makes
the grid grow when more width becomes available, without any JS.

Alternative considered: JS `ResizeObserver` setting an explicit column
count. Rejected — CSS grid already does this declaratively; adding JS
would be unnecessary complexity for a layout-only change.

### Pick `<min>` so today's width yields 5 columns
`.gallery-page` content width today is `1500px - 2*1.5rem padding ≈
1452px`. With the existing `1rem` (16px) gap and 5 columns:
`(1452 - 4*16) / 5 ≈ 277px` per tile. Use `min-width: 275px` so the
default appearance is unchanged at today's typical container width.

### Raise `.gallery-page`'s `max-width` so 15 columns is reachable
`auto-fill`/`minmax` alone can't cap the column count — it will keep
adding columns for as much width as the container offers. To get "more
columns when zooming out, capped at 15" without extra CSS tricks, size
the container's ceiling to exactly what 15 columns of the chosen tile
width need, so the grid mathematically cannot exceed 15 regardless of how
far the browser is zoomed out or how wide the monitor is:

`15 * 275px + 14 * 16px = 4349px` tile+gap width, plus the page's `3rem`
(48px) horizontal padding → round up to `max-width: 4400px`.

This is a larger change than it sounds: raising `.gallery-page`'s
`max-width` from 1500px to 4400px means a very wide monitor at 100% zoom
(not just a zoomed-out normal monitor) will also show more than 5 columns,
since the container now grows with any available width, not just
zoom-driven width. This is treated as a desirable side effect (matches
proposal framing: "responsive layout that fits as many tiles per row as
the available width allows") rather than a regression — today's fixed
1500px cap already produces wide unused side margins on large monitors,
which this incidentally improves.

Alternative considered: keep `max-width: 1500px` and just swap in
`auto-fill`/`minmax` — rejected because it would silently cap the grid at
~5 columns forever (see Context), defeating the change's purpose.

Alternative considered: remove `max-width` entirely (unbounded) —
rejected because nothing would then cap the column count at 15; an
ultrawide monitor zoomed out fully could exceed it.

### Leave the mobile breakpoint's fixed 2-column rule as-is
`@media (max-width: 700px) { .gallery-grid-standard { grid-template-columns:
repeat(2, 1fr); } }` already overrides the base rule and continues to do so
unchanged — narrow real-device viewports keep today's exact behavior.

## Risks / Trade-offs

- [Wide-monitor users see a layout change at 100% zoom, not just
  zoomed-out users] → Acceptable per the "Raise max-width" decision above;
  the container was arbitrarily narrow before, not intentionally
  constrained to 5 columns as a design choice.
- [4400px max-width is a large jump from 1500px, easy to mistype or
  mis-tune] → Value is derived directly from the 15-column /
  275px-min-tile math in this doc; if the min tile width ever changes,
  recompute `max-width` the same way rather than picking an arbitrary
  round number.
- [`auto-fill` vs `auto-fit`] → `auto-fill` was chosen over `auto-fit`
  because with `1fr` tracks, both behave identically once tiles exist;
  `auto-fit` only differs when a row would otherwise be empty, which does
  not apply to a filled card grid. Either works; `auto-fill` is the more
  conventional default for this pattern.
