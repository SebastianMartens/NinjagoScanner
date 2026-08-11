## ADDED Requirements

### Requirement: A single photo's language can be corrected inline
Each photo tile SHALL provide an inline control for editing that photo's `Language`, pre-filled with its current value (German, English, Polish, or Unknown), offering exactly those four options. Selecting a different option SHALL update only that photo's `Language`, leaving its `ReviewStatus`, `SetName`, `CardNumber`, and every other sidecar field unchanged, and SHALL NOT move the photo to a different group, since `Language` is not part of the series/card-number grouping key.

#### Scenario: Correcting a misdetected language
- **WHEN** a user selects a different option in the language control on a photo tile
- **THEN** that photo's `Language` is updated to the selected value, its `ReviewStatus`, `SetName`, and `CardNumber` are unchanged, and the photo remains in its current group

#### Scenario: Language control offers a closed set of options
- **WHEN** a user opens the language control on a photo tile
- **THEN** German, English, Polish, and Unknown are the only selectable options

### Requirement: The language control reflects the photo's current value after other changes
A photo tile's language control SHALL be re-initialized to that photo's current `Language` whenever the group list is reloaded, including after actions taken on other photos in the same group.

#### Scenario: Control shows the corrected value after saving
- **WHEN** a user corrects a photo's language and the save completes
- **THEN** the language control on that photo tile displays the newly saved value
