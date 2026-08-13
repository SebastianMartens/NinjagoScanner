# web-collection-list Specification

## Purpose
Gives a person a complete view of every card known to the catalog — owned or not — by merging CatalogService's full card list with locally owned photos, showing ownership counts, and letting them inspect and edit the sidecar data behind a selected card's photo.

## Requirements

### Requirement: The overview covers every catalog card, not just owned ones
The `/collection` page SHALL list every card returned by CatalogService's card catalog, each annotated with the number of owned photo copies (`OwnedCopies`) determined by matching the card's series and card number, normalized, against scanned photo sidecars' set name and card number.

Series name and card number uniquely identify a catalog card (see GLOSSARY.md's Card entry), so this match is always unambiguous: a photo's `OwnedCopies` contribution is attributed to exactly one catalog card.

#### Scenario: A catalog card with no matching photo
- **WHEN** a catalog card has no scanned photo whose set name and card number match it after normalization
- **THEN** the card still appears in the overview with `OwnedCopies` equal to zero and is treated as not owned

#### Scenario: A catalog card with multiple matching photos
- **WHEN** more than one scanned photo's set name and card number match the same catalog card after normalization
- **THEN** the card's `OwnedCopies` reflects the count of matching photos and it is treated as a duplicate

### Requirement: Cards can be grouped, filtered, and searched
The overview SHALL let a user group cards by series, category, ownership status (missing / owned once / owned more than once), or not at all; filter by a selected series (which also narrows the available categories) and by category; restrict to only missing or only duplicate-owned cards; and search by card number or card name substring, with all active filters applied together. When grouping by category, and when populating the category filter's options, categories SHALL be ordered by each category's lowest card number (using the catalog's card-number ordering), not alphabetically by category name.

#### Scenario: Selecting a series narrows available categories
- **WHEN** a user selects a series in the series filter
- **THEN** the category filter's options are limited to categories present within that series, and if the previously selected category is no longer available it is cleared

#### Scenario: Combining "only missing" with a series filter
- **WHEN** a user selects a series and also enables "Nur fehlende Karten"
- **THEN** only cards in that series with zero owned copies are shown

#### Scenario: Grouping by category orders groups by lowest card number
- **WHEN** a user groups the collection by category, and one category's lowest card number is higher than another's despite the first category's name sorting alphabetically earlier (e.g. "Action Cards" starting at card `101` versus "Heroes" starting at card `1`)
- **THEN** the "Heroes" group appears before the "Action Cards" group, ordered by each group's lowest card number rather than by category name

#### Scenario: Category filter dropdown lists categories by lowest card number
- **WHEN** the category filter's options are populated for the selected series
- **THEN** the options appear ordered by each category's lowest card number, not alphabetically by category name

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
The detail pane SHALL provide a form to edit the selected photo's card name, card number, set name (chosen from known series), rarity, language (chosen from German, English, Polish, or Unknown), confidence, reasoning summary, detected text (one entry per line), error message, and review status, and saving SHALL persist all of those fields via a single sidecar update, then reload the overview and re-select the current card.

#### Scenario: Saving valid sidecar edits
- **WHEN** a user edits sidecar fields and submits the form with a valid numeric confidence value
- **THEN** the update is saved, the overview and detail pane are refreshed to reflect it, and a success message is shown

#### Scenario: Saving with an invalid confidence value
- **WHEN** a user submits the form with a confidence value that is not a valid number
- **THEN** the save is rejected client-side with an error message and no update is sent

#### Scenario: Language control offers a closed set of options
- **WHEN** a user opens the Language control on the sidecar edit form
- **THEN** German, English, Polish, and Unknown are the only selectable options

#### Scenario: Language control is pre-filled with the resolved value
- **WHEN** the detail pane loads a photo's sidecar into the edit form
- **THEN** the Language control shows that photo's currently resolved language (defaulting to German if no explicit language was recorded) as the selected option

### Requirement: Series lists and groupings follow catalog sort order
Wherever series are listed or cards are grouped by series — the series filter dropdown, the "group by series" view, and manual sorting of the series column — series SHALL appear ordered by the catalog's `SortOrder`, not alphabetically by series name.

#### Scenario: Series filter dropdown ordering
- **WHEN** the collection overview loads its series filter options
- **THEN** the options are ordered by the catalog's `SortOrder` ascending, so a series like "Serie 10" appears after "Serie 2" rather than before it

#### Scenario: Grouping by series
- **WHEN** a user groups the collection by series
- **THEN** the group headers appear ordered by the catalog's `SortOrder` ascending

#### Scenario: Sorting the table by the Series column
- **WHEN** a user clicks the "Serie" column header to sort
- **THEN** rows are ordered by the catalog's `SortOrder` (ascending, or descending when toggled), not by the series name text

### Requirement: Sorting the table by the Nr. column follows the canonical card-number order
Clicking the "Nr." column header to sort the card list SHALL order rows using the same card-number rule used everywhere else in the application: purely numeric card numbers first, ordered by value; then alphabetic-prefix-plus-number card numbers (e.g. `LE4`, `XXL1`), ordered by prefix alphabetically and then by numeric suffix; then any remaining format ordered alphabetically by raw text. Toggling the sort direction SHALL reverse this order.

#### Scenario: Sorting by Nr. ascending
- **WHEN** a user clicks the "Nr." column header on a list containing both numeric and alphanumeric card numbers
- **THEN** rows are ordered with all numeric card numbers first by ascending value, followed by alphanumeric card numbers ordered by prefix alphabetically and then by numeric suffix

#### Scenario: Toggling sort direction
- **WHEN** a user clicks the "Nr." column header a second time
- **THEN** the same ordering rule applies in reverse

### Requirement: The series filter can be pre-selected via a query-string parameter
The `/collection` page SHALL read an optional `series` query-string parameter on load and, when it is present and matches a known series exactly (case-insensitive), pre-select that series in the series filter; the parameter SHALL be ignored — leaving the series filter unset and showing no error — when it is absent, blank, or does not match any known series.

#### Scenario: Arriving with a valid series parameter
- **WHEN** a user navigates to `/collection?series=Serie%205`
- **THEN** the series filter is pre-selected to "Serie 5" and the card list is filtered accordingly, exactly as if the user had chosen it manually

#### Scenario: Arriving with an unrecognized series parameter
- **WHEN** a user navigates to `/collection?series=NotARealSeries`
- **THEN** the series filter remains unset ("Alle Serien") and no error is shown

#### Scenario: Arriving without a series parameter
- **WHEN** a user navigates to `/collection` with no `series` parameter
- **THEN** the page behaves exactly as before this change, with no series filter pre-selected
