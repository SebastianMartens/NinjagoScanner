## Context

See proposal.md - Why. `CollectionQueryService.BuildReviewGroups` builds the ordered group list: matched groups sorted by series `SortOrder` then card number, then the catch-all group appended when it has photos. `ReviewSession` keeps the list, applies filters, and tracks `CurrentIndex` into the filtered list; `Review.razor` only renders whatever `ReviewSession` exposes.

## Goals / Non-Goals

**Goals:**
- The catch-all group is first in `BuildReviewGroups`' result.

**Non-Goals:**
- No change to how photos are assigned to the catch-all group, its header text, or the ordering of matched groups.
- No new UI control (e.g. a toggle for catch-all position).

## Decisions

**Change the order at the single place it is decided.** `BuildReviewGroups` inserts the catch-all group at index 0 instead of appending it. `ReviewSession` and `Review.razor` need no change because they never assume the catch-all group is last: filtering keeps relative order, `Key`-based lookups (`Regroup`, `FindNextFilteredIndexAfter`) are position-independent, `TryShowCard` skips catch-all groups, and Prev/Next/"Confirm all" advance by index in the list. Alternative considered: sorting in `ReviewSession` or the page - rejected, it would split the ordering rule across two layers and leave `BuildReviewGroups` documenting the opposite.

**Opening position follows the order.** `CurrentIndex` starts at 0, so the page now opens on the catch-all group whenever it survives the active filters (default filter: has unreviewed photos). That is the intent of the change (surface un-placed photos first); no special-casing.

## Risks / Trade-offs

- [Position-based tests break] Existing tests assert the catch-all group is last (`groups[^1]`, `groups[3]`) → update them to expect it first.
- [Confirm-all after the catch-all group] "Confirm all" on the catch-all group advances to the next matching group, which is now the first matched group rather than the end-of-list state → this is consistent with the general advance rule; no code change.
