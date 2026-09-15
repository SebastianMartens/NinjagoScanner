"""Prompt building and series-name matching, ported from SeriesCatalogService.cs."""

from __future__ import annotations

import re
from dataclasses import dataclass

from picture_service.models import SeriesInfo

_WHITESPACE_RUN = re.compile(r"\s+")


@dataclass(frozen=True)
class GeminiCardPayload:
    """Mirrors the fields SeriesCatalogService's matching logic reads off a Gemini result."""

    status: str | None = None
    card_name: str | None = None
    card_number: str | None = None
    set_name: str | None = None
    rarity: str | None = None
    language: str | None = None
    confidence: float = 0.0
    reasoning_summary: str | None = None
    detected_text: tuple[str, ...] | None = None


def build_prompt(series_catalog: list[SeriesInfo]) -> str:
    if not series_catalog:
        return "- Serie 1: kein Symbol"

    lines: list[str] = []
    for series in series_catalog:
        symbol_hint = _extract_series_symbol_hint(series)
        line = f"- {series.serie}: {symbol_hint}"
        if series.jahr > 0:
            line += f" ({series.jahr})"
        if series.card_names:
            line += " | Bekannte Kartennamen: " + ", ".join(series.card_names)
        lines.append(line)

    return "\n".join(lines)


def resolve_set_name(payload: GeminiCardPayload, series_catalog: list[SeriesInfo]) -> str | None:
    if not series_catalog:
        return payload.set_name.strip() if payload.set_name else None

    exact_match = _find_series_by_name(series_catalog, payload.set_name)
    if exact_match is not None:
        return exact_match.serie

    inferred_match = _find_series_by_evidence(
        series_catalog,
        payload.set_name,
        payload.card_name,
        payload.reasoning_summary,
        payload.detected_text,
    )
    return inferred_match.serie if inferred_match is not None else None


def _find_series_by_name(series_catalog: list[SeriesInfo], candidate: str | None) -> SeriesInfo | None:
    if not candidate or not candidate.strip():
        return None

    normalized_candidate = _normalize_lookup_text(candidate)
    for series in series_catalog:
        if _normalize_lookup_text(series.serie) == normalized_candidate:
            return series
    return None


def _find_series_by_evidence(
    series_catalog: list[SeriesInfo],
    set_name: str | None,
    card_name: str | None,
    reasoning_summary: str | None,
    detected_text: tuple[str, ...] | None,
) -> SeriesInfo | None:
    evidence: list[str] = []
    _add_evidence(evidence, set_name)
    _add_evidence(evidence, card_name)
    _add_evidence(evidence, reasoning_summary)

    if detected_text:
        for text in detected_text:
            _add_evidence(evidence, text)

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
