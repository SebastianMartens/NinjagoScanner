## Why

The /review page lists the "Ohne bekannte Serie" group (photos whose series/card number don't resolve to a catalog card) after every matched group. These are the photos that most need attention - the AI couldn't place them - yet they sit at the very end, so a reviewer has to page through the whole collection to reach them.

## What Changes

- The catch-all group ("no series" group) is ordered **first** on the /review page, before every matched group, instead of last.
- Matched groups keep their existing order (series `SortOrder`, then card number).
- Consequence: with the default filters the page opens on the catch-all group when it has unreviewed photos; "Weiter" then proceeds through the matched groups in catalog order.

## Capabilities

### New Capabilities

### Modified Capabilities
- `web-card-review-flow`: the "Groups are ordered by known series order, then card number" requirement changes the catch-all group's position from after every matched group to before every matched group.

## Impact

- `NinjagoScanner.Web/Services/CollectionQueryService.cs`: `BuildReviewGroups` puts the catch-all group at the start of the list.
- `NinjagoScanner.Web/Models/CardReviewGroup.cs`: doc comment says "trailing bucket" - update wording.
- `NinjagoScanner.Web.Tests`: existing ordering assertions ("catch-all group last") flip to first.
- No gRPC/proto, CatalogService or PictureService changes.
