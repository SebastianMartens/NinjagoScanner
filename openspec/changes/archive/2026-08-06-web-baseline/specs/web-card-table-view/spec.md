# web-card-table-view Specification

## Purpose

Gives a person a dense, tabular alternative to the tile gallery for comparing many scanned cards at once, with grouping, search, inline set assignment, and inline detail expansion.

## ADDED Requirements

### Requirement: Cards are rendered as a grouped table
The `/table` page SHALL render scanned cards in one table per group, each row showing a clickable thumbnail, card name (or file name if undetected) with the image file name, card number, an editable set selector, `AnalysisStatus` badge, `ReviewStatus` badge, rarity, confidence, and a details toggle.

#### Scenario: Viewing the table with scanned cards present
- **WHEN** a user opens `/table` and at least one scanned card exists
- **THEN** cards are rendered in grouped tables with a row per card, showing the columns Bild, Name, Nummer, Set, Status, Review-Status, Seltenheit, Confidence, and Details

### Requirement: Rows can be grouped
The table view SHALL let a user group rows by set/series, by `AnalysisStatus`, by rarity, or not at all, with groups sorted alphabetically by group key and each group header showing the group name and card count. Within every grouping, rows SHALL be ordered by set name, then card number, then card name.

#### Scenario: Grouping by status
- **WHEN** a user selects the "status" grouping option
- **THEN** rows are grouped into sections keyed by the card's status label, each section header showing the group name and the number of cards in it

### Requirement: Rows can be filtered
The table view SHALL let a user narrow the displayed rows by free-text search, matched case-insensitively against card name, card number, set name, rarity, `AnalysisStatus`, and image file name.

#### Scenario: Filtering the table
- **WHEN** a user enters search text
- **THEN** only rows whose card name, number, set, rarity, status, or file name contains that text (case-insensitively) remain visible

### Requirement: A card's set can be assigned inline from the table
Each row SHALL provide a dropdown of known series (from the catalog) plus an "Ohne Set" option that, when changed, saves the new set name for that card's photo without requiring the user to leave the table, disables that row's control while saving, and updates the row's displayed set locally on success.

#### Scenario: Changing a card's set from the table
- **WHEN** a user selects a different series in a row's set dropdown
- **THEN** the row's set control is disabled with a "Speichert..." hint while the change is saved, and afterward the row reflects the newly selected set without a full page reload

### Requirement: Row details can be expanded inline
A row SHALL show a details toggle only when the card has an error message or a reasoning summary; expanding it SHALL reveal the error message if present, otherwise the reasoning summary.

#### Scenario: Expanding details for a card with an error
- **WHEN** a user clicks "Details anzeigen" on a row whose card has an error message
- **THEN** the error message is shown and the toggle label changes to "Details ausblenden"

#### Scenario: No details available
- **WHEN** a card has neither an error message nor a reasoning summary
- **THEN** its row shows no details toggle, only a placeholder

### Requirement: A thumbnail opens an enlarged image preview
Clicking a row's thumbnail SHALL open a modal showing the full image with the card's name (or file name) as a caption; clicking outside the image or the close button SHALL dismiss it.

#### Scenario: Previewing a card image
- **WHEN** a user clicks a row's thumbnail
- **THEN** a modal opens showing the enlarged image and a caption with the card's name or file name, and it can be closed via the close button or by clicking the backdrop
