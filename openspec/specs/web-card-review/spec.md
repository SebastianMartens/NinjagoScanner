# web-card-review Specification

## Purpose

Lets a person manually validate a scanned card's detected data against its photo in the Web UI, recording that judgment separately from the automated Gemini analysis result.

## Requirements

### Requirement: AnalysisStatus is displayed read-only
The card detail/edit view SHALL display a card's `AnalysisStatus` but SHALL NOT provide any control to edit it directly.

#### Scenario: Viewing a card's analysis result
- **WHEN** a user opens a card's sidecar details in the Collection view
- **THEN** the current `AnalysisStatus` is shown as read-only information, not as an editable input

### Requirement: ReviewStatus is editable via an explicit control
The card detail/edit view SHALL provide an explicit control to set a card's `ReviewStatus` to `unreviewed`, `verified`, or `incorrect`, independent of every other editable field on that view.

#### Scenario: Marking a card as verified
- **WHEN** a user selects `verified` in the review status control and saves
- **THEN** the card's `ReviewStatus` is updated to `verified` and no other sidecar field is changed by that action

#### Scenario: Marking a card as incorrect
- **WHEN** a user selects `incorrect` in the review status control and saves
- **THEN** the card's `ReviewStatus` is updated to `incorrect` and no other sidecar field is changed by that action

#### Scenario: Editing other card fields does not change ReviewStatus
- **WHEN** a user edits and saves any other sidecar field (e.g. card name, card number, set name, rarity) without touching the review status control
- **THEN** the card's `ReviewStatus` is unchanged by that save

### Requirement: Card lists are filterable by ReviewStatus
Card list views SHALL let a user filter the displayed cards by `ReviewStatus`, in addition to any existing filtering by `AnalysisStatus`.

#### Scenario: Filtering to unreviewed cards
- **WHEN** a user selects the `unreviewed` review filter on a card list view
- **THEN** only cards whose `ReviewStatus` is `unreviewed` are shown

#### Scenario: Filtering to cards flagged incorrect
- **WHEN** a user selects the `incorrect` review filter on a card list view
- **THEN** only cards whose `ReviewStatus` is `incorrect` are shown
