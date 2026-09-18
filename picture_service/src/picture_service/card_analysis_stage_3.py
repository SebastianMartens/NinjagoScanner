"""Stage 3: deterministic catalog matching (picture-service-catalog-matching), extending the
series evidence-matching pattern (picture-service-series-name-matching, originally ported from
SeriesCatalogService.cs) to also resolve a card number within the matched series.

Series/card-number evidence comes from stage 1/2's generic attribute maps (Detected/Derived) -
every scalar value across both maps is a candidate piece of evidence text; the exact attribute
key vocabulary is deliberately not pinned (see design.md's Non-Goals), so this reads values
generically rather than off named fields.
"""

from __future__ import annotations

import re
from dataclasses import dataclass

from picture_service.models import (
    CARD_CLASSES,
    AnalysisStatuses,
    AttributeMap,
    CatalogCardInfo,
    CatalogSnapshot,
    SeriesInfo,
    VerifiedMatch,
)

_WHITESPACE_RUN = re.compile(r"\s+")

# Convention key names stage 1/2 attributes may use for a raw guess - see this module's
# docstring on why these aren't a required schema, just a read convention.
_SERIES_NAME_KEYS = ("series_name", "set_name")
_CARD_NUMBER_KEYS = ("card_number",)
_CARD_NAME_KEYS = ("card_name",)
_RARITY_KEYS = ("rarity",)
_LANGUAGE_KEYS = ("language",)


@dataclass(frozen=True)
class CatalogMatchResult:
    """Stage 3's output - the sidecar's Judged section fields it is responsible for."""

    analysis_status: str
    set_name: str | None = None
    card_number: str | None = None
    card_name: str | None = None
    rarity: str | None = None
    language: str | None = None
    error_message: str | None = None


def match_catalog(
    detected: AttributeMap,
    derived: AttributeMap,
    catalog: CatalogSnapshot,
    verified: VerifiedMatch | None = None,
) -> CatalogMatchResult:
    """Stage 3 entry point - see picture-service-catalog-matching. Runs no LLM call; consumes
    only stage 1/2's output and the catalog snapshot already loaded from CatalogService.

    When `verified` is given, the human-confirmed series and card number are kept as-is instead
    of being resolved again; only the remaining Judged fields are recomputed."""
    evidence = _collect_evidence(detected, derived)
    series_name_guess = _first_string(detected, derived, _SERIES_NAME_KEYS)
    card_name_guess = _first_string(detected, derived, _CARD_NAME_KEYS)
    rarity = _first_string(detected, derived, _RARITY_KEYS)
    language = _first_string(detected, derived, _LANGUAGE_KEYS)

    if verified is not None:
        return CatalogMatchResult(
            analysis_status=AnalysisStatuses.OK,
            set_name=verified.set_name,
            card_number=verified.card_number,
            card_name=_find_catalog_card_name(verified, catalog) or card_name_guess,
            rarity=rarity,
            language=language,
        )

    resolved_series = resolve_set_name(series_name_guess, evidence, list(catalog.series))

    if resolved_series is None:
        return CatalogMatchResult(
            analysis_status=AnalysisStatuses.FAILED,
            set_name=series_name_guess.strip() if series_name_guess else None,
            card_name=card_name_guess,
            rarity=rarity,
            language=language,
            error_message="Keine passende Serie gefunden.",
        )

    card_number_guess = _first_string(detected, derived, _CARD_NUMBER_KEYS)
    derived_class = derived.get("class")
    derived_class = derived_class if isinstance(derived_class, str) and derived_class in CARD_CLASSES else None

    resolved_card_number, resolved_card_name = resolve_card_number(
        resolved_series, card_number_guess, evidence, derived_class, list(catalog.cards)
    )

    card_name = resolved_card_name or card_name_guess

    if resolved_card_number is None:
        return CatalogMatchResult(
            analysis_status=AnalysisStatuses.FAILED,
            set_name=resolved_series.serie,
            card_number=card_number_guess.strip() if card_number_guess else None,
            card_name=card_name,
            rarity=rarity,
            language=language,
            error_message="Keine passende Kartennummer gefunden.",
        )

    # The ok/uncertain split for a fully-resolved match is left open by design.md's Open
    # Questions ("What exactly distinguishes ok from uncertain..."); always OK for now.
    return CatalogMatchResult(
        analysis_status=AnalysisStatuses.OK,
        set_name=resolved_series.serie,
        card_number=resolved_card_number,
        card_name=card_name,
        rarity=rarity,
        language=language,
    )


def _find_catalog_card_name(verified: VerifiedMatch, catalog: CatalogSnapshot) -> str | None:
    normalized_series = _normalize_lookup_text(verified.set_name)
    card = next(
        (
            card
            for card in catalog.cards
            if _normalize_lookup_text(card.series_name) == normalized_series
            and card.card_number.strip() == verified.card_number.strip()
        ),
        None,
    )
    return card.card_name if card else None


def _first_string(detected: AttributeMap, derived: AttributeMap, keys: tuple[str, ...]) -> str | None:
    for source in (derived, detected):
        for key in keys:
            value = source.get(key)
            if isinstance(value, str) and value.strip():
                return value
    return None


def _collect_evidence(detected: AttributeMap, derived: AttributeMap) -> list[str]:
    """Every scalar value across both maps is a candidate piece of evidence text (whichever of
    them carry series/card-relevant signal) - see picture-service-catalog-matching's "Series is
    resolved using the existing evidence-matching rules"."""
    evidence: list[str] = []
    for value in (*detected.values(), *derived.values()):
        _add_evidence(evidence, str(value) if value is not None else None)
    return evidence


