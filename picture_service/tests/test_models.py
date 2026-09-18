import dataclasses
from datetime import datetime, timezone

from picture_service.models import (
    AnalysisStatuses,
    CardAnalysisResult,
    Languages,
    ReviewStatuses,
    SidecarRecord,
)

EXPECTED_ANALYSIS_RESULT_FIELDS = {
    "photo_id",
    "analysis_status",
    "source_file_name",
    "card_name",
    "card_number",
    "set_name",
    "rarity",
    "language",
    "confidence",
    "reasoning_summary",
    "detected_text",
    "ai_model",
    "scanned_at_utc",
    "error_message",
    "raw_model_response",
    "review_status",
    "is_transport_failure",
    "detected",
    "derived",
}

EXPECTED_SIDECAR_RECORD_FIELDS = {
    "analysis_status",
    "review_status",
    "card_name",
    "card_number",
    "set_name",
    "rarity",
    "language",
    "confidence",
    "reasoning_summary",
    "detected_text",
    "scanned_at_utc",
    "error_message",
    "source_file_name",
    "ai_model",
    "raw_model_response",
    "detected",
    "derived",
}


def test_card_analysis_result_fields_match_csharp_source():
    fields = {f.name for f in dataclasses.fields(CardAnalysisResult)}
    assert fields == EXPECTED_ANALYSIS_RESULT_FIELDS


def test_sidecar_record_fields_match_csharp_source():
    fields = {f.name for f in dataclasses.fields(SidecarRecord)}
    assert fields == EXPECTED_SIDECAR_RECORD_FIELDS


def test_card_analysis_result_defaults():
    result = CardAnalysisResult(
        photo_id="p1",
        analysis_status=AnalysisStatuses.OK,
        source_file_name="a.jpg",
        ai_model="gemini-x",
        scanned_at_utc=datetime.now(timezone.utc),
    )
    assert result.detected_text == ()
    assert result.review_status == ReviewStatuses.UNREVIEWED
    assert result.is_transport_failure is False
    assert result.detected == {}
    assert result.derived == {}


def test_sidecar_record_defaults_detected_derived_to_none():
    assert SidecarRecord().detected is None
    assert SidecarRecord().derived is None


def test_sidecar_record_from_analysis_result_round_trips_fields():
    result = CardAnalysisResult(
        photo_id="p1",
        analysis_status=AnalysisStatuses.OK,
        source_file_name="a.jpg",
        ai_model="gemini-x",
        scanned_at_utc=datetime.now(timezone.utc),
        card_name="Kai",
        card_number="1",
        set_name="Serie 1",
        rarity="common",
        language=Languages.GERMAN,
        confidence=0.9,
        reasoning_summary="looks right",
        detected_text=("Kai", "1"),
        error_message=None,
        raw_model_response="{}",
        review_status=ReviewStatuses.VERIFIED,
        detected={"number_top_left": "1"},
        derived={"class": "character"},
    )

    record = SidecarRecord.from_analysis_result(result)

    assert record.analysis_status == result.analysis_status
    assert record.review_status == result.review_status
    assert record.card_name == result.card_name
    assert record.detected_text == result.detected_text
    assert record.scanned_at_utc == result.scanned_at_utc
    assert record.detected == {"number_top_left": "1"}
    assert record.derived == {"class": "character"}
