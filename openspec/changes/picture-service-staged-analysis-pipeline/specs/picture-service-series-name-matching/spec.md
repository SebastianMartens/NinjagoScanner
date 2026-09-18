## MODIFIED Requirements

### Requirement: Exact series name match wins
If the detected/derived attributes' series-name guess matches a known series name (case-insensitive, whitespace-normalized), that series' canonical name SHALL be used as the resolved set name.

#### Scenario: Case-insensitive exact match
- **WHEN** the series-name guess found in the detected/derived attributes differs from a known series name only in case or surrounding whitespace
- **THEN** the resolved set name is the catalog's canonical series name

### Requirement: Evidence-based matching when no exact name match exists
When there is no exact series name match, the resolved set name SHALL be determined by scoring each known series against the evidence available in the detected and derived attribute values (whichever of them carry series-relevant signal, such as a series-name guess, character/card-name text, or other detected text), using the highest-scoring series if there is a unique winner.

#### Scenario: Series matched by symbol/logo hint
- **WHEN** the evidence text contains a series' known symbol/logo hint but not its exact name
- **THEN** that series is a scoring candidate and is selected if it scores higher than all others

#### Scenario: Series matched by known card name
- **WHEN** the evidence text contains one of a series' known card names
- **THEN** that series is a scoring candidate and is selected if it scores higher than all others

#### Scenario: Series matched by year
- **WHEN** the evidence text contains a series' year and no stronger signal (name, symbol, or card name) is present
- **THEN** that series contributes only the lower year-based score toward the match

#### Scenario: "No symbol" evidence favors Serie 1
- **WHEN** the evidence text contains phrasing indicating the absence of a symbol or logo (e.g. "no symbol", "no logo")
- **THEN** "Serie 1" is scored highly as a candidate match

### Requirement: Empty catalog falls back to the model's raw guess
If the loaded series catalog is empty, the resolved set name SHALL be the detected/derived attributes' series-name guess (trimmed), without attempting exact or evidence-based matching.

#### Scenario: No catalog loaded
- **WHEN** the series catalog contains zero entries
- **THEN** the resolved set name is the series-name guess found in the detected/derived attributes, trimmed, with no catalog validation applied
