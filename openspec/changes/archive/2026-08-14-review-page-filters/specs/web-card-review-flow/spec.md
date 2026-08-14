## MODIFIED Requirements

### Requirement: Groups can be filtered by review status
The review page SHALL provide a review-status filter control offering `All`, `Unreviewed`, `Verified`, and `Incorrect`. When a status other than `All` is selected, a group SHALL be included in the list used for display and navigation if and only if at least one of its photos currently has that `ReviewStatus`; every photo in an included group SHALL still be shown, regardless of that individual photo's own `ReviewStatus`. Selecting `All` includes every group, matching the page's behavior without this filter. This filter combines with the analysis-status filter and the free-text search filter using AND: a group is included only if it satisfies this filter and every other currently active filter.

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

### Requirement: The group list updates to stay consistent with the active filter
Whenever a photo's `ReviewStatus` changes, the review page SHALL re-evaluate every active filter (review status, analysis status, and free-text search) against the updated data before the next group is shown, so a group that no longer satisfies all active filters is no longer included in navigation.

#### Scenario: Confirming the last matching photo in a group removes it from a non-All filter
- **WHEN** the active review-status filter is `Unreviewed`, a group has exactly one `unreviewed` photo, and that photo's status is changed to `verified`
- **THEN** that group is no longer included in the filtered group list

### Requirement: Groups can be navigated manually
The review page SHALL provide controls to move to the previous or next group among the groups currently satisfying every active filter (review status, analysis status, and free-text search), in the order defined above, independent of the "Confirm all" action.

#### Scenario: Moving to the next group manually
- **WHEN** a user activates the next-group control
- **THEN** the page displays the next group in sort order among groups satisfying every active filter, without changing any photo's `ReviewStatus` or `SetName`

#### Scenario: Moving to the previous group manually
- **WHEN** a user activates the previous-group control
- **THEN** the page displays the previous group in sort order among groups satisfying every active filter, without changing any photo's `ReviewStatus` or `SetName`

#### Scenario: Changing any filter returns to the first matching group
- **WHEN** a user selects a different review-status filter value, selects a different analysis-status filter value, or edits the free-text search box
- **THEN** the page displays the first group, in sort order, among the groups satisfying every currently active filter

## ADDED Requirements

### Requirement: Groups can be filtered by analysis status
The review page SHALL provide an analysis-status filter control offering `All`, `Ok`, `Uncertain`, `Failed`, and `Pending`. When a status other than `All` is selected, a group SHALL be included in the list used for display and navigation if and only if at least one of its photos currently has that `AnalysisStatus`; every photo in an included group SHALL still be shown, regardless of that individual photo's own `AnalysisStatus`. Selecting `All` removes this filter's constraint, matching the page's behavior without it. This filter combines with the review-status filter and the free-text search filter using AND: a group is included only if it satisfies this filter and every other currently active filter.

#### Scenario: Filtering to groups with a failed photo
- **WHEN** a user selects `Failed` in the analysis-status filter
- **THEN** only groups containing at least one photo whose `AnalysisStatus` is `failed` are shown, and every photo in each shown group is displayed regardless of its own `AnalysisStatus`

#### Scenario: A group matching the filter keeps its differently-analyzed photos visible
- **WHEN** the active analysis-status filter is `Uncertain` and a matching group also contains photos that are `ok` or `failed`
- **THEN** all of that group's photos remain visible, not only the ones with `AnalysisStatus` `uncertain`

#### Scenario: Clearing the analysis-status filter
- **WHEN** a user selects `All` in the analysis-status filter
- **THEN** every group excluded solely by that filter becomes eligible again, subject to the review-status filter and free-text search filter still being satisfied

### Requirement: Groups can be filtered by free-text search over card name and number
The review page SHALL provide a free-text search input. While the search text is non-empty, a group SHALL be included in the list used for display and navigation if and only if at least one of its photos has a `CardName` or `CardNumber` that contains the search text as a case-insensitive substring; every photo in an included group SHALL still be shown, regardless of whether that individual photo itself matched the search text. Matching updates live as the search text changes, without requiring a separate submit action. Clearing the search text removes this filter's constraint, matching the page's behavior without it. This filter combines with the review-status filter and the analysis-status filter using AND: a group is included only if it satisfies this filter and every other currently active filter.

#### Scenario: Searching by card number
- **WHEN** a user types a value into the search box that matches part of a photo's `CardNumber`
- **THEN** only groups containing at least one photo whose `CardNumber` contains that value, case-insensitively, are shown, and every photo in each shown group is displayed regardless of whether it individually matched

#### Scenario: Searching by card name
- **WHEN** a user types a value into the search box that matches part of a photo's `CardName`
- **THEN** only groups containing at least one photo whose `CardName` contains that value, case-insensitively, are shown, and every photo in each shown group is displayed regardless of whether it individually matched

#### Scenario: Search results update as the user types
- **WHEN** a user is typing into the search box
- **THEN** the filtered group list updates after each keystroke, without requiring the user to submit the search

#### Scenario: Clearing the search box
- **WHEN** a user clears the search box
- **THEN** every group excluded solely by the search filter becomes eligible again, subject to the review-status filter and analysis-status filter still being satisfied
