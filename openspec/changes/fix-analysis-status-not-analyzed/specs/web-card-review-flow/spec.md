## MODIFIED Requirements

### Requirement: Groups can be filtered by analysis status
The review page SHALL provide an analysis-status filter control offering `All`, `Ok`, `Uncertain`, `Failed`, and `NotAnalyzed`. When a status other than `All` is selected, a group SHALL be included in the list used for display and navigation if and only if at least one of its photos currently has that `AnalysisStatus`; every photo in an included group SHALL still be shown, regardless of that individual photo's own `AnalysisStatus`. Selecting `All` removes this filter's constraint, matching the page's behavior without it. This filter combines with the review-status filter and the free-text search filter using AND: a group is included only if it satisfies this filter and every other currently active filter.

#### Scenario: Filtering to groups with a failed photo
- **WHEN** a user selects `Failed` in the analysis-status filter
- **THEN** only groups containing at least one photo whose `AnalysisStatus` is `failed` are shown, and every photo in each shown group is displayed regardless of its own `AnalysisStatus`

#### Scenario: A group matching the filter keeps its differently-analyzed photos visible
- **WHEN** the active analysis-status filter is `Uncertain` and a matching group also contains photos that are `ok` or `failed`
- **THEN** all of that group's photos remain visible, not only the ones with `AnalysisStatus` `uncertain`

#### Scenario: Clearing the analysis-status filter
- **WHEN** a user selects `All` in the analysis-status filter
- **THEN** every group excluded solely by that filter becomes eligible again, subject to the review-status filter and free-text search filter still being satisfied

#### Scenario: Filtering to groups with a not-analyzed photo
- **WHEN** a user selects `NotAnalyzed` in the analysis-status filter
- **THEN** only groups containing at least one photo whose `AnalysisStatus` is `notAnalyzed` are shown, and every photo in each shown group is displayed regardless of its own `AnalysisStatus`
