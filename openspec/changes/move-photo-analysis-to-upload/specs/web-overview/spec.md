## REMOVED Requirements

### Requirement: A manual Gemini scan can be triggered
**Reason**: AI Analysis runs automatically after a single-photo upload and on per-photo re-analysis during review. A manual batch analysis is only needed for batch-uploaded photos, so the action moves to the batch upload section of `/upload` and gets a provider-neutral label.
**Migration**: Use the batch-analysis action on `/upload` (see `web-photo-upload`, "Photos that have not been analyzed can be analyzed from the upload page"). It runs the same scan and shows the same summary.
