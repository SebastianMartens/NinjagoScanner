## Why

NinjagoScanner has no in-app page that explains what the app is, that it's free, or how it handles a person's photos and data — that context currently only lives in the repo's README, which real end users never see. Since the app targets families/children and carries advertising, it needs an accessible in-app page covering what the app does plus the cost/privacy/usage disclosures a parent or new user would look for.

## What Changes

- Add a new static, read-only "About" page to the Web app at `/about`, in German only (the app's UI is already German-only throughout, so this introduces no new localization infrastructure).
- Page content, in order:
  - An introductory "What is NinjagoScanner?" section describing the app's purpose and features, adapted from the German README's existing intro (`readme_de.md`, "Was ist NinjagoScanner?").
  - A "Ist das kostenlos?" section with the exact text supplied by the product owner, covering: free/non-commercial hobby project, minimal non-intrusive ads, child-appropriate (no violence/chat/in-app purchases/hidden costs), no personal data required, anonymous registration allowed, no data shared with third parties, plus usage disclaimers (service may be discontinued/restricted at any time, no availability/accuracy guarantee, and photo upload restrictions — own cards only, no personal information in photos).
- Add an "Über" (About) link to both nav blocks in `NavMenu.razor` (top header nav and bottom mobile tab bar) pointing at `/about`.

## Capabilities

### New Capabilities
- `web-about-page`: A static informational `/about` page in the Web app describing what NinjagoScanner does and disclosing its free/non-commercial nature, child-safety posture, privacy stance, and usage terms, reachable from the app's navigation.

### Modified Capabilities

(none — no existing capability's requirements change; the nav gains a link but no existing nav requirement is altered)

## Impact

- Affected project: `NinjagoScanner.Web` only (no CatalogService/PictureService changes, no gRPC contract changes).
- New file: `NinjagoScanner.Web/Components/Pages/About.razor` (static content, no service injection needed — same pattern as `NotFound.razor`/`Error.razor`).
- Modified file: `NinjagoScanner.Web/Components/Layout/NavMenu.razor` (add one `NavLink` in each of the two existing nav blocks).
- Possible modified file: `NinjagoScanner.Web/wwwroot/app.css` if the page needs styling beyond what `overview-page`/`overview-header` classes already provide (existing convention: pages don't use scoped `.razor.css` files).
- No new dependencies, no database/storage changes, no test-host or gRPC fixture changes.
