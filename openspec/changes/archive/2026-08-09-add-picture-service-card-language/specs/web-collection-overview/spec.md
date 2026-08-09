## MODIFIED Requirements

### Requirement: A selected photo's sidecar can be edited and saved
The detail pane SHALL provide a form to edit the selected photo's card name, card number, set name (chosen from known series), rarity, language (chosen from German, English, or Unknown), confidence, reasoning summary, detected text (one entry per line), error message, and review status, and saving SHALL persist all of those fields via a single sidecar update, then reload the overview and re-select the current card.

#### Scenario: Saving valid sidecar edits
- **WHEN** a user edits sidecar fields and submits the form with a valid numeric confidence value
- **THEN** the update is saved, the overview and detail pane are refreshed to reflect it, and a success message is shown

#### Scenario: Saving with an invalid confidence value
- **WHEN** a user submits the form with a confidence value that is not a valid number
- **THEN** the save is rejected client-side with an error message and no update is sent

#### Scenario: Language control offers a closed set of options
- **WHEN** a user opens the Language control on the sidecar edit form
- **THEN** German, English, and Unknown are the only selectable options

#### Scenario: Language control is pre-filled with the resolved value
- **WHEN** the detail pane loads a photo's sidecar into the edit form
- **THEN** the Language control shows that photo's currently resolved language (defaulting to German if no explicit language was recorded) as the selected option
