## MODIFIED Requirements

### Requirement: The Judged section keeps a fixed set of fields
Unlike `Detected` and `Derived`, the `Judged` section SHALL expose a fixed, typed set of fields (at minimum: series name, card number, card name, rarity, language, analysis status, review status, and the rotation flag) that downstream consumers (including `NinjagoScanner.Web`) can rely on being present by name, whether or not catalog matching resolved a confident value for each.

#### Scenario: Unresolved fields are still present, just empty or raw
- **WHEN** catalog matching does not confidently resolve a given Judged field
- **THEN** that field is still present in the Judged section (empty, null, or holding a raw guess per `picture-service-catalog-matching`), not omitted from the record's shape

#### Scenario: Rotation flag defaults to unrotated
- **WHEN** a sidecar record's rotation flag has never been explicitly set
- **THEN** the Judged section reports it as unrotated (not set/rotated)
