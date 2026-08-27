## ADDED Requirements

### Requirement: ListCards includes a ready-to-use download URL on every entry
`ListCards` SHALL include a working, short-lived download URL on every `CardEntry` it returns, so callers can display or link to the photo without a separate request for its download URL.

#### Scenario: Every entry carries a download URL
- **WHEN** `ListCards` is called against a directory containing photos
- **THEN** every returned `CardEntry` includes a download URL that can be used immediately to fetch that photo's bytes

### Requirement: ListCards resolves photo existence and sidecar data via bulk reads
`ListCards` SHALL determine which photos exist and read their sidecar data using a bounded, small number of bulk operations, rather than issuing one existence check or one sidecar read per photo, so its response time does not grow linearly with the number of photos.

#### Scenario: Listing hundreds of photos
- **WHEN** `ListCards` is called against a directory containing hundreds of photos
- **THEN** determining which photos exist and reading their sidecar data together take a bounded, small number of underlying storage requests rather than one pair of requests per photo
