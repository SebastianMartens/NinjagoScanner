## REMOVED Requirements

### Requirement: ListCards includes a ready-to-use download URL on every entry
**Reason**: Presigning a download URL for every photo in the collection on every `ListCards` call costs CPU time proportional to total collection size, not to how many photos any caller actually displays (Review shows at most 18 at a time; Gallery shows at most one photo per catalog card within one series). Measured against a 10,354-photo collection, this cost alone was 4.62s of a ~9.8s call. See `picture-service-photo-download`'s bounded resolution RPC.

**Migration**: Callers that read `CardEntry.download_url` (Review, Gallery, the Collection page's per-card photo list) resolve download URLs for the specific photo IDs they intend to display via the bounded RPC added to `picture-service-photo-download`, instead of reading the field from `ListCards`'s response.

#### Scenario: Every entry carries a download URL
- **WHEN** `ListCards` is called against a directory containing photos
- **THEN** every returned `CardEntry` includes a download URL that can be used immediately to fetch that photo's bytes
