## MODIFIED Requirements

### Requirement: The Judged section keeps a fixed set of fields
Unlike `Detected` and `Derived`, the `Judged` section SHALL expose a fixed, typed set of fields (at minimum: series name, card number, card name, language, analysis status, and review status) that downstream consumers (including `NinjagoScanner.Web`) can rely on being present by name, whether or not catalog matching resolved a confident value for each. The `Judged` section SHALL NOT contain a rarity: a card's rarity is catalog data, and a detected or derived rarity is only evidence stored in the `Detected` and `Derived` sections. Stored records that still contain a rarity value SHALL remain readable, with that value ignored.

#### Scenario: Unresolved fields are still present, just empty or raw
- **WHEN** catalog matching does not confidently resolve a given Judged field
- **THEN** that field is still present in the Judged section (empty, null, or holding a raw guess per `picture-service-catalog-matching`), not omitted from the record's shape

#### Scenario: A legacy stored rarity is ignored
- **WHEN** a stored sidecar record contains a `Rarity` value from before this change
- **THEN** the record is read successfully, the value is not exposed through any API, and it is not written back on the next save

#### Scenario: A derived rarity stays in the Derived section
- **WHEN** stage 2 derives a rarity for a photo
- **THEN** it is stored under the `Derived` section's `rarity` key and no Judged field is set from it
