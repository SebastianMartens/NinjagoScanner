## ADDED Requirements

### Requirement: A single photo's card number can be corrected inline
Each photo tile SHALL provide an inline control for editing that photo's `CardNumber`, pre-filled with its current value. Submitting the control SHALL update only that photo's `CardNumber`, leaving its `ReviewStatus`, `SetName`, and every other sidecar field unchanged.

#### Scenario: Correcting a misdetected card number
- **WHEN** a user edits the card number control on a photo tile to a different value and submits it
- **THEN** that photo's `CardNumber` is updated to the entered value, its `ReviewStatus` and `SetName` are unchanged, and the group list is reloaded so the photo appears under the group matching its new `SetName`/`CardNumber`

#### Scenario: Submitting an unchanged card number
- **WHEN** a user submits the card number control without changing its value
- **THEN** that photo's `CardNumber` is unchanged and the photo remains in its current group

#### Scenario: Clearing a card number
- **WHEN** a user clears the card number control and submits it
- **THEN** that photo's `CardNumber` becomes blank, and the photo moves to the catch-all group on the next load, since a blank `CardNumber` does not resolve to any catalog card

### Requirement: The card number control reflects the photo's current value after other changes
A photo tile's card number control SHALL be re-initialized to that photo's current `CardNumber` whenever the group list is reloaded, including after actions taken on other photos in the same group.

#### Scenario: Control shows the corrected value after saving
- **WHEN** a user corrects a photo's card number and the save completes
- **THEN** the card number control on that photo tile displays the newly saved value
