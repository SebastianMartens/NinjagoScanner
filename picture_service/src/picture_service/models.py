"""Domain models, ported from ScannerModels.cs and SidecarTable.cs's SidecarRecord."""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timezone

""" Analysis status represents if the photo was analyzed by AI, yet."""
class AnalysisStatuses:
    OK = "ok"
    UNCERTAIN = "uncertain" # indicates that the AI is not confident about the analysis
    FAILED = "failed" # usually indicates that the photo does not show a card
    NOT_ANALYZED = "notAnalyzed" # photo not yet send to the AI analysis


class ReviewStatuses:
    UNREVIEWED = "unreviewed"
    VERIFIED = "verified" # a human verified that card number and series are correct (usually via "Review" page)
    INCORRECT = "incorrect" # a human marked card numer/series as incorrect


class Languages:
    GERMAN = "de"
    ENGLISH = "en"
    POLISH = "pl"
    UNKNOWN = "unknown"
    DEFAULT = GERMAN


@dataclass(frozen=True)
class CardAnalysisResult:
    """Produced only by AI Analysis (see gemini_service.py)."""

    photo_id: str
    analysis_status: str
    source_file_name: str
    ai_model: str
    scanned_at_utc: datetime
    card_name: str | None = None
    card_number: str | None = None
    set_name: str | None = None
    rarity: str | None = None
    language: str | None = None
    confidence: float = 0.0
    reasoning_summary: str | None = None
    detected_text: tuple[str, ...] = field(default_factory=tuple)
    error_message: str | None = None
    raw_model_response: str | None = None
    review_status: str = ReviewStatuses.UNREVIEWED
    # True when this failure means Gemini never produced a response it evaluated the photo
    # with (transport-level failure or an exception attempting the call) - as opposed to a
    # content-level failure from a response Gemini did return. Only meaningful when
    # analysis_status is FAILED.
    is_transport_failure: bool = False


@dataclass(frozen=True)
class SidecarRecord:
    """Lenient, fully-optional representation of a sidecar record, used when reading/merging
    arbitrary sidecar contents (as opposed to CardAnalysisResult, which is only produced by
    AI Analysis). Field names match the DynamoDB attribute names exactly (see sidecar_table.py).
    """

    analysis_status: str | None = None
    review_status: str | None = None
    card_name: str | None = None
    card_number: str | None = None
    set_name: str | None = None
    rarity: str | None = None
    language: str | None = None
    confidence: float = 0.0
    reasoning_summary: str | None = None
    detected_text: tuple[str, ...] | None = None
    scanned_at_utc: datetime | None = None
    error_message: str | None = None
    source_file_name: str | None = None
    ai_model: str | None = None
    raw_model_response: str | None = None

    @staticmethod
    def from_analysis_result(result: CardAnalysisResult) -> SidecarRecord:
        return SidecarRecord(
            analysis_status=result.analysis_status,
            review_status=result.review_status,
            card_name=result.card_name,
            card_number=result.card_number,
            set_name=result.set_name,
            rarity=result.rarity,
            language=result.language,
            confidence=result.confidence,
            reasoning_summary=result.reasoning_summary,
            detected_text=tuple(result.detected_text),
            scanned_at_utc=result.scanned_at_utc,
            error_message=result.error_message,
            source_file_name=result.source_file_name,
            ai_model=result.ai_model,
            raw_model_response=result.raw_model_response,
        )


def utc_now() -> datetime:
    return datetime.now(timezone.utc)


@dataclass(frozen=True)
class SeriesInfo:
    """One series from the catalog, as loaded via catalog_client.py."""

    serie: str
    jahr: int = 0
    besonderheiten: tuple[str, ...] = field(default_factory=tuple)
    sondereditionen: tuple[str, ...] = field(default_factory=tuple)
    card_names: tuple[str, ...] = field(default_factory=tuple)
