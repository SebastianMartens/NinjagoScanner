## MODIFIED Requirements

### Requirement: Photo analysis-status breakdown is shown
The catalog-wide statistics section SHALL show a count of all scanned photos for each analysis status (`ok`, `uncertain`, `failed`, `not yet analyzed`), covering every scanned photo regardless of whether it matched a catalog series. A photo counts as `not yet analyzed` when it has no recorded analysis status of `ok`, `uncertain`, or `failed` (including photos that were never scanned by Gemini and therefore have no sidecar data yet). The four counts SHALL always sum to the total number of scanned photos.

#### Scenario: Photos in every analysis status exist
- **WHEN** at least one scanned photo exists for each of `ok`, `uncertain`, `failed`, and `not yet analyzed`
- **THEN** the statistics section shows a count for each of the four statuses, and the four counts sum to the total number of scanned photos

#### Scenario: No photos exist for an analysis status
- **WHEN** no scanned photo currently has a given analysis status
- **THEN** that status is still shown, with a count of zero

#### Scenario: A photo has never been analyzed
- **WHEN** a photo exists in the card photos directory but has not yet been scanned by Gemini (no `ok`/`uncertain`/`failed` status recorded)
- **THEN** that photo is counted in the `not yet analyzed` bucket, not omitted from the breakdown
