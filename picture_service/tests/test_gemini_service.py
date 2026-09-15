import math

from langchain_core.exceptions import ModelAPIError, ModelInvalidRequestError, ModelNotFoundError, ModelRateLimitError

from picture_service.config import ScannerConfig
from picture_service.gemini_service import analyze_card, build_messages
from picture_service.models import AnalysisStatuses, Languages, SeriesInfo

CATALOG = [SeriesInfo(serie="Serie 1", jahr=2016), SeriesInfo(serie="Serie 2", jahr=2017)]


def make_config(**overrides) -> ScannerConfig:
    return ScannerConfig.load_for_scan(
        api_key="key",
        model="gemini-test",
        retry_delay_ms=overrides.pop("retry_delay_ms", 1),
        max_attempts=overrides.pop("max_attempts", 3),
        **overrides,
    )


class FakeModel:
    """Fake StructuredModel: each entry in `responses` is either a dict (a successful
    include_raw=True result) or an exception instance to raise."""

    def __init__(self, responses: list):
        self._responses = list(responses)
        self.call_count = 0
        self.last_messages = None

    async def ainvoke(self, messages):
        self.last_messages = messages
        self.call_count += 1
        response = self._responses.pop(0)
        if isinstance(response, Exception):
            raise response
        return response


def ok_response(**overrides) -> dict:
    parsed = {
        "status": "ok",
        "cardName": "Kai",
        "cardNumber": "1",
        "setName": "Serie 1",
        "rarity": "common",
        "language": "de",
        "confidence": 0.9,
        "reasoningSummary": "clear",
        "detectedText": ["Kai", "1"],
    }
    parsed.update(overrides)
    return {"raw": _RawMessage(str(parsed)), "parsed": parsed, "parsing_error": None}


class _RawMessage:
    def __init__(self, content):
        self.content = content


# --- Request building ---


def test_request_includes_image_and_series_catalog_prompt():
    config = make_config()
    messages = build_messages(config, CATALOG, "card.jpg", b"fake-bytes")

    content = messages[0]["content"]
    text_block = next(b for b in content if b["type"] == "text")
    image_block = next(b for b in content if b["type"] == "image_url")

    assert "Serie 1" in text_block["text"]
    assert "Serie 2" in text_block["text"]
    assert image_block["image_url"].startswith("data:image/jpeg;base64,")


def test_mime_type_matches_extension():
    config = make_config()
    messages = build_messages(config, CATALOG, "card.png", b"fake-bytes")

    image_block = next(b for b in messages[0]["content"] if b["type"] == "image_url")
    assert image_block["image_url"].startswith("data:image/png;base64,")


# --- Retry policy ---


async def test_rate_limited_then_succeeds_retries_and_uses_final_response():
    model = FakeModel([ModelRateLimitError("rate limited"), ok_response()])
    config = make_config(max_attempts=3)

    result = await analyze_card(model, config, CATALOG, "p1", "card.jpg", b"data")

    assert model.call_count == 2
    assert result.analysis_status == AnalysisStatuses.OK


async def test_server_error_exhausts_all_attempts():
    model = FakeModel([ModelAPIError("boom")] * 3)
    config = make_config(max_attempts=3)

    result = await analyze_card(model, config, CATALOG, "p1", "card.jpg", b"data")

    assert model.call_count == 3
    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.is_transport_failure is True


async def test_non_retryable_error_fails_immediately_without_retry():
    model = FakeModel([ModelInvalidRequestError("bad request")])
    config = make_config(max_attempts=3)

    result = await analyze_card(model, config, CATALOG, "p1", "card.jpg", b"data")

    assert model.call_count == 1
    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.is_transport_failure is True


# --- Transport vs content failure classification ---


async def test_retries_exhausted_marks_transport_failure():
    model = FakeModel([ModelRateLimitError("x")] * 3)
    config = make_config(max_attempts=3)

    result = await analyze_card(model, config, CATALOG, "p1", "card.jpg", b"data")

    assert result.is_transport_failure is True


async def test_immediate_non_retryable_marks_transport_failure():
    model = FakeModel([ModelNotFoundError("not found")])
    config = make_config()

    result = await analyze_card(model, config, CATALOG, "p1", "card.jpg", b"data")

    assert result.is_transport_failure is True


async def test_malformed_output_marks_content_failure_not_transport():
    model = FakeModel([{"raw": _RawMessage("not json"), "parsed": None, "parsing_error": "bad json"}])
    config = make_config()

    result = await analyze_card(model, config, CATALOG, "p1", "card.jpg", b"data")

    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.is_transport_failure is False


async def test_model_reports_failed_status_marks_content_failure_not_transport():
    model = FakeModel([ok_response(status="failed")])
    config = make_config()

    result = await analyze_card(model, config, CATALOG, "p1", "card.jpg", b"data")

    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.is_transport_failure is False


