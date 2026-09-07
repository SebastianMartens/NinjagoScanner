## MODIFIED Requirements

### Requirement: ListCards reports every supported image in the directory
`ListCards` SHALL return one `CardEntry` for every file with a supported image extension in the resolved card photos directory belonging to the given `collection_id`, and SHALL return an empty list if the directory does not exist or the collection has no photos.

#### Scenario: Directory with images
- **WHEN** `ListCards` is called with a `collection_id` for a collection whose directory contains supported image files
- **THEN** the response contains exactly one `CardEntry` per supported image file belonging to that collection

#### Scenario: Directory does not exist
- **WHEN** `ListCards` is called and the resolved card photos directory for the given `collection_id` does not exist
- **THEN** the response contains an empty list of cards

#### Scenario: Another collection's photos are excluded
- **WHEN** `ListCards` is called with a `collection_id`, and a different collection also has supported image files
- **THEN** the response does not include any `CardEntry` for the other collection's images
