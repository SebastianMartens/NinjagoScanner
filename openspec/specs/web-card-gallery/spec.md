# web-card-gallery Specification

## Purpose
Gives a person a visual, tile-based overview of every scanned card photo and its detected details, with search/filter/group controls and a manual trigger for a PictureService Gemini scan of the photo folder.
## Requirements
### Requirement: Cards are rendered as tiles with detected details
The `/` page SHALL render one tile per scanned card, each showing the photo, its `AnalysisStatus` badge, its `ReviewStatus` badge, the card name (or file name if no name was detected), the image file name, card number, set name, rarity, confidence, and, when present, the reasoning summary, error message, and detected text tags.

#### Scenario: Viewing the gallery with scanned cards present
- **WHEN** a user opens `/` and at least one scanned card exists
- **THEN** each card is shown as a tile with its photo, status badges, and available detail fields, falling back to `-` for number/set/rarity when not detected

### Requirement: Cards can be grouped
The gallery SHALL let a user group the displayed cards by none, `AnalysisStatus`, set name, or rarity, with groups sorted alphabetically by group key (case-insensitive) and cards without a set name grouped under "Ohne Set" / without a rarity under "Unbekannt".

#### Scenario: Grouping by set
- **WHEN** a user selects the "set" grouping option
- **THEN** cards are grouped into sections by their set name, each section sorted alphabetically, and cards with no set name appear together in an "Ohne Set" section

### Requirement: Cards can be filtered
The gallery SHALL let a user narrow the displayed cards by a free-text search (matched case-insensitively against card name, card number, set name, rarity, `AnalysisStatus`, and image file name), by `AnalysisStatus`, by set name, and by rarity, with all active filters applied together (AND).

#### Scenario: Combining search and status filter
- **WHEN** a user enters search text and also selects an `AnalysisStatus` filter
- **THEN** only cards matching both the search text and the selected status are shown

### Requirement: Empty and no-match states are distinguished
The gallery SHALL show a distinct message when no scanned cards exist at all versus when cards exist but none match the current filter combination.

#### Scenario: No photos in cardFotos
- **WHEN** no scanned card photos exist
- **THEN** the page shows a message indicating no supported image files were found

#### Scenario: Filters exclude all cards
- **WHEN** at least one card exists but the current search/status/set/rarity combination matches none of them
- **THEN** the page shows a distinct "no matches for this filter combination" message instead of the no-photos message

### Requirement: A manual Gemini scan can be triggered
The gallery SHALL provide a button that starts a PictureService scan of the resolved card photos directory, disables itself while the scan is running, and afterward shows a summary of processed/skipped/uncertain/failed counts (or the service's configuration-error message) and refreshes the displayed card list.

#### Scenario: Running a scan successfully
- **WHEN** a user clicks the scan button and the scan completes without a configuration error
- **THEN** the button is disabled for the duration of the scan, a summary message with processed/skipped/uncertain/failed counts is shown afterward, and the card list is reloaded to reflect any new results

#### Scenario: Scan cannot start due to configuration
- **WHEN** a user clicks the scan button and PictureService reports a configuration error
- **THEN** the shown message is PictureService's error message instead of a processed/skipped/uncertain/failed summary

