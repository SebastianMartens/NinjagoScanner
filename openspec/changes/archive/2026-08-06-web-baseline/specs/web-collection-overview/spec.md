# web-collection-overview Specification

## Purpose

Gives a person a complete view of every card known to the catalog — owned or not — by merging CatalogService's full card list with locally owned photos, showing ownership counts, and letting them inspect and edit the sidecar data behind a selected card's photo.

## ADDED Requirements

### Requirement: The overview covers every catalog card, not just owned ones
The `/collection` page SHALL list every card returned by CatalogService's card catalog, each annotated with the number of owned photo copies (`OwnedCopies`) determined by matching the card's series and card number, normalized, against scanned photo sidecars' set name and card number.

#### Scenario: A catalog card with no matching photo
- **WHEN** a catalog card has no scanned photo whose set name and card number match it after normalization
- **THEN** the card still appears in the overview with `OwnedCopies` equal to zero and is treated as not owned

#### Scenario: A catalog card with multiple matching photos
- **WHEN** more than one scanned photo's set name and card number match the same catalog card after normalization
- **THEN** the card's `OwnedCopies` reflects the count of matching photos and it is treated as a duplicate

### Requirement: Summary statistics are shown
The page SHALL show, once loaded, the total number of catalog cards, the number owned, the number owned more than once, the total number of scanned photos, the number of those photos mapped to a catalog card, and (only when greater than zero) the number of unmapped photos.

#### Scenario: Photos exist that don't match any catalog card
- **WHEN** at least one scanned photo's set name/card number does not match any catalog card
- **THEN** the "nicht zugeordnet" (unmapped) statistic is shown with that count; otherwise it is omitted

### Requirement: Cards can be grouped, filtered, and searched
The overview SHALL let a user group cards by series, category, ownership status (missing / owned once / owned more than once), or not at all; filter by a selected series (which also narrows the available categories) and by category; restrict to only missing or only duplicate-owned cards; and search by card number or card name substring, with all active filters applied together.

#### Scenario: Selecting a series narrows available categories
- **WHEN** a user selects a series in the series filter
- **THEN** the category filter's options are limited to categories present within that series, and if the previously selected category is no longer available it is cleared

#### Scenario: Combining "only missing" with a series filter
- **WHEN** a user selects a series and also enables "Nur fehlende Karten"
- **THEN** only cards in that series with zero owned copies are shown

### Requirement: Selecting a card loads its full details
Selecting a card row SHALL load that card's series metadata (year, logo, theme, highlights) and its matching photos, and SHALL clear the currently displayed details while loading.

#### Scenario: Selecting a card
- **WHEN** a user clicks a card row (or navigates to it via keyboard)
- **THEN** the detail pane shows a loading state, then the card's title, series/category/number, metadata, and its list of matching photos once loaded

#### Scenario: Card has no matching photos
- **WHEN** the selected card has no matching photos
- **THEN** the detail pane indicates no photo is available for the card and disables sidecar editing

### Requirement: A card's photo can be chosen when multiple exist
When a selected card has more than one matching photo, the detail pane SHALL let the user pick which photo's image and sidecar are shown, defaulting to the first photo (ordered by file name) when the card is first selected.

#### Scenario: Switching between multiple photos
- **WHEN** a user selects a different photo from the photo picker for a card with multiple photos
- **THEN** the displayed image and the sidecar edit form update to reflect the newly selected photo, and any unsaved sidecar-save status messages are cleared

### Requirement: Keyboard navigation moves the selection through the current list
While focus is within the card list, pressing the down or up arrow key SHALL move the selection to the next or previous card in the currently filtered/ordered list (if one exists in that direction) and scroll it into view.

#### Scenario: Navigating past the last visible card
- **WHEN** the last card in the filtered list is selected and the user presses the down arrow key
- **THEN** the selection does not change

### Requirement: A selected photo's sidecar can be edited and saved
The detail pane SHALL provide a form to edit the selected photo's card name, card number, set name (chosen from known series), rarity, confidence, reasoning summary, detected text (one entry per line), error message, and review status, and saving SHALL persist all of those fields via a single sidecar update, then reload the overview and re-select the current card.

#### Scenario: Saving valid sidecar edits
- **WHEN** a user edits sidecar fields and submits the form with a valid numeric confidence value
- **THEN** the update is saved, the overview and detail pane are refreshed to reflect it, and a success message is shown

#### Scenario: Saving with an invalid confidence value
- **WHEN** a user submits the form with a confidence value that is not a valid number
- **THEN** the save is rejected client-side with an error message and no update is sent
