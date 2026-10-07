## MODIFIED Requirements

### Requirement: Selecting a card loads its full details
Selecting a card row SHALL load that card's series metadata (year, logo, theme, highlights), its catalog rarity and its matching photos, and SHALL clear the currently displayed details while loading.

#### Scenario: Selecting a card
- **WHEN** a user clicks a card row (or navigates to it via keyboard)
- **THEN** the detail pane shows a loading state, then the card's title, series/category/number, rarity, metadata, and its list of matching photos once loaded

#### Scenario: Card has no matching photos
- **WHEN** the selected card has no matching photos
- **THEN** the detail pane indicates no photo is available for the card and offers no way to open the card in the review page

## REMOVED Requirements

### Requirement: A selected photo's sidecar can be edited and saved
**Reason**: The review page offers complete sidecar editing (series, card number, language, review status, re-analysis, deletion), so the duplicate edit form on `/collection` is dropped. Its free-text rarity field is obsolete because rarity is catalog data.
**Migration**: Use the "open in review" action in the collection detail pane (see "The detail pane links to the review page for the card") to edit a photo's sidecar on `/review`. Fields without a review-page control (confidence, reasoning summary, detected text, error message, card name) are no longer manually editable.

## ADDED Requirements

### Requirement: The detail pane links to the review page for the card
When the selected card has at least one matching photo, the detail pane SHALL provide a button that opens the review page on that card's group, using the review page's card address (series name and card number). The collection page SHALL NOT provide a form for editing a photo's sidecar.

#### Scenario: Opening the card in the review page
- **WHEN** a user activates the review button for a selected card that has matching photos
- **THEN** the review page opens on the group for that card's series and card number

#### Scenario: No review button without photos
- **WHEN** the selected card has no matching photos
- **THEN** the detail pane shows no review button

#### Scenario: No sidecar edit form
- **WHEN** a user selects any card on the collection page
- **THEN** the detail pane contains no editable sidecar fields

### Requirement: The detail pane shows the card's catalog rarity
The detail pane SHALL show the selected card's rarity as defined by the catalog (`common`, `limited` or `legendary`), read-only and independent of whether the card has photos.

#### Scenario: Limited card
- **WHEN** a user selects a card whose catalog rarity is `limited`
- **THEN** the detail pane shows that rarity

#### Scenario: Card without photos still shows its rarity
- **WHEN** a user selects a card that has no photos
- **THEN** the detail pane still shows the card's catalog rarity
