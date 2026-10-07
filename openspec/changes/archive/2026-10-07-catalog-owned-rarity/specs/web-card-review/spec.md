## MODIFIED Requirements

### Requirement: ReviewStatus is editable via an explicit control
The review page's photo tile SHALL provide an explicit control to set a photo's `ReviewStatus` to `unreviewed`, `verified`, or `incorrect`, independent of every other editable field on that tile. The collection detail pane SHALL NOT provide one (it links to the review page instead).

#### Scenario: Marking a card as verified
- **WHEN** a user selects `verified` in the review status control
- **THEN** the photo's `ReviewStatus` is updated to `verified` and no other sidecar field is changed by that action

#### Scenario: Marking a card as incorrect
- **WHEN** a user selects `incorrect` in the review status control
- **THEN** the photo's `ReviewStatus` is updated to `incorrect` and no other sidecar field is changed by that action

#### Scenario: Editing other card fields does not change ReviewStatus
- **WHEN** a user changes any other editable field on the tile (e.g. card number, language, series) without touching the review status control
- **THEN** the photo's `ReviewStatus` is unchanged by that edit
