## Why

The collection statistics badges on the `/collection` page (total cards, owned, duplicates, photos, mapped/unmapped) are now redundant — the Overview page already displays the same information. Removing them declutters the collection page header and eliminates a maintenance burden for keeping two places in sync.

## What Changes

- Remove the `<div class="collection-stats">` block from the collection page header that displays aggregate statistics (total cards, owned, duplicates, total photos, mapped photos, unmapped photos)
- Remove the associated CSS styles (`.collection-stats` and `.collection-stats span`)
- Remove the `CollectionOverviewResult` computation and related data-fetching logic from the collection page, if no longer needed elsewhere on that page

## Capabilities

### New Capabilities

_(none)_

### Modified Capabilities

- `web-collection-list`: Remove the "Summary statistics are shown" requirement — these stats are no longer displayed on the collection page

## Impact

- **Code**: `Collection.razor` (template and code-behind logic), `app.css` (badge styles)
- **Model**: `CollectionOverviewResult` usage in the collection page may be removable if nothing else on that page consumes it
- **No API changes**: The overview page continues to compute and show these stats independently
