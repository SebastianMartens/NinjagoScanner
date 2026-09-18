## Context

See proposal.md - Why/What Changes for motivation. Today, `gemini_service.analyze_card` is called
from exactly two places - `picture_scanner_service.py`'s `Scan` (batch) and `UploadPhoto` (single
photo, client-streaming) RPCs - and both then build a `SidecarRecord` from the result and write it
via `sidecar_table.py`. Both call sites need to move to the three-stage pipeline; there is no other
caller. `card_analysis_stage_3.py` originally resolved a series by exact-match/evidence-scoring;
stage 3 has since been redesigned to score catalog cards instead (see "Stage 3 scores cards, not
series" under Decisions).

This change depends on `catalog-service-card-class` for the per-card `Class` field used in stage 3's
class scoring, but does not require it to ship first (see Decisions).

## Goals / Non-Goals

**Goals:**
- Make each stage's output independently testable with plain data fixtures, with stage 3 fully
  free of any AI/gRPC-mocking dependency for iteration.
- Keep the sidecar's `Judged` section a stable, typed contract so `NinjagoScanner.Web` is not
  forced to change in this same change.
- Preserve `Scan`'s and `UploadPhoto`'s existing external behavior (batching, retry/skip logic,
  transport-failure abort) - this change is about what happens *inside* "analyze one photo", not
  the RPCs wrapping it.

**Non-Goals:**
- Pinning the exact attribute key vocabulary (`Detected`/`Derived` key names) - this is meant to
  be iterated on prompt-by-prompt once the pipeline exists, not designed up front (explicit user
  direction from the exploration that produced this change).
- Pinning the exact format/rarity value set for derived attributes - same reason.
- Rarity: stage 3 does not read or set it (detection is not implemented); it stays a manually edited Judged field.
- Deciding, field by field, what surfaces on the Review page vs. stays internal - tracked as an
  explicit task (see tasks.md), not a design decision made here.
- Changing photo storage, `AnalysisStatus`/`ReviewStatus` semantics, or `Scan`'s
  batching/skip/delay behavior.

## Decisions

### Sequencing relative to catalog-service-card-class
This change can be implemented before `catalog-service-card-class` ships: stage 3 treats an
unavailable `Class` (empty/not-yet-present field from `CatalogService`) as no class signal - the
number and name still score. Until it ships `catalog_client.py` always returns `card_class=None`,
so the class points and the class-mismatch rule below are inert. Once the other change ships,
`catalog_client.py` starts returning real `Class` values and they start working without further
code changes here, beyond making sure the gRPC message field is actually read.

### Where stage 2's output lands relative to stage 3
Stage 2 (derived attributes) always runs after a successful stage 1, and stage 3 always runs after
a successful stage 2 - the pipeline is strictly sequential, not parallelized, since each stage
consumes the previous stage's output. A failure at stage 1 or 2 short-circuits the rest (see the
`picture-service-catalog-matching` spec's status requirement) - this mirrors today's single-call
behavior where a Gemini failure never reaches series matching at all.

### Reusing vs. duplicating the retry helper
Stage 1 and stage 2 each make their own Gemini call and both need the same retry/transport-failure
classification behavior (`picture-service-gemini-analysis`, modified). Implementation should
factor the existing retry loop in `gemini_service.analyze_card` into a shared helper both stages
call, rather than duplicating the loop - this is an implementation detail, not a spec requirement,
but worth noting since the current retry loop is currently written inline for the single call.

### Stage 3 scores cards, not series
Series detection in stages 1/2 is unreliable or missing, whereas the derived `card_number` is
reliable. Stage 3 therefore no longer resolves a series first: it scores every catalog card across
all series against the *derived* attributes only, and the best card's series is the series.
Score per card (max 100): card number equal 50, class equal 20, card name up to 30 (exact = 30,
similar scaled by similarity, below 0.6 similarity nothing). A card needs 50 to be a candidate
(so the number alone, or class + exact name, is enough; class + a partial name is not) and must be
the single best - a tie between different series/numbers yields no match. The weights and
thresholds are named constants in `card_analysis_stage_3.py`, meant to be tuned.

If both classes are known and differ, the card number earns nothing for that card: a number that
belongs to a card of another class means the number was misread or the class misjudged, and it
should not back the wrong card. That card can still be found through its name, so one wrong signal
does not make a correct card unmatchable. Class remains advisory in the sense that a missing or
unrecognized derived class, or a catalog card without a class, is simply no signal - this follows
from the class taxonomy being a deliberate simplification (`catalog-service-card-class`'s
design.md "best guess" rows).

Name similarity uses normalized-text equality (case/punctuation ignored) for an exact match,
otherwise difflib's ratio, with a fragment contained in the real name counting as at least 0.8 -
to tolerate a wrong-language or partially detected name. Detected attributes are not consulted
for now; the series-name guess and the series-evidence scoring (symbol hint, year, known card
names) are dropped, so `picture-service-series-name-matching` is removed.

### Verified photos keep their series and card number on re-analysis
A `verified` Review Status means a human confirmed series and card number, so re-analysis must not
overturn them. Only `Scan` (with `overwrite_existing_sidecars`) analyzes a photo that already has
a sidecar - `UploadPhoto` always creates a new photo - so `Scan` reads the existing sidecar and,
when it is `verified` with both a series name and a card number, passes them to `analyze_card` as
a `VerifiedMatch`. Stages 1 and 2 still run and replace `Detected`/`Derived`; stage 3 skips
series/card-number resolution and returns the verified values, taking card name from the catalog
entry for them (falling back to the derived guess), and language from the derived
attributes as usual. Status is `ok` since a human already vouched for the match. If stage 1 or 2
fails, the `failed` result still carries the verified series and card number, so a failed
re-scan cannot wipe what a human confirmed. `Scan`'s unexpected-exception fallback does the same.
The pin is passed in rather than read from the sidecar inside `analyze_card`, keeping the
pipeline free of sidecar-store dependencies.

## Risks / Trade-offs

- **[Risk] Two sequential Gemini calls instead of one roughly doubles LLM latency per photo** →
  Accepted trade-off (see proposal.md's Impact); stage 2 is text-only and expected to be faster/
  cheaper than a vision call, so the increase is not a straight 2x. Not re-litigated here.
- **[Risk] Loosely-typed `Detected`/`Derived` sections mean nothing enforces that stage 2's prompt
  and stage 3's matcher agree on key names** → Accepted per explicit user direction (interpretation
  of attribute presence is deliberately left to convention, not schema); mitigated in practice by
  developing stage 2/3 together and by tests exercising real key names end-to-end at the
  integration level, even though unit tests use golden fixtures with whatever keys the fixture
  author chose.
- **[Trade-off] Card-number resolution is new, catalog-grounded behavior where today's pipeline
  had none** → Expected to occasionally reject a card number the model actually read correctly, if
  scoring hits a tie (a number exists in every series, so without a usable name it stays
  ambiguous) or the class check rejects it. This is the intended trade (catalog-grounded number,
  escalated to `failed`/reviewable, over blindly trusting free-form OCR).
- **[Risk] The scoring weights are unvalidated guesses** → tune against real photos; they are
  constants, and `test_card_analysis_stage_3.py` pins the intended behavior (not the numbers).

## Migration Plan

1. Add the `Detected`/`Derived`/`Judged` section shape to `models.py`'s `SidecarRecord` and
   `sidecar_table.py`'s DynamoDB item mapping, alongside the existing flat fields (additive, not
   yet used).
