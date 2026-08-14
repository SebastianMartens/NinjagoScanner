## 1. Remove the page

- [x] 1.1 Delete `NinjagoScanner.Web/Components/Pages/CardsTable.razor`

## 2. Remove navigation entries

- [x] 2.1 Remove the `Tabelle` `NavLink` from the top nav in `NavMenu.razor`
- [x] 2.2 Remove the `Tabelle` `NavLink` from the bottom tab bar in `NavMenu.razor`

## 3. Clean up CSS

- [x] 3.1 Remove table-only rule blocks from `app.css`: `.table-page` (own block), `.table-controls*`, `.table-group*`, `.cards-table*`, `.table-tags`, `.table-thumb*`, `.table-primary`, `.table-status`, `.table-set-select`, `.table-set-hint`, `.table-details*`, `.table-error`, `.table-image-preview*`
- [x] 3.2 Remove the `.table-status` token from the shared interactive-element selector (`app.css:92`), keeping `.ownership-badge, .review-btn, .review-status-segment`
- [x] 3.3 Remove the `.table-page` token from the shared page-container selector (`app.css:351`), keeping `.collection-page, .gallery-page, .review-page, .upload-page, .overview-page`
- [x] 3.4 Remove the `.table-header` and `.table-image-preview-backdrop` rules from the responsive media query block (~`app.css:1841`)
- [x] 3.5 Grep each removed class name across all `.razor` files to confirm nothing outside the deleted page referenced it before removing its CSS — found `.table-secondary` is actually shared with `Collection.razor` and `Overview.razor` (detail-pane muted text), so it was restored (relocated next to `.detail-image`) instead of deleted

## 4. Verify

- [x] 4.1 Build the solution (`dotnet build NinjagoScanner.slnx`) and confirm no compilation errors (no other code referenced `CardsTable`)
- [x] 4.2 Run the Web app and confirm `/table` no longer appears in navigation and the route returns a 404 (or redirects, per Blazor default routing behavior) — verified via curl: home page has no "Tabelle" references, `/table` returns HTTP 404 rendering the app's `NotFound` page
- [x] 4.3 Confirm Gallery and Collection pages still render correctly with unaffected styling — verified via curl: `/gallery` returns 200 with "Galerie" heading and `gallery-tile` markup present, `/collection` returns 200 with "Sammlung" heading and `collection-grid` markup present
