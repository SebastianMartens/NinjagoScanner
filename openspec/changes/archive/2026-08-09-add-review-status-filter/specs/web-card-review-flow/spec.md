## ADDED Requirements

### Requirement: Groups can be filtered by review status
The review page SHALL provide a review-status filter control offering `All`, `Unreviewed`, `Verified`, and `Incorrect`. When a status other than `All` is selected, a group SHALL be included in the list used for display and navigation if and only if at least one of its photos currently has that `ReviewStatus`; every photo in an included group SHALL still be shown, regardless of that individual photo's own `ReviewStatus`. Selecting `All` includes every group, matching the page's behavior without a filter.

#### Scenario: Filtering to groups with an unreviewed photo
- **WHEN** a user selects `Unreviewed` in the review-status filter
- **THEN** only groups containing at least one photo whose `ReviewStatus` is `unreviewed` are shown, and every photo in each shown group is displayed regardless of its own `ReviewStatus`

#### Scenario: A group matching the filter keeps its differently-reviewed photos visible
- **WHEN** the active filter is `Incorrect` and a matching group also contains photos that are `verified` or `unreviewed`
- **THEN** all of that group's photos remain visible, not only the ones with `ReviewStatus` `incorrect`

#### Scenario: Clearing the filter
- **WHEN** a user selects `All`
- **THEN** every group is shown again, regardless of any photo's `ReviewStatus`

#### Scenario: No groups match the active filter
- **WHEN** the active filter excludes every group
- **THEN** the review page shows the same empty state used when there is nothing left to review

### Requirement: The group list updates to stay consistent with the active filter
Whenever a photo's `ReviewStatus` changes, the review page SHALL re-evaluate the active filter against the updated data before the next group is shown, so a group that no longer has any photo matching the active filter is no longer included in navigation.

#### Scenario: Confirming the last matching photo in a group removes it from a non-All filter
- **WHEN** the active filter is `Unreviewed`, a group has exactly one `unreviewed` photo, and that photo's status is changed to `verified`
- **THEN** that group is no longer included in the filtered group list

## MODIFIED Requirements

### Requirement: Groups can be navigated manually
The review page SHALL provide controls to move to the previous or next group among the groups currently matching the active review-status filter, in the order defined above, independent of the "Confirm all" action.

#### Scenario: Moving to the next group manually
- **WHEN** a user activates the next-group control
- **THEN** the page displays the next group in sort order among groups matching the active filter, without changing any photo's `ReviewStatus` or `SetName`

#### Scenario: Moving to the previous group manually
- **WHEN** a user activates the previous-group control
- **THEN** the page displays the previous group in sort order among groups matching the active filter, without changing any photo's `ReviewStatus` or `SetName`

#### Scenario: Changing the filter returns to the first matching group
- **WHEN** a user selects a different review-status filter value
- **THEN** the page displays the first group, in sort order, among the groups matching the newly selected filter

### Requirement: A group can be confirmed all at once
The review page SHALL provide a group-level "Confirm all" control that sets `ReviewStatus` to `verified` for every photo currently shown in the group, then advances the page to the next group among the groups matching the active review-status filter, re-evaluated after the status change.

#### Scenario: Confirming a group where every photo is correct
- **WHEN** a user activates "Confirm all" on a group
- **THEN** every photo currently shown in that group has its `ReviewStatus` set to `verified`, and the page advances to the next group, in sort order, among groups matching the active filter

#### Scenario: Confirm all advances even into an already-reviewed group
- **WHEN** the next group in sort order after a "Confirm all" action has every photo already reviewed and still matches the active filter
- **THEN** the page still advances to that group rather than skipping it

#### Scenario: Confirm all under an active filter can empty the group list
- **WHEN** the active filter is `Unreviewed`, the confirmed group was the only remaining group matching the filter, and no later group matches it either
- **THEN** the page shows the same empty state used when there is nothing left to review