2. Implement stage 1 (attribute detection) as a new function, tested against fakes the same way
   `test_gemini_service.py` fakes today's single call.
3. Implement stage 2 (derived attributes), tested with plain golden `Detected` fixtures (no fake
   Gemini client needed beyond the same `ainvoke` fake pattern, now text-only).
4. Implement stage 3 in `card_analysis_stage_3.py` as card scoring across the whole catalog (see
   Decisions), tested with plain golden `Derived`+catalog-snapshot fixtures - no AI or gRPC
   involved.
5. Wire the three stages together behind the same `analyze_card`-shaped entry point so `Scan` and
   `UploadPhoto` need a minimal call-site change.
6. Switch `sidecar_table.py`'s write path to the three-section shape; keep the read path
   supporting both shapes (legacy flat records surface as `Judged`-only, per
   `picture-service-sidecar-sections`).
7. Extend the existing `MigrateSidecars` RPC to also perform this migration (idempotent, per its
   existing pattern for the `status` → `AnalysisStatus` rename), and run it against production data
   once deployed.
Rollback: revert the code change; legacy-shaped records already migrated are still valid to read
under the old code path only if that old code path is restored too - if migration has already run
against production data before a rollback is needed, note that as a real constraint rather than a
theoretical one (this is the same risk profile as the earlier sidecar/DynamoDB migration referenced
in `picture-service-sidecar-review`).

## Open Questions

- Where does language detection live in the new pipeline - a `Detected` attribute (it's arguably
  "what text is visible") or a `Derived` one (today's prompt already does light inference:
  "Bestimme die Sprache anhand des gedruckten Textes")? Deliberately left open; either stage can
  own it without affecting this change's specs.
- What exactly distinguishes `ok` from `uncertain` once both series and card number are
  confidently resolved (today's single-call pipeline used a fixed 0.65 confidence threshold on
  one model-reported number - the staged pipeline may have per-stage confidence, matching-score-
  based confidence, or something else). Left open per `picture-service-catalog-matching`'s status
  requirement; resolving it does not require touching the specs written for this change, only
  design/implementation of catalog matching's internals.
- The exact `Detected`/`Derived` attribute key vocabulary and the format/rarity value set - by
  design, iterated on after this pipeline exists (see Non-Goals).
- Per-field Review-page exposure (tracked as a task, not resolved here).