# --- Series resolution (picture-service-series-name-matching) ---


def resolve_set_name(series_name_guess: str | None, evidence: list[str], series_catalog: list[SeriesInfo]) -> SeriesInfo | None:
    if not series_catalog:
        return None

    exact_match = _find_series_by_name(series_catalog, series_name_guess)
    if exact_match is not None:
        return exact_match

    return _find_series_by_evidence(series_catalog, evidence)


def _find_series_by_name(series_catalog: list[SeriesInfo], candidate: str | None) -> SeriesInfo | None:
    if not candidate or not candidate.strip():
        return None

    normalized_candidate = _normalize_lookup_text(candidate)
    for series in series_catalog:
        if _normalize_lookup_text(series.serie) == normalized_candidate:
            return series
    return None


def _find_series_by_evidence(series_catalog: list[SeriesInfo], evidence: list[str]) -> SeriesInfo | None:
    if not evidence:
        return None

    best_match: SeriesInfo | None = None
    best_score = 0
    tie = False

    for series in series_catalog:
        score = _score_series_match(series, evidence)
        if score <= 0:
            continue

        if score > best_score:
            best_score = score
            best_match = series
            tie = False
        elif score == best_score:
            tie = True

    return None if tie else best_match


def _add_evidence(evidence: list[str], text: str | None) -> None:
    if not text or not text.strip():
        return

    normalized_text = _normalize_lookup_text(text)
    if normalized_text:
        evidence.append(normalized_text)


def _score_series_match(series: SeriesInfo, evidence: list[str]) -> int:
    score = 0
    normalized_name = _normalize_lookup_text(series.serie)
    symbol_hint = _normalize_lookup_text(_extract_series_symbol_hint(series))
    year = str(series.jahr) if series.jahr > 0 else None
    normalized_card_names = [
        name for name in (_normalize_lookup_text(n) for n in series.card_names) if name
    ]

    for text in evidence:
        if normalized_name in text:
            score = max(score, 100)

        if symbol_hint and symbol_hint in text:
            score = max(score, 70)

        if year is not None and year in text:
            score = max(score, 20)

        if any(card_name in text for card_name in normalized_card_names):
            score = max(score, 35)

        if series.serie.lower() == "serie 1" and (
            "kein symbol" in text
            or "ohne symbol" in text
            or "kein logo" in text
            or "ohne logo" in text
        ):
            score = max(score, 90)

    return score


def _extract_series_symbol_hint(series: SeriesInfo) -> str:
    logo_entry = next(
        (entry for entry in series.besonderheiten if entry.lower().startswith("logo:")),
        None,
    )

    if logo_entry and logo_entry.strip():
        return logo_entry[len("Logo:") :].strip()

    if series.serie.lower() == "serie 1":
        return "kein Symbol"

    return "Symbol siehe Serienbeschreibung"


def _normalize_lookup_text(value: str) -> str:
    filtered = "".join(
        character for character in value.strip().lower() if character.isalnum() or character.isspace()
    )
    return _WHITESPACE_RUN.sub(" ", filtered).strip()


# --- Card number resolution (picture-service-catalog-matching) ---


def resolve_card_number(
    resolved_series: SeriesInfo,
    card_number_guess: str | None,
    evidence: list[str],
    derived_class: str | None,
    catalog_cards: list[CatalogCardInfo],
) -> tuple[str | None, str | None]:
    """Returns `(card_number, card_name)` - the same category of rules as series resolution
    (exact match wins, otherwise evidence-scoring with a unique winner, otherwise no match),
    additionally narrowing by `derived_class` when it disambiguates a scoring tie (see
    picture-service-catalog-matching's "Card number is resolved within the matched series")."""
    normalized_series = _normalize_lookup_text(resolved_series.serie)
    series_cards = [card for card in catalog_cards if _normalize_lookup_text(card.series_name) == normalized_series]
    if not series_cards:
        return None, None

    if card_number_guess and card_number_guess.strip():
        exact = next(
            (card for card in series_cards if card.card_number.strip() == card_number_guess.strip()), None
        )
        if exact is not None:
            return exact.card_number, exact.card_name

    scored = [(_score_card_match(card, evidence), card) for card in series_cards]
    scored = [(score, card) for score, card in scored if score > 0]
    if not scored:
        return None, None

    best_score = max(score for score, _ in scored)
    candidates = [card for score, card in scored if score == best_score]

    if len(candidates) > 1 and derived_class is not None:
        narrowed = [card for card in candidates if card.card_class == derived_class]
        if len(narrowed) == 1:
            candidates = narrowed

    if len(candidates) != 1:
        return None, None

    winner = candidates[0]
    return winner.card_number, winner.card_name


def _score_card_match(card: CatalogCardInfo, evidence: list[str]) -> int:
    """A card number is short and easily a substring of unrelated evidence (e.g. "Serie 2"
    contains "2") - unlike series/card-name text, a number attribute only counts as evidence
    when it IS the whole evidence value (a raw `card_number`-shaped attribute is realistically
    just "2", not a sentence containing it), not merely contained within a longer string."""
    score = 0
    normalized_number = _normalize_lookup_text(card.card_number)
    normalized_name = _normalize_lookup_text(card.card_name)

    for text in evidence:
        if normalized_number and text == normalized_number:
            score = max(score, 100)

        if normalized_name and normalized_name in text:
            score = max(score, 60)

    return score
