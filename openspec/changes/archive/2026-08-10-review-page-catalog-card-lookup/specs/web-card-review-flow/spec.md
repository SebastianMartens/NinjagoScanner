## MODIFIED Requirements

### Requirement: Photos are grouped by series and card number
The review page SHALL group every scanned photo by resolving its own sidecar `SetName` and `CardNumber` to a catalog card, using the same normalization the collection overview uses when matching sidecars against the catalog (differences in letter case, whitespace, and formatting are ignored). A group exists if and only if at least one photo's sidecar values resolve to that catalog card.

Series name and card number uniquely identify a catalog card (see GLOSSARY.md's Card entry), so a group corresponds to exactly one catalog card.

#### Scenario: Photos sharing a series and card number are grouped together
- **WHEN** two or more photos have the same `SetName` and `CardNumber` in their sidecar
- **THEN** they appear together in the same group on the review page

#### Scenario: Photos whose raw sidecar values normalize to the same catalog card are grouped together
- **WHEN** two or more photos have `SetName`/`CardNumber` values that differ only in ways normalization ignores (such as case, whitespace, or leading zeros) but resolve to the same catalog card
- **THEN** they appear together in the same group on the review page

#### Scenario: A catalog card with no photos never appears
- **WHEN** a catalog card has no photo whose sidecar `SetName`/`CardNumber` resolves to it
- **THEN** no group for that card is shown on the review page

### Requirement: Groups are ordered by known series order, then card number
Groups, each corresponding to a matched catalog card, SHALL be ordered by that card's series' catalog `SortOrder`, then by `CardNumber` within the series. Every photo whose `SetName`/`CardNumber` pair does not resolve, after normalization, to a catalog card - including a blank `SetName`, a blank `CardNumber`, an unrecognized series, or a card number not found within an otherwise recognized series - SHALL be merged into exactly one catch-all group, sorted after every matched group.

#### Scenario: Groups follow catalog series order
- **WHEN** the review page lists matched groups
- **THEN** they appear ordered by the series' catalog `SortOrder`, and by `CardNumber` within the same series

#### Scenario: Unrecognized and blank series are combined into one trailing group
- **WHEN** photos have a `SetName` that does not resolve to any known catalog series, or have no `SetName` at all
- **THEN** all such photos appear together in a single group that is ordered after every matched group

#### Scenario: A recognized series with an unrecognized card number falls into the catch-all group
- **WHEN** a photo's `SetName` matches a known catalog series but its `CardNumber` does not match any card within that series
- **THEN** that photo is placed in the catch-all group rather than forming its own group

## ADDED Requirements

### Requirement: A matched group's header shows the resolved catalog card name
The review page SHALL show, in a matched group's header, the catalog card name resolved from that group's series name and card number, in addition to the existing series name and card number label. The catch-all group, which does not correspond to a single catalog card, SHALL NOT show a catalog card name in its header.

#### Scenario: Viewing a matched group's header
- **WHEN** a user views a group that resolved to a catalog card
- **THEN** the group header shows that catalog card's name together with the series name and card number

#### Scenario: Viewing the catch-all group's header
- **WHEN** a user views the catch-all group
- **THEN** the group header does not show a catalog card name
