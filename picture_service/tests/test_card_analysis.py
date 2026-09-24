from langchain_core.exceptions import ModelInvalidRequestError

from picture_service.card_analysis import analyze_card
from picture_service.card_analysis_stage_1_and_2 import ATTRIBUTE_DETECTION_SCHEMA
from picture_service.config import ScannerConfig
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
