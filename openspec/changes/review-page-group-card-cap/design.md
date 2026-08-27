## Context

See proposal.md - Why. `Review.razor` renders `group.Photos` (a `CardReviewGroup`'s full photo list, produced by `CollectionQueryService.GetReviewGroupsAsync`) as one flat loop of tiles with no cap; `group.Photos.Count` also drives the "X Foto(s)" label and every filter check (`MatchesReviewStatusFilter`, etc., which use `.Any()` over the full list).

## Goals / Non-Goals

**Goals:**
- Bound the number of photo tiles rendered per group to 18 so the catch-all group (thousands of unmatched photos) no longer makes the page slow.
- Tell the user when a group has more photos than are currently displayed, rather than silently hiding them.

**Non-Goals:**
- Pagination or "load more" within a group — out of scope; the message only informs, it doesn't add a way to see the rest in this change.
- Changing which photos belong to a group, how groups are filtered, or the group order.

## Decisions

**Cap at the view layer (`Review.razor`), not in `CollectionQueryService`/`CardReviewGroup`.**
`group.Photos` stays the full list returned by `GetReviewGroupsAsync`; the Razor page renders `group.Photos.Take(18)` and derives the "more exist" message from `group.Photos.Count > 18`. Filtering (`MatchesReviewStatusFilter`/`MatchesAnalysisStatusFilter`/`MatchesSearchFilter`) and the existing "X Foto(s)" count keep reading the full list, so a group whose 19th photo is the only `incorrect` one is still found by the review-status filter — only *simultaneous display* is capped, not what counts as being "in" the group.
Alternative considered: truncate `CardReviewGroup.Photos` itself in `CollectionQueryService`. Rejected because it would also shrink the count label and make filters blind to photos past the 18th, which is a bigger behavior change than the proposal calls for and would need its own spec delta.

**"Confirm all" acts on the capped (visible) set, using its existing wording.**
The existing requirement already says "every photo currently shown in the group" — with the cap in place at the view layer, "shown" naturally becomes `group.Photos.Take(18)`, without needing new plumbing: `ConfirmAllAsync` iterates whatever the page is currently rendering for that group. No proto or service change needed.

## Risks / Trade-offs

- [A group with >18 photos can never be fully confirmed via "Confirm all" in one click, since confirming the visible 18 doesn't remove already-confirmed photos from the group — the group still has >18 total next load, just with fewer unreviewed, so the filter default (`Unreviewed`) will keep shrinking what's shown each pass] → Acceptable: repeated "Confirm all" clicks converge to zero unreviewed photos in a few passes; this matches how the catch-all group is expected to be worked down over time, and no worse than the pre-change flow for that group being unusable at all.
- [Photos 19+ in a group are simply unreachable through this page until the group drops under 18 (no per-group pagination)] → Acceptable per Non-Goals; the free-text search and status filters remain the way to narrow a large group down to reachable results if a specific photo needs attention sooner.
