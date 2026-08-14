## MODIFIED Requirements

### Requirement: A single photo can be reassigned to a different series via a popover
Each photo tile SHALL display, always visible with no trigger or popover step, a grid of controls listing every known catalog series; activating a series control SHALL update only that photo's `SetName` to the selected series, leaving its `ReviewStatus` and every other sidecar field unchanged.

#### Scenario: Reassigning a misclassified photo
- **WHEN** a user activates a series control for a series different from the group the photo is currently shown in
- **THEN** that photo's `SetName` is updated to the selected series, its `ReviewStatus` is unchanged, and the photo no longer appears in the current group on the next load

#### Scenario: Series controls are visible without any extra step
- **WHEN** a photo tile is displayed
- **THEN** every known catalog series' control is visible on that tile with no action required to reveal them
