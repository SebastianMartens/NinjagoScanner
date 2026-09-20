"""Stage 3: deterministic catalog matching (picture-service-catalog-matching).

Stages 1/2 do not reliably yield a series, so this stage does not resolve a series first.
Instead every catalog card, of any series, is scored against the *derived* attributes
(`card_number`, `class`, `card_name`) and the highest-scoring card is the match - its series is
the resolved series. Stage 1's `detected` attributes are deliberately not consulted for now;
the derived card number is the reliable signal.

Scoring, per catalog card (maximum 100):
- card number equals the derived one: 50 - unless both sides carry a class and the classes
  differ. That combination means something is wrong (the number was misread or the class
  was misjudged), so the number earns nothing rather than backing a card of the wrong class.
- class equals the derived one: 20
- card name: up to 30. An exact (normalized) match earns the full 30; a similar name (partially
  detected) earns 30 scaled by its similarity, and nothing below a minimum similarity. Most
  catalog names exist in English only, so a card in another language is also compared by the
  English name stage 2 derived for it (`card_name_en`); the better of the two similarities counts.

A card must reach `_MIN_MATCH_SCORE` and be the single best candidate to be the match.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from difflib import SequenceMatcher

from picture_service.models import (
    CARD_CLASSES,
    AnalysisStatuses,
    AttributeMap,
    CatalogCardInfo,
    CatalogSnapshot,
    VerifiedMatch,
)

_WHITESPACE_RUN = re.compile(r"\s+")

_NUMBER_POINTS = 50.0
_CLASS_POINTS = 20.0
_NAME_POINTS = 30.0

# Below this similarity a card name is treated as unrelated rather than as a partial detection.
_MIN_NAME_SIMILARITY = 0.6
# One normalized name fully containing the other (e.g. "kai" in "kai zx") counts as at least this
# similar, since a partially detected name is usually a fragment of the real one.
_CONTAINED_NAME_SIMILARITY = 0.8
_MIN_CONTAINED_NAME_LENGTH = 3

# A card number alone (class not known on either side) is enough for a match; a class plus an
# exact name is too. A class plus only a partially matching name is not.
_MIN_MATCH_SCORE = 50.0


@dataclass(frozen=True)
class CatalogMatchResult:
    """Stage 3's output - the sidecar's Judged section fields it is responsible for."""

    analysis_status: str
    set_name: str | None = None
    card_number: str | None = None
    card_name: str | None = None
    language: str | None = None
    error_message: str | None = None


def match_catalog(
    derived: AttributeMap,
    catalog: CatalogSnapshot,
    verified: VerifiedMatch | None = None,
) -> CatalogMatchResult:
    """Stage 3 entry point - see picture-service-catalog-matching. Runs no LLM call; consumes
    only stage 2's output and the catalog snapshot already loaded from CatalogService.

    When review status `verified` is given, the human-confirmed series and card number are kept as-is instead
    of being resolved again; only the remaining Judged fields are recomputed."""
    card_number_guess = _read_card_number(derived.get("card_number"))
    card_name_guess = _read_string(derived.get("card_name"))
    card_name_english_guess = _read_string(derived.get("card_name_en"))
    language = _read_string(derived.get("language"))

    if verified is not None:
        return CatalogMatchResult(
            analysis_status=AnalysisStatuses.OK,
            set_name=verified.set_name,
            card_number=verified.card_number,
            card_name=_find_catalog_card_name(verified, catalog) or card_name_guess,
            language=language,
        )

    derived_class = derived.get("class")
    derived_class = derived_class if isinstance(derived_class, str) and derived_class in CARD_CLASSES else None

    winner = resolve_card(
        card_number_guess, card_name_guess, derived_class, list(catalog.cards), card_name_english_guess
    )

    if winner is None:
        return CatalogMatchResult(
            analysis_status=AnalysisStatuses.FAILED,
            card_number=card_number_guess,
            card_name=card_name_guess,
            language=language,
            error_message="Keine passende Karte gefunden.",
        )

    # The ok/uncertain split for a fully-resolved match is left open by design.md's Open
    # Questions ("What exactly distinguishes ok from uncertain..."); always OK for now.
    return CatalogMatchResult(
        analysis_status=AnalysisStatuses.OK,
        set_name=winner.series_name,
        card_number=winner.card_number,
        card_name=winner.card_name,
        language=language,
    )


def resolve_card(
    card_number_guess: str | None,
    card_name_guess: str | None,
    derived_class: str | None,
    catalog_cards: list[CatalogCardInfo],
    card_name_english_guess: str | None = None,
) -> CatalogCardInfo | None:
    """The single best-scoring catalog card across all series, or None when no card reaches the
    minimum score or the best score is shared by cards that are not the same series + number."""
    best_score = 0.0
    best_cards: list[CatalogCardInfo] = []

    for card in catalog_cards:
        score = _score_card(card, card_number_guess, card_name_guess, derived_class, card_name_english_guess)
        if score < _MIN_MATCH_SCORE:
            continue
        if score > best_score:
            best_score = score
            best_cards = [card]
        elif score == best_score:
            best_cards.append(card)

    if not best_cards:
        return None

    # The same series + number can be listed under more than one category; that is still one card.
    identities = {
        (_normalize_lookup_text(card.series_name), _normalize_card_number(card.card_number)) for card in best_cards
    }
    return best_cards[0] if len(identities) == 1 else None


def _score_card(
    card: CatalogCardInfo,
    card_number_guess: str | None,
    card_name_guess: str | None,
    derived_class: str | None,
    card_name_english_guess: str | None = None,
) -> float:
    class_known = derived_class is not None and card.card_class is not None
    class_matches = class_known and card.card_class == derived_class

    score = 0.0

    if card_number_guess is not None and _normalize_card_number(card.card_number) == card_number_guess:
        if not class_known or class_matches:
            score += _NUMBER_POINTS

    if class_matches:
        score += _CLASS_POINTS

    name_guesses = [guess for guess in (card_name_guess, card_name_english_guess) if guess]
    if name_guesses:
        similarity = max(_name_similarity(guess, card.card_name) for guess in name_guesses)
        if similarity >= _MIN_NAME_SIMILARITY:
            score += _NAME_POINTS * similarity

    return score


def _name_similarity(name_guess: str, catalog_name: str) -> float:
    """1.0 for an exact match after normalization, otherwise how alike the two names are."""
    guess = _normalize_lookup_text(name_guess)
    catalog = _normalize_lookup_text(catalog_name)
    if not guess or not catalog:
        return 0.0
    if guess == catalog:
        return 1.0

    similarity = SequenceMatcher(None, guess, catalog).ratio()

    shorter, longer = sorted((guess, catalog), key=len)
    if len(shorter) >= _MIN_CONTAINED_NAME_LENGTH and shorter in longer:
        similarity = max(similarity, _CONTAINED_NAME_SIMILARITY)

    # Only an exact match may reach 1.0, so it always outranks a merely similar name.
    return min(similarity, 0.99)


def _find_catalog_card_name(verified: VerifiedMatch, catalog: CatalogSnapshot) -> str | None:
    normalized_series = _normalize_lookup_text(verified.set_name)
    normalized_number = _normalize_card_number(verified.card_number)
    card = next(
        (
            card
            for card in catalog.cards
            if _normalize_lookup_text(card.series_name) == normalized_series
            and _normalize_card_number(card.card_number) == normalized_number
        ),
        None,
    )
    return card.card_name if card else None


def _read_string(value: object) -> str | None:
    return value.strip() if isinstance(value, str) and value.strip() else None


def _read_card_number(value: object) -> str | None:
    """A derived card number arrives as text or a number ("0 if not visible" in stage 1)."""
    if isinstance(value, bool) or value is None:
        return None
    number = _normalize_card_number(str(value))
    return None if number in ("", "0") else number


def _normalize_card_number(value: str) -> str:
    """"007", "7" and "7.0" are all card 7; anything non-numeric is compared as trimmed text."""
    text = value.strip()
    try:
        number = float(text)
    except ValueError:
        return text
    return str(int(number)) if number.is_integer() else text


def _normalize_lookup_text(value: str) -> str:
    filtered = "".join(
        character for character in value.strip().lower() if character.isalnum() or character.isspace()
    )
    return _WHITESPACE_RUN.sub(" ", filtered).strip()
