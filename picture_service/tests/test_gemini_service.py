import pytest
from langchain_core.exceptions import ModelAPIError, ModelInvalidRequestError, ModelNotFoundError, ModelRateLimitError

from picture_service.config import ScannerConfig
from picture_service.gemini_service import (
    ATTRIBUTE_DETECTION_SCHEMA,
    analyze_card,
    build_attribute_detection_messages,
    build_chat_model,
    build_derived_attributes_messages,
    compute_derived_attributes,
    detect_attributes,
)
from picture_service.models import AnalysisStatuses, CatalogCardInfo, CatalogSnapshot, SeriesInfo, VerifiedMatch


def make_config(**overrides) -> ScannerConfig:
    return ScannerConfig.load_for_scan(
        api_key="key",
        model="gemini-test",
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


def ok_raw_result(parsed: dict) -> dict:
    return {"raw": _RawMessage(str(parsed)), "parsed": parsed, "parsing_error": None}


class _RawMessage:
    def __init__(self, content):
        self.content = content


# --- Stage 1: request building ---


def test_attribute_detection_request_includes_only_the_photo_no_catalog():
    messages = build_attribute_detection_messages("card.jpg", b"fake-bytes")

    content = messages[0]["content"]
    text_block = next(b for b in content if b["type"] == "text")
    image_block = next(b for b in content if b["type"] == "image_url")

    assert "Serie 1" not in text_block["text"]
    assert image_block["image_url"].startswith("data:image/jpeg;base64,")


def test_attribute_detection_mime_type_matches_extension():
    messages = build_attribute_detection_messages("card.png", b"fake-bytes")

    image_block = next(b for b in messages[0]["content"] if b["type"] == "image_url")
    assert image_block["image_url"].startswith("data:image/png;base64,")


# --- Stage 1: successful detection ---


async def test_attribute_detection_produces_flat_key_value_map():
    model = FakeModel([ok_raw_result({"number_top_left": "1", "color_area": "red"})])
    result = await detect_attributes(model, make_config(), "card.jpg", b"data")

    assert result.success is True
    assert result.attributes == {"number_top_left": "1", "color_area": "red"}


async def test_attribute_detection_drops_nested_values():
    model = FakeModel([ok_raw_result({"number_top_left": "1", "nested": {"a": 1}, "list_value": [1, 2]})])
    result = await detect_attributes(model, make_config(), "card.jpg", b"data")

    assert result.success is True
    assert result.attributes == {"number_top_left": "1"}


# --- Stage 1: failure classification (retrying itself happens inside the SDK, see
# picture-service-sdk-gemini-retries: an error reaching us is already final) ---


@pytest.mark.parametrize("error", [ModelRateLimitError("rate limited"), ModelAPIError("boom")])
async def test_attribute_detection_error_from_sdk_is_transport_failure_after_one_call(error):
    model = FakeModel([error, ok_raw_result({"a": "b"})])
    result = await detect_attributes(model, make_config(max_attempts=3), "card.jpg", b"data")

    assert model.call_count == 1
    assert result.success is False
    assert result.is_transport_failure is True


async def test_attribute_detection_non_retryable_fails_immediately():
    model = FakeModel([ModelInvalidRequestError("bad request")])
    result = await detect_attributes(model, make_config(max_attempts=3), "card.jpg", b"data")

    assert model.call_count == 1
    assert result.success is False
    assert result.is_transport_failure is True


async def test_attribute_detection_exception_is_transport_failure():
    model = FakeModel([ModelNotFoundError("not found")])
    result = await detect_attributes(model, make_config(), "card.jpg", b"data")

    assert result.is_transport_failure is True


# --- Stage 1: malformed/empty output ---


async def test_attribute_detection_empty_output_is_content_failure():
    model = FakeModel([{"raw": _RawMessage(""), "parsed": None, "parsing_error": None}])
    result = await detect_attributes(model, make_config(), "card.jpg", b"data")

    assert result.success is False
    assert result.is_transport_failure is False
    assert "kein JSON" in result.error_message


async def test_attribute_detection_invalid_json_preserves_raw_text():
    model = FakeModel([{"raw": _RawMessage("{not valid"), "parsed": None, "parsing_error": "oops"}])
    result = await detect_attributes(model, make_config(), "card.jpg", b"data")

    assert result.success is False
    assert result.is_transport_failure is False
    assert result.raw_model_response == "{not valid"


# --- Stage 2: request building ---


def test_derived_attributes_request_is_text_only():
    messages = build_derived_attributes_messages({"number_top_left": "1"})

    assert len(messages) == 1
    assert isinstance(messages[0]["content"], str)
    assert "number_top_left: 1" in messages[0]["content"]


# --- Stage 2: successful derivation ---


async def test_derived_attributes_produces_flat_key_value_map():
    model = FakeModel([ok_raw_result({"class": "character", "series_name": "Serie 1"})])
    result = await compute_derived_attributes(model, make_config(), {"number_top_left": "1"})

    assert result.success is True
    assert result.attributes == {"class": "character", "series_name": "Serie 1"}


async def test_derived_attributes_recognized_class_kept():
    model = FakeModel([ok_raw_result({"class": "vehicle"})])
    result = await compute_derived_attributes(model, make_config(), {})

    assert result.attributes["class"] == "vehicle"


async def test_derived_attributes_unrecognized_class_is_dropped():
    model = FakeModel([ok_raw_result({"class": "not-a-real-class", "series_name": "Serie 1"})])
    result = await compute_derived_attributes(model, make_config(), {})

    assert "class" not in result.attributes
    assert result.attributes["series_name"] == "Serie 1"


# --- Stage 2: failure classification (same as stage 1) ---


@pytest.mark.parametrize("error", [ModelRateLimitError("x"), ModelAPIError("boom")])
async def test_derived_attributes_error_from_sdk_is_transport_failure_after_one_call(error):
    model = FakeModel([error, ok_raw_result({"class": "art"})])
    result = await compute_derived_attributes(model, make_config(max_attempts=3), {})

    assert model.call_count == 1
    assert result.success is False
    assert result.is_transport_failure is True


async def test_derived_attributes_non_retryable_fails_immediately():
    model = FakeModel([ModelInvalidRequestError("bad request")])
    result = await compute_derived_attributes(model, make_config(max_attempts=3), {})

    assert model.call_count == 1
    assert result.is_transport_failure is True


# --- build_chat_model: the SDK's retry/timeout are configured from ScannerConfig ---


def test_build_chat_model_hands_attempts_and_timeout_to_the_sdk(monkeypatch):
    captured = {}

    class RecordingChatModel:
        def __init__(self, **kwargs):
            captured.update(kwargs)

        def with_structured_output(self, schema, include_raw):
            return ("structured", schema, include_raw)

    monkeypatch.setattr("langchain_google_genai.ChatGoogleGenerativeAI", RecordingChatModel)

    built = build_chat_model(make_config(max_attempts=4, timeout_seconds=45), ATTRIBUTE_DETECTION_SCHEMA)

    assert captured["max_retries"] == 4
    assert captured["timeout"] == 45
    assert captured["model"] == "gemini-test"
    assert built == ("structured", ATTRIBUTE_DETECTION_SCHEMA, True)


# --- analyze_card: sequential wiring across all three stages ---

CATALOG = CatalogSnapshot(
    series=(SeriesInfo(serie="Serie 1", jahr=2016),),
    cards=(CatalogCardInfo(series_name="Serie 1", card_number="1", card_name="Kai", category="Heroes"),),
)


def _build_model_factory(stage1_responses, stage2_responses=None):
    stage1_model = FakeModel(stage1_responses)
    stage2_model = FakeModel(stage2_responses or [])

    def build_model(config, schema):
        return stage1_model if schema is ATTRIBUTE_DETECTION_SCHEMA else stage2_model

    return build_model, stage1_model, stage2_model


async def test_analyze_card_runs_all_three_stages_on_success():
    build_model, _, stage2_model = _build_model_factory(
        [ok_raw_result({"card_number": "1"})], [ok_raw_result({"card_number": "1"})]
    )

    result = await analyze_card(build_model, make_config(), CATALOG, "p1", "card.jpg", b"data")

    assert stage2_model.call_count == 1
    assert result.analysis_status == AnalysisStatuses.OK
    assert result.set_name == "Serie 1"
    assert result.card_number == "1"
    assert result.detected == {"card_number": "1"}
    assert result.derived == {"card_number": "1"}


async def test_analyze_card_stage2_does_not_run_when_stage1_fails():
    build_model, _, stage2_model = _build_model_factory([ModelInvalidRequestError("bad")])

    result = await analyze_card(build_model, make_config(), CATALOG, "p1", "card.jpg", b"data")

    assert stage2_model.call_count == 0
    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.is_transport_failure is True


async def test_analyze_card_stage3_does_not_run_when_stage2_fails():
    build_model, _, _ = _build_model_factory(
        [ok_raw_result({"card_number": "1"})], [ModelInvalidRequestError("bad")]
    )

    result = await analyze_card(build_model, make_config(), CATALOG, "p1", "card.jpg", b"data")

    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.set_name is None
    assert result.detected == {"card_number": "1"}


# --- analyze_card: verified series/card number are pinned (picture-service-catalog-matching) ---

VERIFIED = VerifiedMatch(set_name="Serie 1", card_number="1")


async def test_analyze_card_reruns_stages_but_keeps_verified_series_and_number():
    build_model, stage1_model, stage2_model = _build_model_factory(
        [ok_raw_result({"card_number": "7"})], [ok_raw_result({"card_number": "7", "rarity": "rare"})]
    )

    result = await analyze_card(build_model, make_config(), CATALOG, "p1", "card.jpg", b"data", VERIFIED)

    assert stage1_model.call_count == 1
    assert stage2_model.call_count == 1
    assert result.analysis_status == AnalysisStatuses.OK
    assert result.set_name == "Serie 1"
    assert result.card_number == "1"
    assert result.card_name == "Kai"
    assert result.rarity is None
    assert result.detected == {"card_number": "7"}
    assert result.derived == {"card_number": "7", "rarity": "rare"}


async def test_analyze_card_stage1_failure_keeps_verified_series_and_number():
    build_model, _, _ = _build_model_factory([ModelInvalidRequestError("bad")])

    result = await analyze_card(build_model, make_config(), CATALOG, "p1", "card.jpg", b"data", VERIFIED)

    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.set_name == "Serie 1"
    assert result.card_number == "1"


async def test_analyze_card_stage2_failure_keeps_verified_series_and_number():
    build_model, _, _ = _build_model_factory([ok_raw_result({"card_number": "7"})], [ModelInvalidRequestError("bad")])

    result = await analyze_card(build_model, make_config(), CATALOG, "p1", "card.jpg", b"data", VERIFIED)

    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.set_name == "Serie 1"
    assert result.card_number == "1"

