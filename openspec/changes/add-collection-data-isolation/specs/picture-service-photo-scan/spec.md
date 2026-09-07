## ADDED Requirements

### Requirement: Scan operates within one collection
`Scan` SHALL restrict its batch processing — validating prerequisites, enumerating images, analyzing, and reporting — to the photos belonging to the given `collection_id`, and SHALL NOT process or report on photos belonging to any other collection.

#### Scenario: Scanning one collection does not touch another
- **WHEN** `Scan` is invoked with a `collection_id` for a collection that has unanalyzed photos, while a different collection also has unanalyzed photos
- **THEN** only the specified collection's photos are analyzed, and the returned `ScanSummary` counts do not include the other collection's photos
