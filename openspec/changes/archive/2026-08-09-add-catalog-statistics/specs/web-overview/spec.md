## ADDED Requirements

### Requirement: Catalog-wide statistics are shown
The Overview page SHALL show, once loaded, a catalog-wide statistics section with: the total number of catalog cards, the number of those cards with at least one owned/matching photo, and the total number of scanned photos. Card ownership for this count SHALL use the same exact-match rule (trim + case-fold series name, per series) as the per-series summary's owned-card count, so the catalog-wide owned-card total is consistent with the sum of the per-series tiles' owned-card counts.

#### Scenario: Statistics reflect the whole catalog
- **WHEN** the Overview page finishes loading
- **THEN** the statistics section shows the total catalog card count, the number of catalog cards with at least one matching photo, and the total number of scanned photos, each as a single number

#### Scenario: Owned-card total matches the sum of per-series tiles
- **WHEN** the per-series summary and the catalog-wide statistics are both shown
- **THEN** the catalog-wide "owned cards" count equals the sum of the "owned cards" counts shown across all per-series tiles

### Requirement: Photo analysis-status breakdown is shown
The catalog-wide statistics section SHALL show a count of all scanned photos for each analysis status (`ok`, `uncertain`, `failed`), covering every scanned photo regardless of whether it matched a catalog series.

#### Scenario: Photos in every analysis status exist
- **WHEN** at least one scanned photo exists for each of `ok`, `uncertain`, and `failed`
- **THEN** the statistics section shows a count for each of the three statuses, and the three counts sum to the total number of scanned photos

#### Scenario: No photos exist for an analysis status
- **WHEN** no scanned photo currently has a given analysis status
- **THEN** that status is still shown, with a count of zero

### Requirement: Photo review-status breakdown is shown
The catalog-wide statistics section SHALL show a count of all scanned photos for each review status (`unreviewed`, `verified`, `incorrect`), covering every scanned photo regardless of whether it matched a catalog series.

#### Scenario: Photos in every review status exist
- **WHEN** at least one scanned photo exists for each of `unreviewed`, `verified`, and `incorrect`
- **THEN** the statistics section shows a count for each of the three statuses, and the three counts sum to the total number of scanned photos

#### Scenario: No photos exist for a review status
- **WHEN** no scanned photo currently has a given review status
- **THEN** that status is still shown, with a count of zero
