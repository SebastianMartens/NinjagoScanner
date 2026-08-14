## Context

`CardsTable.razor` (`/table`) is a self-contained Blazor page: all grouping, filtering, sorting, and inline-edit logic lives in its own `@code` block, operating on `CardListItem` objects it fetches via `CardCatalogService.GetCardsAsync()`, `GetKnownSeriesAsync()`, and `UpdateSetNameAsync()`. None of those service methods, nor `CardListItem`, were written exclusively for this page — Gallery and Collection already call the same methods, and their own tests (`CardCatalogServiceGalleryTests`, `CardCatalogServiceDeletePhotoTests`) exercise them independently of the table page. See proposal.md for motivation.

## Goals / Non-Goals

**Goals:**
- Remove the `/table` route, `CardsTable.razor`, and its nav entries
- Remove CSS that exists solely to style the table page
- Leave `CardCatalogService`, `CardListItem`, and all other pages fully functional and untouched

**Non-Goals:**
- Changing Gallery or Collection page behavior
- Touching PictureService or CatalogService
- Any work related to the separate sidecar/photo storage (Firestore + GCS) exploration

## Decisions

### Delete the component outright rather than deprecate

No other code depends on `CardsTable.razor` (it's a leaf `@page` component), so there's no intermediate "mark obsolete" step needed — straight deletion is safe.

### Verify CSS selectors before deleting each rule, not just grep for `table`

`app.css` has some rules scoped only to the table page (`.cards-table*`, `.table-group*`, `.table-thumb*`, `.table-status`, `.table-set-select`, `.table-details*`, `.table-image-preview*`, `.table-primary`, `.table-tags`, `.table-controls*`) that can be deleted whole. `.table-secondary` looked table-only by name but turned out to be reused by `Collection.razor` and `Overview.razor` for detail-pane muted text — confirmed via grep across all `.razor` files before deletion, then restored (relocated next to `.detail-image`) rather than removed. Two locations also list `.table-page`/`.table-header` alongside other pages' classes in a shared selector and must be edited, not deleted wholesale:
- `app.css:92` — a shared interactive-element rule includes `.table-status` alongside `.ownership-badge, .review-btn, .review-status-segment`; only the `.table-status` token is removed, since `.table-status` itself is table-only (confirmed: the class name appears nowhere outside `CardsTable.razor`)
- `app.css:351` — `.table-page, .collection-page, .gallery-page, .review-page, .upload-page, .overview-page` is a shared page-container rule; only the `.table-page` token is removed, the rule stays for the other pages
- `app.css:1841` and `:1851` (media query) — `.table-header` and `.table-image-preview-backdrop` are table-only and can be removed entirely from the media query block

### `CardCatalogService` needs no changes

`GetCardsAsync`, `GetKnownSeriesAsync`, and `UpdateSetNameAsync` are shared with Gallery/Collection and stay as-is. `CardsTable.razor`'s grouping/filtering/sorting (`GetGroupedCards`, `MatchesSearch`, `GetGroupKey`, etc.) all live in the page's own `@code` block and are deleted with the file — nothing to prune in the service layer.

## Risks / Trade-offs

- [Low] A CSS rule might be miscategorized as table-only when something else quietly depends on it → grep each class name across all `.razor` files (not just `app.css`) before deleting, not just within the table page.
