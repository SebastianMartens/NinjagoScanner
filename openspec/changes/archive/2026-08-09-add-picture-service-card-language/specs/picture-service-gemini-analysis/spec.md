## ADDED Requirements

### Requirement: Analysis result includes a detected language
The parsed analysis result SHALL include a `Language` value normalized to one of `de`, `en`, or `unknown`: a model-reported value matching `de` or `en` (case-insensitively) is kept as that value, and any other, unrecognized, or missing value is normalized to `unknown`.

#### Scenario: Model reports German
- **WHEN** the model payload's language is `de`
- **THEN** the analysis result's `Language` is `de`

#### Scenario: Model reports English in a different case
- **WHEN** the model payload's language is `EN`
- **THEN** the analysis result's `Language` is `en`

#### Scenario: Model reports a language outside the closed set
- **WHEN** the model payload's language is a value other than `de` or `en` (e.g. `fr`)
- **THEN** the analysis result's `Language` is `unknown`

#### Scenario: Model omits the language entirely
- **WHEN** the model payload does not include a language value
- **THEN** the analysis result's `Language` is `unknown`
