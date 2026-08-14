## 1. About Page

- [x] 1.1 Create `NinjagoScanner.Web/Components/Pages/About.razor` with route `@page "/about"`, static (no service injection), following the `overview-page`/`overview-header` markup pattern used by `Overview.razor`
- [x] 1.2 Add the German introduction section ("Was ist NinjagoScanner?"), adapted from `readme_de.md`'s intro, describing the app's purpose and main features
- [x] 1.3 Add the "Ist das kostenlos?" section with the exact disclosure text (cost/ads, child-safety, no personal data required, no data shared with third parties, service availability/accuracy disclaimer, photo-upload restrictions)
- [x] 1.4 Add any page-specific CSS rules needed to `NinjagoScanner.Web/wwwroot/app.css` (reuse existing `cv-*`/`overview-*` classes where possible; avoid a new scoped `.razor.css` unless existing shared classes don't cover the layout)

## 2. Navigation

- [x] 2.1 Add an "Über" `NavLink` pointing at `about` in the top header nav block (`cv-nav-links`) in `NinjagoScanner.Web/Components/Layout/NavMenu.razor`
- [x] 2.2 Add an "Über" `NavLink` pointing at `about` in the bottom mobile tab bar block (`cv-nav-bottom`) in the same file

## 3. Verification

- [ ] 3.1 Run `dotnet build NinjagoScanner.slnx` to confirm the Web project builds
- [ ] 3.2 Start the Web app (`Set-Location NinjagoScanner.Web; dotnet run`) and manually verify: `/about` renders without CatalogService/PictureService running, both nav entries link to it and show active state, and all required text (intro + "Ist das kostenlos?" section, verbatim) is present
