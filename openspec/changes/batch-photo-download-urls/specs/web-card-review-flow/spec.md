## ADDED Requirements

### Requirement: Photo display URLs load without one request per photo
Building the review page's group list SHALL resolve every displayed photo's download URL without issuing a separate download-URL request per photo, so the page's load time does not grow linearly with the number of photos being reviewed.

#### Scenario: Loading the review page with many photos across many groups
- **WHEN** the review page loads its group list for a collection containing hundreds of photos
- **THEN** every photo's display URL is resolved as part of a bounded, small number of requests to PictureService rather than one request per photo
