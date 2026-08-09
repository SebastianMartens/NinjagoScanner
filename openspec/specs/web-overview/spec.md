# web-overview Specification

## Purpose

Serves as the application's landing page ("/"), giving a person the entry point for triggering new photo analysis and, over time, a home for at-a-glance status information about the collection.

## Requirements

### Requirement: Overview is the application's home page
The Overview page SHALL be served at the root route ("/") of the web application.

#### Scenario: Opening the application
- **WHEN** a user navigates to "/"
- **THEN** the Overview page is displayed

### Requirement: A manual Gemini scan can be triggered
The Overview page SHALL provide a button that starts a PictureService scan of the resolved card photos directory, disables itself while the scan is running, and afterward shows a summary of processed/skipped/uncertain/failed counts (or the service's configuration-error message).

#### Scenario: Running a scan successfully
- **WHEN** a user clicks the scan button and the scan completes without a configuration error
- **THEN** the button is disabled for the duration of the scan, and a summary message with processed/skipped/uncertain/failed counts is shown afterward

#### Scenario: Scan cannot start due to configuration
- **WHEN** a user clicks the scan button and PictureService reports a configuration error
- **THEN** the shown message is PictureService's error message instead of a processed/skipped/uncertain/failed summary

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

### Requirement: A per-series collection summary is shown
The Overview page SHALL show, for every series known to the catalog, ordered by the catalog's `SortOrder` ascending: the total number of catalog cards in that series, the number of distinct card numbers in that series with at least one matching photo, and the total number of matching photos for that series (including duplicates). A photo matches a series only when its Series Name equals the series's name exactly after trimming whitespace and case-folding — a photo whose series-name resolution failed during AI Analysis carries its unresolved raw guess instead, so it will not match any series here. This remains a separate rule from the normalized matching `/collection`'s `Owned Copies` uses. Category is not part of this summary.

#### Scenario: A series with no owned cards
- **WHEN** a catalog series has no photo whose Series Name exactly matches it (after trim/case-fold)
- **THEN** the series is still shown, with an owned-card count and a photo count of zero

#### Scenario: A Next Level variant is shown separately from its base series
- **WHEN** the catalog contains both a series and its Next Level variant (e.g. "Serie 5" and "Serie 5 Next Level")
- **THEN** both appear as separate entries in the summary, each with their own counts

### Requirement: Photos with an unrecognized series name are counted separately
Photos whose Series Name doesn't exactly match (after trim/case-fold) any catalog series SHALL be counted in a separate unknown-series bucket, shown apart from the per-series entries and only when its count is greater than zero, mirroring how `/collection` already omits its "nicht zugeordnet" statistic at zero.

#### Scenario: Photos with an unrecognized series name exist
- **WHEN** at least one photo's Series Name does not exactly match (after trim/case-fold) any catalog series
- **THEN** the unknown-series bucket is shown with that count, separate from the per-series entries

#### Scenario: Every photo matches a known series
- **WHEN** every photo's Series Name exactly matches (after trim/case-fold) some catalog series
- **THEN** the unknown-series bucket is omitted

### Requirement: The per-series summary is shown as a tile/card grid
The Overview page SHALL show the per-series summary as a tile/card grid, one tile per series, with no alternate layout or layout-switching control.

#### Scenario: Series summary renders as tiles
- **WHEN** the Overview page loads the per-series summary
- **THEN** each series is shown as a card/tile in a grid, not as a table row

### Requirement: Selecting a series navigates to the pre-filtered collection list
Selecting a series entry in the per-series summary SHALL navigate to the collection list page (`/collection`) with that series pre-selected via a query-string parameter.

#### Scenario: Clicking a series entry
- **WHEN** a user selects a series entry in the per-series summary
- **THEN** the browser navigates to `/collection` with a `series` query-string parameter set to that series' exact name, and the collection list shows that series pre-selected in its series filter
