## MODIFIED Requirements

### Requirement: Groups can be filtered by review status
The review page SHALL provide a review-status filter control offering `All`, `Unreviewed`, `Verified`, and `Incorrect`. When a status other than `All` is selected, a group SHALL be included in the list used for display and navigation if and only if at least one of its photos currently has that `ReviewStatus`; every photo in an included group SHALL still be shown, regardless of that individual photo's own `ReviewStatus`. Selecting `All` includes every group, matching the page's behavior without this filter. This filter combines with the analysis-status filter and the free-text search filter using AND: a group is included only if it satisfies this filter and every other currently active filter.

The review-status filter SHALL default to `Unreviewed` when the review page is loaded.

#### Scenario: Filtering to groups with an unreviewed photo
- **WHEN** a user selects `Unreviewed` in the review-status filter
- **THEN** only groups containing at least one photo whose `ReviewStatus` is `unreviewed` are shown, and every photo in each shown group is displayed regardless of its own `ReviewStatus`

#### Scenario: A group matching the filter keeps its differently-reviewed photos visible
- **WHEN** the active filter is `Incorrect` and a matching group also contains photos that are `verified` or `unreviewed`
- **THEN** all of that group's photos remain visible, not only the ones with `ReviewStatus` `incorrect`

#### Scenario: Clearing the filter
- **WHEN** a user selects `All`
- **THEN** every group is shown again, regardless of any photo's `ReviewStatus`, subject to the analysis-status filter and free-text search filter still being satisfied

#### Scenario: No groups match the active filters
- **WHEN** the active review-status filter, combined with any active analysis-status filter or search text, excludes every group
- **THEN** the review page shows the same empty state used when there is nothing left to review

#### Scenario: Review-status filter defaults to Unreviewed
- **WHEN** a user loads the review page
- **THEN** the review-status filter is set to `Unreviewed`, so only groups with at least one unreviewed photo are shown initially