async def test_series_match_escalation_marks_content_failure_not_transport():
    model = FakeModel([ok_response(setName="Unknown Series")])
    config = make_config()

    result = await analyze_card(model, config, CATALOG, "p1", "card.jpg", b"data")

    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.is_transport_failure is False


# --- Malformed/empty output ---


async def test_empty_candidate_text_is_failed_with_message():
    model = FakeModel([{"raw": _RawMessage(""), "parsed": None, "parsing_error": None}])
    config = make_config()

    result = await analyze_card(model, config, CATALOG, "p1", "card.jpg", b"data")

    assert result.analysis_status == AnalysisStatuses.FAILED
    assert "kein JSON" in result.error_message


async def test_invalid_json_preserves_raw_text_for_diagnostics():
    model = FakeModel([{"raw": _RawMessage("{not valid"), "parsed": None, "parsing_error": "oops"}])
    config = make_config()

    result = await analyze_card(model, config, CATALOG, "p1", "card.jpg", b"data")

    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.raw_model_response == "{not valid"


# --- Confidence clamping and status normalization ---


async def test_model_failed_status_wins_regardless_of_confidence():
    model = FakeModel([ok_response(status="failed", confidence=0.99)])
    result = await analyze_card(model, make_config(), CATALOG, "p1", "card.jpg", b"data")
    assert result.analysis_status == AnalysisStatuses.FAILED


async def test_high_confidence_ok_stays_ok():
    model = FakeModel([ok_response(status="ok", confidence=0.65)])
    result = await analyze_card(model, make_config(), CATALOG, "p1", "card.jpg", b"data")
    assert result.analysis_status == AnalysisStatuses.OK


async def test_low_confidence_forces_uncertain():
    model = FakeModel([ok_response(status="ok", confidence=0.3)])
    result = await analyze_card(model, make_config(), CATALOG, "p1", "card.jpg", b"data")
    assert result.analysis_status == AnalysisStatuses.UNCERTAIN


async def test_out_of_range_confidence_is_clamped():
    for raw, expected in [(-1.0, 0.0), (2.0, 1.0), (math.nan, 0.0), (math.inf, 0.0)]:
        model = FakeModel([ok_response(confidence=raw)])
        result = await analyze_card(model, make_config(), CATALOG, "p1", "card.jpg", b"data")
        assert result.confidence == expected


# --- Set name discard/escalation ---


async def test_model_reported_failure_clears_set_name():
    model = FakeModel([ok_response(status="failed", setName="Serie 1")])
    result = await analyze_card(model, make_config(), CATALOG, "p1", "card.jpg", b"data")
    assert result.set_name is None


async def test_unresolved_series_match_escalates_and_preserves_raw_guess():
    model = FakeModel([ok_response(status="ok", confidence=0.9, setName="Mystery Set")])
    result = await analyze_card(model, make_config(), CATALOG, "p1", "card.jpg", b"data")
    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.set_name == "Mystery Set"


async def test_uncertain_with_unresolved_series_also_escalates():
    model = FakeModel([ok_response(status="uncertain", confidence=0.3, setName="Mystery Set")])
    result = await analyze_card(model, make_config(), CATALOG, "p1", "card.jpg", b"data")
    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.set_name == "Mystery Set"


async def test_confident_series_match_is_not_escalated():
    model = FakeModel([ok_response(status="ok", confidence=0.9, setName="serie 1")])
    result = await analyze_card(model, make_config(), CATALOG, "p1", "card.jpg", b"data")
    assert result.analysis_status == AnalysisStatuses.OK
    assert result.set_name == "Serie 1"


# --- Language normalization ---


async def test_language_de_kept():
    model = FakeModel([ok_response(language="de")])
    result = await analyze_card(model, make_config(), CATALOG, "p1", "card.jpg", b"data")
    assert result.language == Languages.GERMAN


async def test_language_case_insensitive():
    model = FakeModel([ok_response(language="EN")])
    result = await analyze_card(model, make_config(), CATALOG, "p1", "card.jpg", b"data")
    assert result.language == Languages.ENGLISH


async def test_language_pl_kept():
    model = FakeModel([ok_response(language="pl")])
    result = await analyze_card(model, make_config(), CATALOG, "p1", "card.jpg", b"data")
    assert result.language == Languages.POLISH


async def test_language_outside_closed_set_is_unknown():
    model = FakeModel([ok_response(language="fr")])
    result = await analyze_card(model, make_config(), CATALOG, "p1", "card.jpg", b"data")
    assert result.language == Languages.UNKNOWN


async def test_language_missing_is_unknown():
    model = FakeModel([ok_response(language=None)])
    result = await analyze_card(model, make_config(), CATALOG, "p1", "card.jpg", b"data")
    assert result.language == Languages.UNKNOWN
