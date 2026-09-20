## Context

See proposal.md for motivation. Stage 3 is deterministic and by spec makes no LLM call; stage 2 is a text-only Gemini call producing a flat scalar map (`DERIVED_ATTRIBUTES_SCHEMA` accepts arbitrary keys). CatalogService sends one name per card (German if the JSON has one, otherwise English), and the JSON has German names only for series 3 and 8NL (~513 of ~3900 names).

## Goals / Non-Goals

**Goals:**
- German (and other non-English) card names find their English-only catalog card.
- Keep stage 3 deterministic and unchanged when no translation is available.

**Non-Goals:**
- Adding German names to the catalog data, or sending several names per card over the CatalogService proto.
- Changing the score weights, thresholds, or the ok/uncertain split.

## Decisions

**Translate in stage 2, not stage 3.** Stage 2 already makes an LLM call over the same text, so one more output key is free, and the model knows official Ninjago names ("Feuer-Drache" -> "Fire Dragon"). *Alternative:* an LLM call in stage 3 - rejected, it breaks the "no LLM" rule and adds latency/cost. *Alternative:* looser fuzzy matching - rejected, German and English names share almost no characters, so lowering the 0.6 threshold mainly adds false matches.

**Score with the better of the two names.** `card_name` and `card_name_en` are each compared with the catalog name and the max is used, so the German names in series 3/8NL still match directly and a wrong translation can never lower a score that the original name earns. The weights and the exact-only-reaches-1.0 rule are untouched.

**Generic key, optional.** `card_name_en` is just another derived attribute (stored in the sidecar's Derived section); no schema, proto or sidecar-format change, and a missing value is a no-op.

**Fix the `card_name` hint.** The stage 2 prompt said "take over card number" for `card_name`; corrected while editing the prompt.

## Risks / Trade-offs

- [Model returns a wrong or hallucinated English name] -> it only adds similarity to cards that are also gated by class/number rules and the minimum score; the original name is still compared.
- [Existing sidecars lack `card_name_en`] -> they gain it on re-analysis; verified cards keep their series/number regardless.
- [Ambiguous short translations match several cards] -> unchanged tie rule: no match when the best score is shared by different cards.
