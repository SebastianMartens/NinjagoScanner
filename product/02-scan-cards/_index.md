---
id: "2"
title: Scan cards
status: idea
priority: must
updated: 2026-11-27
---

# Scan cards

## Why
Taking a photo is how a card gets into the collection. A child should not have to type or search,
and must be able to put it right when the app gets a card wrong.

## Capabilities
None written yet.

## Acceptance
All points are *(proposed)* until agreed.

- A user adds a card by photographing it with a phone, without typing.
- The app proposes series, card number and language, and says when it is unsure or has failed.
- A user can check and correct what the app recognised, and a human confirmation is never
  overwritten by the app.
- Many cards can be added in one go.
- A child aged about 7–12 can do this in German without help.

## Open questions
- How well must recognition work to count as good enough? No target is set.
- Which service may recognise cards for this audience? See
  [Safe for children](../01-safe-for-children/_index.md).

## Realised by
Adding photos:
- [web-photo-upload](../../openspec/specs/web-photo-upload/spec.md)
- [picture-service-photo-upload](../../openspec/specs/picture-service-photo-upload/spec.md)
- [picture-service-source-file-listing](../../openspec/specs/picture-service-source-file-listing/spec.md)

Recognising cards:
- [picture-service-gemini-analysis](../../openspec/specs/picture-service-gemini-analysis/spec.md)
- [picture-service-attribute-detection](../../openspec/specs/picture-service-attribute-detection/spec.md)
- [picture-service-derived-attributes](../../openspec/specs/picture-service-derived-attributes/spec.md)
- [picture-service-catalog-matching](../../openspec/specs/picture-service-catalog-matching/spec.md)
- [picture-service-photo-scan](../../openspec/specs/picture-service-photo-scan/spec.md)
- [picture-service-photo-reanalysis](../../openspec/specs/picture-service-photo-reanalysis/spec.md)
- [picture-service-sidecar-sections](../../openspec/specs/picture-service-sidecar-sections/spec.md)

Checking and correcting:
- [web-card-review-flow](../../openspec/specs/web-card-review-flow/spec.md)
- [web-card-review](../../openspec/specs/web-card-review/spec.md)
- [web-review-series-logos](../../openspec/specs/web-review-series-logos/spec.md)
- [web-photo-rotation](../../openspec/specs/web-photo-rotation/spec.md)
- [picture-service-sidecar-editing](../../openspec/specs/picture-service-sidecar-editing/spec.md)
- [picture-service-sidecar-review](../../openspec/specs/picture-service-sidecar-review/spec.md)
- [picture-service-photo-deletion](../../openspec/specs/picture-service-photo-deletion/spec.md)
