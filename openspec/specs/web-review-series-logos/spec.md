# web-review-series-logos Specification

## Purpose

Lets a user visually compare a card photo's printed series symbol against each series' official logo directly on the Review page's series-reassignment buttons, instead of relying on memory of what each series symbol looks like.

## Requirements

### Requirement: Series reassignment buttons show a mapped logo icon
When a series has a known logo mapping (image and caption), the Review page's reassignment button for that series SHALL display the mapped logo image inline alongside the series name label, with the mapped caption exposed as the image's accessible/alt text.

#### Scenario: Series has a logo mapping
- **WHEN** the Review page renders a reassignment button for a series that has an entry in the logo mapping (e.g. `Serie 2`)
- **THEN** the button shows the series name label exactly as today, plus the mapped logo image inline, with the image's alt text set to the mapped caption

### Requirement: Series without a logo mapping fall back to text only
When a series has no entry in the logo mapping, the Review page's reassignment button for that series SHALL render as a plain text-only button, identical to current behavior, without any icon placeholder or broken-image indicator.

#### Scenario: Series has no logo mapping
- **WHEN** the Review page renders a reassignment button for a series that has no entry in the logo mapping (e.g. `Serie 1`)
- **THEN** the button shows only the series name label, with no icon element rendered

#### Scenario: Series exists in the catalog but its logo has not yet been mapped
- **WHEN** the Review page renders a reassignment button for a series returned by the catalog service that has not yet been added to the logo mapping (e.g. `Serie 10`)
- **THEN** the button behaves identically to a series with no official logo - text only, no icon, no error

### Requirement: Logo icon does not change reassignment behavior
Adding a logo icon to a series-reassignment button SHALL NOT change the button's click behavior: clicking anywhere on the button (icon or label) SHALL still reassign the photo's series exactly as it does today.

#### Scenario: Clicking a button with a logo icon still reassigns the series
- **WHEN** a user clicks a reassignment button that displays a logo icon
- **THEN** the photo's series is reassigned to that button's series, identical to clicking a text-only button
