## Context

See proposal.md - Why. The Review page's series-reassignment buttons (`Review.razor`, `review-series-buttons` block) currently render `knownSeries` (`IReadOnlyList<string>` from `CardCatalogService.GetKnownSeriesAsync()`, e.g. `"Serie 2"`, `"Serie 5NL"`) as plain-text pill buttons (`.review-btn`, `.review-series-buttons` in `app.css`). Logo image assets already exist at `NinjagoScanner.Web/wwwroot/images/Series{N}[NL]_klein.jpg` and are already served by the existing `app.MapStaticAssets()` call - no new static-file wiring is needed.

## Goals / Non-Goals

**Goals:**
- Show a small logo icon inline on a reassignment button when one is available for that series.
- Keep the mechanism trivial to hand-maintain: adding a series' logo later is a one-line dictionary entry, not a filename-convention or JSON change.
- Never show a broken image or placeholder box for a series without a mapped logo.

**Non-Goals:**
- Populating the mapping for every series - only `Serie 2` is seeded as a worked example; the rest is manual follow-up outside this change.
- Deriving the image path from the series name via naming convention (rejected - see Decisions).
- Reading or displaying the CatalogService `Logo` JSON field (explicitly out of scope per the proposal).
- Touching `Collection.razor`, `CatalogService`, `PictureService`, or any gRPC/proto contract.

## Decisions

**Hardcoded dictionary in `Review.razor`, not a filename-convention lookup.** An earlier direction computed the image path from the series name via regex (`Serie (\d+)(NL)?` → `Series{num}{NL}_klein.jpg`). Rejected in favor of an explicit `Dictionary<string, (string ImageFile, string Caption)>` literal in the `@code` block, keyed by the exact series display string. Rationale: the convention already has exceptions (`Serie 1` has no logo by design; `Serie 10`'s image doesn't exist yet), so a computed lookup would need special-casing anyway; a hardcoded map makes "no entry" the single, obvious way to express both cases, and keeps future manual edits (new caption, corrected image) a one-line diff with no regex to reason about.

**Caption is alt/tooltip text only, not the button label.** The button's visible label stays the raw series name string exactly as it renders today (e.g. `Serie 5NL`), so existing user-facing text and any code that might reason about the label is unaffected. The caption from the map is used only as the `<img alt="...">` value.

**Missing-entry fallback is silent, not a placeholder.** If `knownSeries` contains a series with no map entry, the button renders exactly as it does today (text only). No placeholder icon, no broken-image box, no console warning - this is the expected, common case for series whose logo hasn't been captured/mapped yet, not an error condition.

**No new C# model/class for the mapping.** A `Dictionary<string, (string ImageFile, string Caption)>` (or an equivalent small record) is enough; it lives next to `knownSeries` in `Review.razor`'s `@code` block rather than a separate file, since it is presentation-only data with no reuse outside this page.

## Risks / Trade-offs

- **Manual upkeep** → new series added to the catalog won't get a logo until someone edits the dictionary by hand. Mitigated by the fallback being silent/graceful rather than an error, so this is a cosmetic gap, not a functional one.
- **Map keys must match `knownSeries` strings exactly** (e.g. `"Serie 5NL"`, case and spacing) → a typo in a key silently drops that series' icon. Acceptable given the small, human-curated size of the map and the graceful fallback.
