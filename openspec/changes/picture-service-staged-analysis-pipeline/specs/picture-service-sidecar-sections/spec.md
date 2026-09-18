## Purpose

Defines the sidecar record's three-section shape - Detected, Derived, and Judged - separating what the AI saw from what it inferred from what was ultimately decided, and how existing flat sidecar records migrate into it.

## ADDED Requirements

### Requirement: Sidecar records have three sections
A sidecar record SHALL organize its data into three sections: `Detected` (attribute-detection's output), `Derived` (derived-attributes' output), and `Judged` (catalog-matching's output, plus `AnalysisStatus` and `ReviewStatus`).

#### Scenario: A freshly analyzed photo's sidecar has all three sections
- **WHEN** a photo completes all three analysis stages
- **THEN** its sidecar record's `Detected`, `Derived`, and `Judged` sections are all populated from the corresponding stage's output

### Requirement: Detected and Derived sections accept arbitrary keys
The `Detected` and `Derived` sections SHALL store whatever flat key-value pairs their producing stage returned, without requiring a fixed set of keys and without rejecting a record for containing keys that were not present in previously stored records.

#### Scenario: A new attribute key appears without a schema change
- **WHEN** an attribute-detection or derived-attributes call returns a key not seen in any previously stored sidecar record
- **THEN** the sidecar record is stored successfully with that key present in the relevant section

### Requirement: The Judged section keeps a fixed set of fields
Unlike `Detected` and `Derived`, the `Judged` section SHALL expose a fixed, typed set of fields (at minimum: series name, card number, card name, rarity, language, analysis status, and review status) that downstream consumers (including `NinjagoScanner.Web`) can rely on being present by name, whether or not catalog matching resolved a confident value for each.

#### Scenario: Unresolved fields are still present, just empty or raw
- **WHEN** catalog matching does not confidently resolve a given Judged field
- **THEN** that field is still present in the Judged section (empty, null, or holding a raw guess per `picture-service-catalog-matching`), not omitted from the record's shape

### Requirement: Pre-existing flat sidecar records remain readable
A sidecar record written before this change (a single flat set of fields, no `Detected`/`Derived`/`Judged` sections) SHALL be readable without requiring the photo to be rescanned: its existing fields SHALL be surfaced as the `Judged` section's fields, and its `Detected`/`Derived` sections SHALL be empty.

#### Scenario: Reading a legacy flat sidecar record
- **WHEN** a sidecar record on disk/in storage has no `Detected`/`Derived`/`Judged` structure, only the legacy flat fields
- **THEN** reading it produces a record whose `Judged` section reflects those legacy fields and whose `Detected`/`Derived` sections are empty

### Requirement: A migration operation converts legacy records to the new shape
A dedicated migration operation SHALL be available to rewrite legacy flat sidecar records into the three-section shape, and SHALL be safe to run repeatedly (records already in the three-section shape are left unchanged).

#### Scenario: Migrating legacy sidecar records
- **WHEN** the migration operation is run against a collection containing legacy flat sidecar records
- **THEN** each such record is rewritten into the three-section shape, with its legacy fields moved into `Judged` and empty `Detected`/`Derived` sections

#### Scenario: Migration is idempotent
- **WHEN** the migration operation is run again against records already in the three-section shape
- **THEN** those records are left unchanged and no error occurs
