# web-card-review-flow Specification

## Purpose

Lets a person work through scanned card photos one catalog card at a time, comparing every photo grouped under that series/card-number against each other, confirming correct groups in one click, and fixing the dominant error (Gemini picking the wrong series) without leaving the page.

## Requirements

### Requirement: Photos are grouped by series and card number
The review page SHALL group every scanned photo by the pair of its own sidecar `SetName` and `CardNumber`, independent of the catalog, so a group exists if and only if at least one photo currently carries that `SetName`/`CardNumber` pair.

Series name and card number uniquely identify a catalog card (see GLOSSARY.md's Card entry), so a group's `SetName`/`CardNumber` pair corresponds to exactly one catalog card whenever it matches one.

#### Scenario: Photos sharing a series and card number are grouped together
- **WHEN** two or more photos have the same `SetName` and `CardNumber` in their sidecar
- **THEN** they appear together in the same group on the review page

#### Scenario: A catalog card with no photos never appears
- **WHEN** a catalog card has no photo whose sidecar `SetName`/`CardNumber` matches it
- **THEN** no group for that card is shown on the review page

### Requirement: Groups are ordered by known series order, then card number
Groups whose `SetName` matches a known catalog series SHALL be ordered by that series' catalog `SortOrder`, then by `CardNumber` within the series. Every photo whose `SetName` does not match a known catalog series - including a blank `SetName` - SHALL be merged into exactly one catch-all group, sorted after every known-series group, regardless of `CardNumber`.

#### Scenario: Groups follow catalog series order
- **WHEN** the review page lists groups for known series
- **THEN** they appear ordered by the series' catalog `SortOrder`, and by `CardNumber` within the same series

#### Scenario: Unrecognized and blank series are combined into one trailing group
- **WHEN** photos have a `SetName` that does not match any known catalog series, or have no `SetName` at all
- **THEN** all such photos appear together in a single group that is ordered after every known-series group

### Requirement: All photos in a group are shown at once
The review page SHALL display every photo currently in the selected group simultaneously, with each photo tile always showing that photo's own current series name, card name, and card number.

#### Scenario: Viewing a group with multiple photos
- **WHEN** a user opens a group containing more than one photo
- **THEN** all of that group's photos are shown at the same time, each labeled with its own series name, card name, and card number

### Requirement: Additional photo details are collapsed by default
Each photo tile SHALL hide its remaining sidecar fields (rarity, confidence, reasoning summary, detected text, error message, scanned-at timestamp) behind an on-demand control, collapsed by default.

#### Scenario: Expanding a photo's details
- **WHEN** a user activates the details control on a photo tile
- **THEN** that photo's remaining sidecar fields become visible, without affecting other photo tiles

### Requirement: A single photo's review status is set via a three-segment status control
Each photo tile SHALL provide a single control with three segments - `Unreviewed`, `Verified`, and `Incorrect` - that both displays and sets that photo's `ReviewStatus`, acting only on that one photo. The segment matching the photo's current `ReviewStatus` SHALL be visually highlighted, and activating any segment SHALL set that photo's `ReviewStatus` to the corresponding value.

#### Scenario: Current status is highlighted
- **WHEN** a photo tile is displayed
- **THEN** the segment matching that photo's current `ReviewStatus` is visually highlighted, and no other segment is

#### Scenario: Confirming one photo in a group
- **WHEN** a user activates the `Verified` segment on one photo tile
- **THEN** that photo's `ReviewStatus` becomes `verified`, the `Verified` segment becomes highlighted, and no other photo in the group is changed

#### Scenario: Flagging one photo as an error
- **WHEN** a user activates the `Incorrect` segment on one photo tile
- **THEN** that photo's `ReviewStatus` becomes `incorrect`, the `Incorrect` segment becomes highlighted, and no other photo in the group is changed

#### Scenario: Reverting a photo to unreviewed
- **WHEN** a user activates the `Unreviewed` segment on a photo tile whose `ReviewStatus` is currently `verified` or `incorrect`
- **THEN** that photo's `ReviewStatus` becomes `unreviewed`, the `Unreviewed` segment becomes highlighted, and no other photo in the group is changed

### Requirement: The status control reflects ReviewStatus changes made elsewhere on the page
A photo tile's status control SHALL reflect that photo's current `ReviewStatus` immediately after any action that changes it, including group-level actions, without requiring the page to be reloaded.

#### Scenario: Control updates after Confirm all
- **WHEN** a user activates "Confirm all" on a group
- **THEN** every photo tile in that group has its `Verified` segment highlighted without a page reload

### Requirement: A single photo can be reassigned to a different series with one click
Each photo tile SHALL provide one control per known catalog series; activating it SHALL update only that photo's `SetName` to the selected series, leaving its `ReviewStatus` and every other sidecar field unchanged.

#### Scenario: Reassigning a misclassified photo
- **WHEN** a user activates a series control for a photo whose true series differs from the group it is currently shown in
- **THEN** that photo's `SetName` is updated to the selected series, its `ReviewStatus` is unchanged, and it no longer appears in the current group on the next load

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
