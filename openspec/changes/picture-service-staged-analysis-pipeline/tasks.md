## 1. Sidecar shape (additive first, per design.md's Migration Plan)

- [ ] 1.1 Add `Detected`/`Derived`/`Judged` section support to `models.py`'s `SidecarRecord` and `sidecar_table.py`'s DynamoDB item mapping, alongside the existing flat fields, and verify existing tests (`test_models.py`, `test_sidecar_table.py`) still pass unmodified
- [ ] 1.2 Add read-path support for legacy flat records (surfaced as `Judged`-only, empty `Detected`/`Derived`), and add a test covering a legacy-shaped fixture round-tripping correctly

## 2. Stage 1: attribute detection

- [ ] 2.1 Write the attribute-detection prompt/schema (photo only, no series catalog, generic key-value output) and implement the call, reusing/factoring the existing retry loop from `gemini_service.analyze_card`
- [ ] 2.2 Add tests faking the `StructuredModel`/`ainvoke` boundary (same pattern as `test_gemini_service.py`) covering: successful detection, retryable failure then success, retries exhausted, non-retryable failure, malformed/empty output
- [ ] 2.3 Verify `picture-service-attribute-detection`'s scenarios are all covered by tests

## 3. Stage 2: derived attributes

- [ ] 3.1 Implement the derived-attributes call (text-only input built from stage 1's output, no image), including validating `class` against the fixed 7-value set and treating an unrecognized value as absent
- [ ] 3.2 Add tests with plain golden `Detected`-attribute fixtures (fake `ainvoke` boundary) covering: successful derivation, recognized/unrecognized class values, retry/failure semantics
- [ ] 3.3 Verify `picture-service-derived-attributes`'s scenarios are all covered by tests

## 4. Stage 3: catalog matching

- [ ] 4.1 Extend `series_catalog_service.py`'s evidence-scoring to read evidence from `Detected`/`Derived` attribute values instead of `GeminiCardPayload`'s named fields, and verify existing series-matching tests (`test_series_catalog_service.py`) still pass with updated fixtures
- [ ] 4.2 Add card-number resolution within the matched series (exact match, then evidence scoring, then tie-breaks-to-null), with class-based narrowing when the catalog's per-card `Class` is available (treat missing/unrecognized `Class` as no narrowing signal, not a blocker - see design.md's "Class-based narrowing is advisory")
- [ ] 4.3 Add tests with plain golden `Detected`+`Derived`+fake-catalog-snapshot fixtures (no AI, no gRPC) covering: exact card-number match, evidence-scored match, tie yields no match, class narrows an ambiguous tie, no series resolved skips card-number resolution
- [ ] 4.4 Implement the Judged analysis-status combination rules (failed on stage 1/2 failure, failed on unresolved series, failed on unresolved card number, ok/uncertain otherwise) and add tests for each scenario in `picture-service-catalog-matching`
- [ ] 4.5 Implement "unresolved match preserves the raw guess" for both series and card number, with a test

## 5. Wiring and call sites

- [ ] 5.1 Wire stages 1-3 behind an `analyze_card`-shaped entry point (sequential: stage 2 only runs after stage 1 succeeds, stage 3 only after stage 2 succeeds)
- [ ] 5.2 Update `Scan`'s call site in `picture_scanner_service.py` to use the new entry point, and verify `picture-service-photo-scan`'s existing tests still pass unmodified (batching/skip/retry/delay/abort behavior is unchanged)
- [ ] 5.3 Update `UploadPhoto`'s call site similarly, and verify its existing tests still pass unmodified

## 6. Sidecar migration

- [ ] 6.1 Extend the `MigrateSidecars` RPC to convert legacy flat records to the three-section shape, following the existing idempotent migration pattern, and add a test covering both a legacy record (gets migrated) and an already-migrated record (left unchanged)
- [ ] 6.2 Verify `picture-service-sidecar-sections`'s scenarios are all covered by tests

## 7. Per-field UI exposure decision (explicitly deferred, not pre-decided)

- [ ] 7.1 Walk today's flat `SidecarRecord` fields one by one with the user and decide, per field, whether it stays surfaced on `NinjagoScanner.Web`'s Review page (mapped from the new `Judged` section) or becomes PictureService-internal only; update `Review.razor` and `PictureServiceClient.cs` accordingly for whichever fields move

## 8. Full verification

- [ ] 8.1 Run `uv run pytest` in `picture_service/` and verify all tests pass
- [ ] 8.2 Run `dotnet test NinjagoScanner.slnx` and verify `NinjagoScanner.Web.Tests`' PictureService fake host (`Fixtures/PictureServiceTestHost.cs`) still matches the (unchanged, per proposal.md's Impact) gRPC contract
