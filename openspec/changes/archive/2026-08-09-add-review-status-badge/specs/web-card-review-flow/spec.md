## MODIFIED Requirements

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

## ADDED Requirements

### Requirement: The status control reflects ReviewStatus changes made elsewhere on the page
A photo tile's status control SHALL reflect that photo's current `ReviewStatus` immediately after any action that changes it, including group-level actions, without requiring the page to be reloaded.

#### Scenario: Control updates after Confirm all
- **WHEN** a user activates "Confirm all" on a group
- **THEN** every photo tile in that group has its `Verified` segment highlighted without a page reload
