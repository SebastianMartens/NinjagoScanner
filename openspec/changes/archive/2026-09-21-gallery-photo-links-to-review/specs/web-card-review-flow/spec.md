## ADDED Requirements

### Requirement: The review page can be opened on a specific card's group
The review page SHALL accept a series name and a card number in its address (e.g. from a link on the Gallery page) and, when the collection contains a group for the catalog card that series name and card number resolve to (using the same normalization as the grouping), SHALL open with that group displayed, showing all of its photos as usual. Because the target group must be visible regardless of its photos' statuses, opening the page this way SHALL start with the review-status filter and the analysis-status filter set to `All` and the search text empty. If the address gives only one of the two values, or no group exists for the card, the page SHALL open as if no card had been given. The filters can be changed afterwards like on any other visit, which behaves as described in the filter requirements.

#### Scenario: Opening a card whose photos are all verified
- **WHEN** a user opens the review page for a series and card number whose group contains only `verified` photos
- **THEN** that group is displayed with all of its photos, even though the page's default review-status filter would otherwise hide it

#### Scenario: Filters start cleared for a card link
- **WHEN** a user opens the review page for a specific card
- **THEN** the review-status filter and analysis-status filter show `All` and the search box is empty

#### Scenario: Card values are matched like the grouping matches them
- **WHEN** the address's series name and card number differ from the group's only in ways the grouping normalization ignores (such as letter case or whitespace)
- **THEN** the group for that catalog card is still displayed

#### Scenario: Navigation continues from the opened group
- **WHEN** a user has opened the review page for a specific card and activates the next-group or previous-group control
- **THEN** the page moves to the neighboring group in sort order among the groups matching the (cleared) filters

#### Scenario: No group exists for the card
- **WHEN** the address names a series and card number for which no group exists (for example because its last photo was deleted meanwhile)
- **THEN** the page opens as it does without a card in the address, including the default `Unreviewed` review-status filter

#### Scenario: Incomplete card in the address
- **WHEN** the address contains a series name but no card number, or a card number but no series name
- **THEN** the page opens as it does without a card in the address

## MODIFIED Requirements

### Requirement: Groups can be filtered by review status
The review page SHALL provide a review-status filter control offering `All`, `Unreviewed`, `Verified`, and `Incorrect`. When a status other than `All` is selected, a group SHALL be included in the list used for display and navigation if and only if at least one of its photos currently has that `ReviewStatus`; every photo in an included group SHALL still be shown, regardless of that individual photo's own `ReviewStatus`. Selecting `All` includes every group, matching the page's behavior without this filter. This filter combines with the analysis-status filter and the free-text search filter using AND: a group is included only if it satisfies this filter and every other currently active filter.

The review-status filter SHALL default to `Unreviewed` when the review page is loaded, except when the page is opened on a specific card's group, in which case it starts as `All`, per the "The review page can be opened on a specific card's group" requirement.

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
- **WHEN** a user loads the review page without naming a specific card
- **THEN** the review-status filter is set to `Unreviewed`, so only groups with at least one unreviewed photo are shown initially
