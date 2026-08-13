## Context

The collection page (`Collection.razor`) renders a `<div class="collection-stats">` block with six pill badges showing aggregate statistics. These are computed from an `overview` variable of type `CollectionOverviewResult` and from LINQ queries over `allCards`. The Overview page now displays the same data, making the collection page badges redundant. See proposal.md for motivation.

## Goals / Non-Goals

**Goals:**
- Remove the statistics badge UI from the collection page
- Remove associated CSS that is no longer used
- Remove dead code paths for computing overview statistics if they serve only the badges

**Non-Goals:**
- Changing the Overview page's statistics display
- Removing the per-card ownership badges ("Fehlt" / "Mehrfach" / "Vorhanden") — those remain
- Removing the `CollectionOverviewResult` model class (it may still be used elsewhere)

## Decisions

### Remove only the header stats block, keep per-row badges
The `collection-stats` div in the page header is the only target. The per-row ownership badges (`.ownership-badge`) are a different feature (per-card status, not aggregate statistics) and remain unchanged.

### Remove `.collection-stats` CSS only if unused elsewhere
The Overview page also uses the `.collection-stats` class. The CSS rules must be kept unless the Overview page is confirmed to not depend on them. Check before deleting.

### Keep `CollectionOverviewResult` if still referenced
The `overview` variable powers the photo-count stats in the badges. If other logic on the collection page (e.g., unmapped photo handling) still references it, keep the model and computation; only remove the rendering.

## Risks / Trade-offs

- [Low] `.collection-stats` CSS shared with Overview page → verify before removing styles; leave them if shared.
- [Low] `overview` variable may be referenced elsewhere in the component → remove rendering first, then check if computation can be pruned.
