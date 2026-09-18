## Purpose

Defines the third stage of card analysis: deterministic, non-LLM matching of the detected and derived attributes against the catalog to resolve a card's series and card number, producing the sidecar's Judged section.

## ADDED Requirements

### Requirement: Catalog matching runs without an LLM call
Resolving a photo's series, card number, and card name from its detected and derived attributes SHALL NOT require any call to the Gemini API or any other language model.

#### Scenario: Matching uses only catalog data and prior stage output
- **WHEN** catalog matching runs for a photo whose attribute-detection and derived-attributes stages both succeeded
- **THEN** the series/card-number/card-name resolution is computed entirely from the detected attributes, derived attributes, and the catalog snapshot already loaded from `CatalogService`, with no additional outbound Gemini call

### Requirement: Series is resolved using the existing evidence-matching rules
Series resolution SHALL follow the exact-match-then-evidence-scoring-then-tie-breaks-to-null rules defined in `picture-service-series-name-matching`, sourcing its evidence from the detected and derived attribute values rather than a single model payload's named fields.

#### Scenario: Series resolved from detected/derived evidence
- **WHEN** catalog matching evaluates a photo's detected and derived attributes against the catalog
- **THEN** the resolved series follows `picture-service-series-name-matching`'s exact-match, evidence-scoring, and tie-break rules

### Requirement: Card number is resolved within the matched series
When a series is resolved, card number resolution SHALL follow the same category of rules as series resolution (an exact match against a known card number in that series wins; otherwise the highest-scoring candidate from the available evidence is used if there is a unique winner; a tie yields no match), additionally using the derived `class` (when present) to narrow candidates to cards of that class within the series before scoring.

#### Scenario: Exact card number match within the resolved series
- **WHEN** a detected attribute contains a value that exactly matches a known card number in the resolved series
- **THEN** that card number is the resolved card number

#### Scenario: Class narrows ambiguous card-number candidates
- **WHEN** more than one card in the resolved series shares a card-number candidate under evidence scoring, and the derived `class` matches only one of those candidates' catalog class
- **THEN** the candidate whose catalog class matches the derived class is preferred

#### Scenario: No series resolved means no card number is attempted
- **WHEN** series resolution finds no confident match
- **THEN** card number resolution is not attempted, and no card number is reported

### Requirement: Judged analysis status reflects detection, derivation, and matching outcomes
The sidecar's `Judged` section's analysis status SHALL be `failed` when attribute detection or derived-attribute computation failed for the photo, or when catalog matching finds no confident series match, or finds a confident series but no confident card-number match. When both series and card number are confidently resolved, the status SHALL be `ok` or `uncertain` (never `failed`); the specific split between `ok` and `uncertain` in that case is not fixed by this requirement (see design.md's Open Questions).

#### Scenario: Attribute detection failed
- **WHEN** the attribute-detection stage failed for a photo
- **THEN** the Judged analysis status is `failed`, and catalog matching is not attempted

#### Scenario: Derived-attribute computation failed
- **WHEN** attribute detection succeeded but derived-attribute computation failed
- **THEN** the Judged analysis status is `failed`, and catalog matching is not attempted

#### Scenario: No confident series match
- **WHEN** attribute detection and derived-attribute computation both succeeded, but series resolution finds no confident match
- **THEN** the Judged analysis status is `failed`

#### Scenario: Confident series but no confident card number match
- **WHEN** a series is confidently resolved but card-number resolution finds no confident match within it
- **THEN** the Judged analysis status is `failed`

#### Scenario: Confident series and card number
- **WHEN** both series and card number are confidently resolved
- **THEN** the Judged analysis status is `ok` or `uncertain`, never `failed`

### Requirement: Verified series and card number survive re-analysis
When a photo that already has a sidecar is analyzed again and that sidecar's Review Status is `verified`, a human has already confirmed its series and card number, so analysis SHALL NOT judge them again. Attribute detection and derived-attribute computation SHALL still run and replace the `Detected` and `Derived` sections, but the `Judged` section's series name and card number SHALL keep their existing values instead of being resolved from the new attributes. The remaining Judged fields (card name, rarity, language) SHALL still be recomputed, with the card name taken from the catalog entry for the verified series and card number when one exists. A `verified` sidecar that has no series name or no card number has nothing to keep, and is analyzed like any other photo.

#### Scenario: Re-analysis of a verified photo keeps series and card number
- **WHEN** a photo whose sidecar has Review Status `verified`, series "Serie 1" and card number "1" is analyzed again, and the new attributes point to a different series and card number
- **THEN** detection and derivation run again and replace the `Detected` and `Derived` sections, and the Judged series name and card number remain "Serie 1" and "1"

#### Scenario: Verified photo is not failed for lack of a confident match
- **WHEN** a `verified` photo is analyzed again and the new attributes would not confidently resolve any series or card number
- **THEN** the Judged analysis status is `ok`, with the verified series and card number unchanged

#### Scenario: Stage failure does not discard the verified series and card number
- **WHEN** a `verified` photo is analyzed again and attribute detection or derived-attribute computation fails
- **THEN** the Judged analysis status is `failed`, and the Judged series name and card number remain the verified values

#### Scenario: Photos that are not verified are judged again
- **WHEN** a photo whose Review Status is `unreviewed` or `incorrect` is analyzed again
- **THEN** series and card number are resolved from the new attributes as for a first analysis

### Requirement: Unresolved matches preserve the raw guess rather than storing nothing
When series or card number resolution fails to find a confident match, the Judged section's corresponding field SHALL still be populated with the best available raw guess from the detected/derived attributes (if any), rather than being left empty, so a human reviewer has something to correct from.

#### Scenario: Unresolved series keeps the raw guess
- **WHEN** series resolution finds no confident match but the derived attributes include a series-name guess
- **THEN** the Judged section's series name field is set to that raw guess, and the analysis status is `failed`
