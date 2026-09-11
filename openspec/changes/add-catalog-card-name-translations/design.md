## Context

`CatalogRepository.ExtractSeriesCards`/`EnumerateCardEntries` (`NinjagoScanner.CatalogService/Catalog/CatalogRepository.cs`) walks each `cardInfos/series_*.json` file looking for `{"Karten-Nr.": ..., "Name": "..."}` pairs nested under arbitrary category keys, and builds a flat `CatalogCardItem` per card with a single `CardName` string. All 16 series files use this same JSON shape today, but the language of the `Name` values differs by file depending on which source material each series was originally transcribed from — some are German, most are English. There is currently no field recording which language a name is in, and no way to store more than one language for the same card. See proposal.md - Why.

## Goals / Non-Goals

**Goals:**
- Define a per-language JSON shape for `Name` that can hold German and English today, and additional languages later without another schema change.
- Resolve that map down to the single `card_name` the rest of the system already expects, with German preferred and English as fallback.
- Migrate all 16 existing series files to the new shape without altering any existing name value's text — only wrapping it under the language it already is.

**Non-Goals:**
- No change to `Protos/catalog.proto`, `CatalogContracts.CatalogCardItem`, or any PictureService/Web code — they keep receiving a single resolved `CardName`.
- No user-facing or per-request language preference. The de→en priority is fixed in code, matching today's implicit "German-first" behavior for the files that already had German names.
- No translation of series-level fields (`Thema`, `Besonderheiten`, `Sondereditionen`, `Logo`) or category names — card name only.
- No AI-assisted translation lookup or new tooling — the migration is a manual, one-time edit of the 16 JSON files, done by hand/by AI, not a program.

## Decisions

**Inline per-card map, not a sibling block or parallel files.** `"Name": {"de": "Kai", "en": "Kai"}` keeps each card's data in one place, which matches how the file is already organized (one object per card, nested under category arrays) and is the easiest shape for the manual per-file wrapping this change requires. A sibling `"Uebersetzungen"` block or parallel per-language files were considered but rejected: they'd split one card's data across two locations in the same file (or across files) for no benefit at this stage, since there's no tooling yet that would prefer partial/append-only translation files over editing the card in place.

**Resolution stays inside CatalogService; contract is unchanged.** `CatalogRepository` resolves the map to a single string before building `CatalogCardItem`, the same point where it already extracts `Karten-Nr.`/`Name` today. This is what keeps the change contained to one project: PictureService's series-matching prompt (`SeriesCatalogService.BuildPrompt`) and every Web view-model keep working unmodified, because `CardName` never stops being a plain string on the wire.

**Fixed de→en priority, not a configurable/requested language.** The proposal explicitly scopes this to preparing the data; the actual "pick a language" feature (a user preference, later threaded through the gRPC contract) is future work. Hardcoding the order now means when that feature does arrive, only the resolution call site changes (from "always de→en" to "requested language→de→en" or similar) — the JSON shape and parsing don't need to change again.

**Migration is a mechanical, per-file wrap driven by an explicit source-language list, not auto-detection.** Which language each file's existing names are in was provided directly (see tasks.md) rather than inferred, since language-detecting arbitrary short character names (e.g. "Kai", "Cole") reliably by machine isn't practical and isn't worth building for a one-time migration.

Per-file source language:

| File | Existing names are | Becomes |
|---|---|---|
| `series_3.json` | German | `{"de": "<existing>"}` |
| `series_8NL.json` | German | `{"de": "<existing>"}` |
| all other `series_*.json` (1, 2, 4, 5, 5NL, 6, 6NL, 7, 7NL, 8, 9, 9NL, 10, 11) | English | `{"en": "<existing>"}` |

## Risks / Trade-offs

- **[Risk]** Manual per-file wrapping could accidentally alter a name's text instead of just restructuring it → **[Mitigation]** The migration only changes JSON structure (`"Name": "X"` → `"Name": {"<lang>": "X"}`); the string `X` itself must be copied verbatim, so a diff review should show only structural changes, never a changed name value.
- **[Risk]** The existing per-card dedup key in `ExtractSeriesCards` (`seriesName|category|number|cardName`) is built from the resolved `cardName` — unchanged by this design, but worth calling out since a future change that exposes multiple languages downstream would need to revisit whether dedup should key on something language-independent instead.
- **[Risk]** Two languages today (`de`/`en`) will eventually need to grow to match Sidecar's existing language set (`de`/`en`/`pl`/`unknown`) — no action needed now since the map shape already accepts arbitrary keys; only the fixed resolution order would need revisiting.

## Migration Plan

This is a single, atomic change: the 16 JSON files and `CatalogRepository`'s parsing logic are edited and deployed together, since `CatalogRepository` has no backward-compat path for the old flat-string shape (none is needed — every file is migrated in the same change). No live/runtime data migration is involved; rollback is a plain revert of the commit.
