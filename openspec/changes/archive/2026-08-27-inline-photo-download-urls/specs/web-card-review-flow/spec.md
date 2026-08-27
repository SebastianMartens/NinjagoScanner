## MODIFIED Requirements

### Requirement: Photo display URLs load without one request per photo
Building the review page's group list SHALL resolve every displayed photo's download URL without issuing any additional download-URL request to PictureService beyond the one call that lists the cards, so the page's load time does not grow linearly with the number of photos being reviewed.

#### Scenario: Loading the review page with many photos across many groups
- **WHEN** the review page loads its group list for a collection containing hundreds of photos
- **THEN** every photo's display URL is already present on the data returned by the single call that lists the cards, and no further request to PictureService is made to resolve any of them
