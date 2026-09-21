## ADDED Requirements

### Requirement: Series names are unique across detail files
The catalog SHALL treat two series entries whose names are the same after normalization (ignoring case, underscores, hyphens and repeated whitespace) as a data error and SHALL fail loading with a catalog data error naming the duplicate series and the file it was found in. It SHALL NOT silently keep one of them and discard the other.

#### Scenario: Two detail files declare the same series
- **WHEN** two `series_*.json` files each declare a series that normalizes to the same name (e.g. `Serie_1` and `Serie 1`)
- **THEN** loading the catalog fails with a catalog data error that names the duplicate series

#### Scenario: Distinct series in separate files load side by side
- **WHEN** two `series_*.json` files declare series with different names (e.g. `Serie_0` and `Serie_1`)
- **THEN** `ListSeries` returns both, each with its own cards

### Requirement: Series 0 is a distinct series listed first
The shipped catalog SHALL expose the early Spinner-set series as its own series named "Serie 0" with `sort_order` 0, separate from "Serie 1", so it is returned before every other series by `ListSeries`.

#### Scenario: Series 0 and Series 1 are both listed
- **WHEN** a client calls `ListSeries` against the shipped catalog data
- **THEN** the response contains a "Serie 0" entry and a "Serie 1" entry, and "Serie 0" appears before "Serie 1"

#### Scenario: Series 0 lists its real cards
- **WHEN** a client lists all cards from the shipped catalog data
- **THEN** "Serie 0" has 221 cards: Wave 1 numbers 1-81 and `LE1`-`LE9`, Wave 2 numbers `WB1`-`WB125`, `WBLE1`-`WBLE6`

#### Scenario: Series 1 keeps its cards
- **WHEN** a client lists all cards from the shipped catalog data
- **THEN** every card of "Serie 1" is present and none of them has been replaced by a "Serie 0" card

### Requirement: Card numbers are unique within a series
Every card of a series SHALL have a card number that is unique within that series after normalization, so that a series + card-number pair identifies exactly one catalog card. Where a series contains several waves that each number from 1 (Series 0), the later wave's numbers SHALL carry a letters-only prefix (`WB`) that survives normalization and sorts correctly.

#### Scenario: Series 0 waves do not collide
- **WHEN** the shipped catalog data is loaded
- **THEN** no two "Serie 0" cards share a card number, and Wave 2 cards are numbered `WB1`-`WB125` rather than 1-125
