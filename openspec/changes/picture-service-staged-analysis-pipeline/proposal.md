## Why

Today, `gemini_service.analyze_card` does everything in one Gemini vision call: read the photo,
guess card name/number/set name/rarity/language, and reason about which catalog series it belongs
to, all against a prompt that embeds the entire series catalog. Only the set name gets any
catalog-grounded correction afterward (`series_catalog_service.resolve_set_name`); card number is
trusted verbatim from the model with no cross-check. This makes the pipeline hard to improve
incrementally - a prompt tweak aimed at reading card numbers better can silently change series
matching too, and there's no way to test "did we read the card correctly" independently of "did we
match it to the right catalog entry." Splitting analysis into three independently testable stages
(what's on the photo → what that implies → which catalog card it is) lets each stage be iterated
and tested on its own, and makes card-number resolution catalog-grounded for the first time.

## What Changes

- **BREAKING** (internal pipeline & storage, not a public API in the usual sense - see Impact):
  `gemini_service.analyze_card`'s single Gemini call is replaced by three stages:
  1. **Attribute detection** - a Gemini vision call, photo only, no series-catalog prompt. Returns
     a generic, open-ended key-value map of what's visible (numbers at positions, text, symbols,
     colors...); flat scalars only, position encoded in the key name (e.g. `number_top_left`).
  2. **Derived attributes** - a second, separate Gemini call, text-only (does not re-see the
     photo), reasoning over stage 1's output. Returns another generic key-value map, including a
     `class` value constrained to the catalog's fixed set (`character`, `action`, `vehicle`,
     `puzzle-piece`, `trap`, `limited edition`, `art` - see `catalog-service-card-class`) and a
     format/rarity attribute (exact value set deliberately left open - iterated on later).
  3. **Catalog matching** - deterministic code, no LLM call. Scores every catalog card (all
     series) against stage 2's derived `card_number`, `class` and `card_name` (a number whose
     catalog class differs from the derived class counts for nothing; names match by similarity);
     the best card determines the series and card number. Produces the final judged series/card
     number/card name/language/status.
- The sidecar record is restructured from one flat set of fields into three sections: `Detected`
  (stage 1's generic map), `Derived` (stage 2's generic map), and `Judged` (stays a fixed/typed
  structure - the stable contract `NinjagoScanner.Web` already depends on: series name, card
  number, card name, rarity, language, analysis status, review status). Existing production
  sidecar records are migrated to the new shape via the existing `MigrateSidecars` RPC, following
  the same pattern already used for the `status` → `AnalysisStatus` rename
  (`picture-service-sidecar-review`'s "Legacy sidecar files remain usable and can be migrated").
- Both `Scan` and `UploadPhoto` (the two entry points that currently call
  `gemini_service.analyze_card` directly) are updated to run all three stages.
- Whether each currently-flat sidecar field is surfaced on `NinjagoScanner.Web`'s Review page (or
  elsewhere) versus kept as a PictureService-internal implementation detail is **not decided by
  this proposal** - it's an explicit per-field task to work through during implementation (see
  tasks.md).

## Capabilities

### New Capabilities
- `picture-service-attribute-detection`: the stage 1 Gemini vision call - request shape (photo
  only, no series-catalog prompt), retry/failure semantics, and the generic key-value output
  contract.
- `picture-service-derived-attributes`: the stage 2 Gemini call - text-only input (stage 1's
  output, no photo), retry/failure semantics, and the generic key-value output contract,
  including the constraint that `class` is always one of the catalog's fixed values.
- `picture-service-catalog-matching`: stage 3's deterministic card scoring (card number, class,
  card name) across the whole catalog, which determines the series and card number, and how the
  three stages' outputs combine into the `Judged` section's analysis status.
- `picture-service-sidecar-sections`: the sidecar record's three-section shape (`Detected`/
  `Derived` generic, `Judged` fixed/typed) and migration of pre-existing flat sidecar records.

### Modified Capabilities
- `picture-service-gemini-analysis`: the single-call request/retry contract is superseded by
  stage 1's contract (new capability above); this delta narrows the existing capability to what
  still applies unchanged (transport-vs-content failure classification as a pattern reused by
  both LLM stages) and removes what no longer applies to a single call (the series-catalog
  prompt; confidence/status normalization and language detection, which move to
  `picture-service-catalog-matching` and `picture-service-derived-attributes` respectively).
- `picture-service-series-name-matching`: removed entirely. Series detection in stages 1/2 is
  unreliable, so a series is no longer resolved from a name guess or evidence text; it follows
  from the card matched by `picture-service-catalog-matching`.

## Impact

- **Code**: `picture_service/src/picture_service/{gemini_service.py, card_analysis_stage_3.py,
  models.py, sidecar_table.py, picture_scanner_service.py}`. `catalog_client.py` gains a
  dependency on `catalog-service-card-class`'s `Class` field.
- **Storage**: DynamoDB sidecar item shape changes (see `picture-service-sidecar-sections`);
  existing items are migrated via `MigrateSidecars`, not replaced destructively.
- **Cost/latency**: one Gemini call per photo becomes two (stage 1 vision + stage 2 text-only,
  expected cheaper/faster than stage 1) plus a free deterministic stage 3 - an accepted trade-off,
  not re-litigated by this change (see design.md).
- **Testing**: stage 1 keeps today's "fake the `StructuredModel`/`ainvoke` boundary" testing
  approach (no real photo fixtures exist or are added by this change). Stages 2 and 3 become
  newly, cheaply unit-testable with plain golden key-value fixtures and no live Gemini or gRPC
  calls required to iterate - this is the main testability benefit driving the change.
- **Dependency**: stage 3's `Class`-based narrowing depends on `catalog-service-card-class`
  shipping first; this change can still be implemented and merged independently by treating an
  unavailable `Class` as "no narrowing signal" until that change lands (see design.md).
- **Not affected**: photo storage (S3), `AnalysisStatus`/`ReviewStatus` semantics
  (`picture-service-sidecar-review`), `Scan`'s batching/skip/retry/delay behavior
  (`picture-service-photo-scan`), and `NinjagoScanner.Web`'s gRPC-facing contract for the fields
  that remain in `Judged`.
