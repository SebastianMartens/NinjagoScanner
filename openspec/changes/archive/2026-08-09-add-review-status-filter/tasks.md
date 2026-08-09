## 1. Filter control

- [x] 1.1 Add a `reviewStatusFilter` field to `Review.razor` (default `All`) and a `<select>` control in the header offering `All`, `Unreviewed`, `Verified`, `Incorrect`, reusing the existing `GetReviewStatusLabel` labels for the three statuses.
- [x] 1.2 Wire the select's `@bind` (or `@onchange`) to update `reviewStatusFilter` and reset `currentIndex` to `0`.

## 2. Filtered group list

- [x] 2.1 Derive a `FilteredGroups` property/list from `groups` that includes a group when `reviewStatusFilter` is `All`, or when at least one of the group's `Photos` has `ReviewStatus` equal to `reviewStatusFilter`.
- [x] 2.2 Update `CurrentGroup`, the position indicator (`n / total`), `GoToPrevious`, `GoToNext`, and the empty-state check (`groups.Count == 0` vs "all reviewed") to operate over `FilteredGroups` instead of `groups`.
- [x] 2.3 Update `TryFindGroupIndex` (or add a filtered variant) so it searches within `FilteredGroups`, since it's used to re-locate the current group by key after a status/series change.

## 3. Keep filtered list consistent after actions

- [x] 3.1 After `SetReviewStatusAsync`, `ConfirmAllAsync`, and `ReassignSeriesAsync` reload `groups`, ensure the filtered-group lookup re-evaluates against the refreshed data (ReviewStatuses/SetName can move a group in or out of the active filter).
- [x] 3.2 When the previously current group's key is no longer present in `FilteredGroups` (e.g. it dropped out of the filter), fall back to the same index position within `FilteredGroups`, clamped to the last valid index, matching existing fallback behavior in `ReassignSeriesAsync`.
- [x] 3.3 Verify `RestartFromBeginning` resets to the first group of `FilteredGroups` under the currently active filter.

## 4. Manual verification

- [x] 4.1 Run the Web app, open `/review`, and confirm: selecting each filter value shows only matching groups (with all photos of a matching group visible), `Confirm all` advances correctly within a filter, and changing the filter jumps to the first matching group.
