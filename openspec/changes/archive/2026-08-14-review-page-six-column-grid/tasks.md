## 1. Review page layout

- [x] 1.1 In [app.css](../../../NinjagoScanner.Web/wwwroot/app.css), widen `.review-page`'s `max-width` (or remove the cap in favor of viewport-relative width) so the page uses more of the available screen width.
- [x] 1.2 In `app.css`, change `.review-photo-grid`'s `grid-template-columns` from the uncapped `repeat(auto-fill, minmax(280px, 1fr))` to a rule that fills available width but never exceeds six columns, using the same tile width (280px) as today.
- [x] 1.3 Verify the responsive `@media (max-width: 700px)` block still reflows the grid to fewer columns at narrow widths without any tile-size change.

## 2. Verification

- [x] 2.1 Run the app (`NinjagoScanner.Web`, with CatalogService and PictureService running) and load `/review` with a group containing more than six photos at a wide (e.g. ultrawide/4K) viewport; confirm no row exceeds six tiles and tile size is unchanged from today.
- [x] 2.2 Resize the browser down through mid and narrow widths and confirm the grid wraps to fewer tiles per row (5, 4, 3, 2, 1) at the same tile size, matching the existing responsive behavior below 700px.
- [x] 2.3 Run `dotnet test NinjagoScanner.Web.Tests` to confirm no regressions (this change is CSS/markup-only, so no test updates are expected).
