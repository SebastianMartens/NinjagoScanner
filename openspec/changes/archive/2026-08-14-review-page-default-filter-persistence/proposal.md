## Why

The review page (`/review`) always opens with the Review-Status filter cleared, so reviewers have to re-select "Nicht geprüft" (Unreviewed) every time they open the page, even though reviewing unvalidated cards is the page's primary workflow.

## What Changes

- The Review-Status filter defaults to `Unreviewed` ("Nicht geprüft") on load instead of `All`.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `web-card-review-flow`: the review-status filter gains a specified default value (`Unreviewed`) instead of defaulting to `All`.

## Impact

- `NinjagoScanner.Web/Components/Pages/Review.razor`: the `reviewStatusFilter` field's initial value changes from `All` to `Unreviewed`.
- No changes to CatalogService, PictureService, gRPC contracts, or sidecar data — this is a Web-only, single-field change.
- No conflict with the in-progress `review-page-six-column-grid` change, which touches only CSS/layout on the same page.
