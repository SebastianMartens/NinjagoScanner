## MODIFIED Requirements

### Requirement: A single photo can be reassigned to a different series via a popover
Each photo tile SHALL provide a single trigger control, showing the photo's current series, that opens a popover grid listing every known catalog series; activating a series within the open popover SHALL update only that photo's `SetName` to the selected series, leaving its `ReviewStatus` and every other sidecar field unchanged, and SHALL close the popover. Activating the trigger control itself SHALL only open or close the popover and SHALL NOT change the photo's `SetName`.

#### Scenario: Reassigning a misclassified photo
- **WHEN** a user opens a photo tile's series popover and activates a series control for a series different from the group the photo is currently shown in
- **THEN** that photo's `SetName` is updated to the selected series, its `ReviewStatus` is unchanged, the popover closes, and the photo no longer appears in the current group on the next load

#### Scenario: Opening and closing the popover does not reassign the photo
- **WHEN** a user activates a photo tile's series trigger control to open or close its popover, without activating a series within it
- **THEN** that photo's `SetName` is unchanged

#### Scenario: The trigger control reflects the photo's current series
- **WHEN** a photo tile is displayed
- **THEN** its series trigger control shows that photo's current series
