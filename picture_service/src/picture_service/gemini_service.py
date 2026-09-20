"""Gemini card analysis, staged per picture-service-staged-analysis-pipeline.

Stage 1 (attribute detection, see picture-service-attribute-detection) is a vision call, photo
only, no series catalog - a generic, open-ended key-value map of what's visible. Stage 2
(derived attributes, see picture-service-derived-attributes) is a text-only call reasoning over
stage 1's output, also a generic key-value map, plus a `class` value constrained to the
catalog's fixed set. Both calls share the same transport/content failure classification,
factored into `_invoke_model` below rather than duplicated per stage.

Uses `langchain-google-genai`'s structured-output chat model call in place of a hand-rolled
HTTP request/manual-JSON-parse (see design.md's "Decisions" for why). Retries are owned entirely
by the Gemini SDK underneath it (see picture-service-gemini-analysis and the
picture-service-sdk-gemini-retries change): this module makes one call per stage and classifies
whatever the SDK finally raises.

Stage 3 (catalog matching) is deterministic, no LLM call - see card_analysis_stage_3.py.
"""

from __future__ import annotations

import base64
from dataclasses import dataclass, field
from typing import Any, Protocol

from langchain_core.exceptions import ModelError, ModelNotFoundError
from opentelemetry import trace

from picture_service.config import ScannerConfig
from picture_service.models import (
    CARD_CLASSES,
    AnalysisStatuses,
    AttributeMap,
    CardAnalysisResult,
    CatalogSnapshot,
    VerifiedMatch,
    utc_now,
)
from picture_service.prompts import ATTRIBUTE_DETECTION_PROMPT, DERIVED_ATTRIBUTES_PROMPT
from picture_service.card_analysis_stage_3 import match_catalog

_tracer = trace.get_tracer("picture_service.gemini")

_MIME_TYPES = {
    ".jpg": "image/jpeg",
    ".jpeg": "image/jpeg",
    ".png": "image/png",
    ".bmp": "image/bmp",
    ".webp": "image/webp",
}

ATTRIBUTE_DETECTION_SCHEMA: dict[str, Any] = {
    "type": "object",
    "additionalProperties": {"type": ["string", "number", "boolean"]},
}

DERIVED_ATTRIBUTES_SCHEMA: dict[str, Any] = {
    "type": "object",
    "additionalProperties": {"type": ["string", "number", "boolean"]},
}


class StructuredModel(Protocol):
    """Minimal shape gemini_service depends on - a LangChain `with_structured_output(...,
    include_raw=True)` runnable, or any fake providing the same `ainvoke` contract for tests.
    """

    async def ainvoke(self, messages: list[Any]) -> dict[str, Any]: ...


def build_chat_model(config: ScannerConfig, schema: dict[str, Any]) -> StructuredModel:
    """Builds a fresh structured-output model bound to one stage's schema - stage 1 and stage 2
    use different schemas, so each stage calls this with its own (see analyze_card)."""
    from langchain_google_genai import ChatGoogleGenerativeAI

    # The Gemini SDK owns retries: max_retries is the *total* attempt count including the first
    # request (1 = no retries; 0 would mean the SDK's own default of 5, which max_attempts >= 1
    # from ScannerConfig rules out). timeout bounds one attempt, so a hung request is abandoned
    # and retried like any other transient failure. Backoff shape (~1s doubling, jittered, capped
    # at 60s) is the SDK's and isn't configurable through LangChain.
    model = ChatGoogleGenerativeAI(
        model=config.model,
        api_key=config.api_key,
        temperature=0.2,
        timeout=config.timeout_seconds,
        max_retries=config.max_attempts,
    )
    return model.with_structured_output(schema, include_raw=True)


def _get_mime_type(source_file_name: str) -> str:
    return _MIME_TYPES.get(_extension(source_file_name), "application/octet-stream")


def _extension(source_file_name: str) -> str:
    dot_index = source_file_name.rfind(".")
    return source_file_name[dot_index:].lower() if dot_index != -1 else ""


def build_attribute_detection_messages(source_file_name: str, image_bytes: bytes) -> list[dict]:
    mime_type = _get_mime_type(source_file_name)
    data_url = f"data:{mime_type};base64,{base64.b64encode(image_bytes).decode('ascii')}"

    return [
        {
            "role": "user",
            "content": [
                {"type": "text", "text": ATTRIBUTE_DETECTION_PROMPT},
                {"type": "image_url", "image_url": data_url},
            ],
        }
    ]


def build_derived_attributes_messages(detected_attributes: AttributeMap) -> list[dict]:
    attribute_lines = "\n".join(f"{key}: {value}" for key, value in detected_attributes.items())
    return [{"role": "user", "content": DERIVED_ATTRIBUTES_PROMPT + attribute_lines}]


@dataclass(frozen=True)
class AttributeStageResult:
    """The outcome of one stage 1 or stage 2 call (picture-service-attribute-detection,
    picture-service-derived-attributes)."""

    success: bool
    attributes: AttributeMap = field(default_factory=dict)
    error_message: str | None = None
    raw_model_response: str | None = None
    is_transport_failure: bool = False


@dataclass(frozen=True)
class _InvokeOutcome:
    raw_result: dict[str, Any] | None
    failure_message: str | None


def _failure_message(exception: ModelError, model: str) -> str:
    if isinstance(exception, ModelNotFoundError) and "no longer available" in str(exception).lower():
        return (
            f"Gemini-Modell '{model}' ist nicht mehr verfuegbar. Setze Gemini:Model oder "
            "GEMINI_MODEL auf ein aktuelles Modell, z. B. 'gemini-2.5-flash'."
        )
    return f"Gemini API Fehler: {exception}"


async def _invoke_model(model: StructuredModel, config: ScannerConfig, messages: list[dict]) -> _InvokeOutcome:
    """One call to Gemini; any retrying happened inside the SDK before an exception reached
    here. A `ModelError` (what LangChain raises for the HTTP errors it recognizes, once the SDK
    has given up or the error wasn't retryable) becomes a failure outcome. Any other exception
    propagates uncaught, and only the RPC handler's outer try/except classifies it as a
    transport failure.
    """
    try:
        with _tracer.start_as_current_span("gemini.invoke", attributes={"gemini.model": config.model}):
            raw_result = await model.ainvoke(messages)
    except ModelError as exception:
        return _InvokeOutcome(raw_result=None, failure_message=_failure_message(exception, config.model))

    return _InvokeOutcome(raw_result=raw_result, failure_message=None)


def _parse_attribute_map(raw_result: dict[str, Any]) -> tuple[AttributeMap | None, str | None, str | None]:
    """Returns `(attributes, error_message, raw_text)` - `attributes` is `None` on a
    content-level failure (see picture-service-attribute-detection's "A failure result
    indicates whether Gemini evaluated the photo"). A value that isn't a scalar (object or
    array) is dropped rather than accepted as-is (that requirement's "A value is not a nested
    structure")."""
    raw_message = raw_result.get("raw")
    raw_text = getattr(raw_message, "content", None) if raw_message is not None else None
    if isinstance(raw_text, list):
        raw_text = "".join(part.get("text", "") if isinstance(part, dict) else str(part) for part in raw_text)

    parsed = raw_result.get("parsed")
    if parsed is None:
        if not raw_text or not str(raw_text).strip():
            return None, "Gemini hat kein JSON-Ergebnis geliefert.", raw_text

        parsing_error = raw_result.get("parsing_error")
        detail = f": {parsing_error}" if parsing_error else ""
        return None, f"Gemini-Antwort war kein gueltiges JSON{detail}", str(raw_text)

    if not isinstance(parsed, dict):
        return None, "Gemini-Antwort war kein Objekt.", str(raw_text) if raw_text else None

    attributes: AttributeMap = {
        key: value for key, value in parsed.items() if value is not None and not isinstance(value, (dict, list))
    }

    return attributes, None, str(raw_text) if raw_text else None


async def detect_attributes(
    model: StructuredModel, config: ScannerConfig, source_file_name: str, image_bytes: bytes
) -> AttributeStageResult:
    """Stage 1 - see picture-service-attribute-detection."""
    with _tracer.start_as_current_span("gemini.stage1_detect_attributes", attributes={"image.bytes": len(image_bytes)}):
        messages = build_attribute_detection_messages(source_file_name, image_bytes)
        outcome = await _invoke_model(model, config, messages)

    if outcome.raw_result is None:
        return AttributeStageResult(success=False, error_message=outcome.failure_message, is_transport_failure=True)

    attributes, error_message, raw_text = _parse_attribute_map(outcome.raw_result)
    if attributes is None:
        return AttributeStageResult(success=False, error_message=error_message, raw_model_response=raw_text)

    return AttributeStageResult(success=True, attributes=attributes, raw_model_response=raw_text)


async def compute_derived_attributes(
    model: StructuredModel, config: ScannerConfig, detected_attributes: AttributeMap
) -> AttributeStageResult:
    """Stage 2 - see picture-service-derived-attributes."""
    with _tracer.start_as_current_span("gemini.stage2_derive_attributes"):
        messages = build_derived_attributes_messages(detected_attributes)
        outcome = await _invoke_model(model, config, messages)

    if outcome.raw_result is None:
        return AttributeStageResult(success=False, error_message=outcome.failure_message, is_transport_failure=True)

    attributes, error_message, raw_text = _parse_attribute_map(outcome.raw_result)
    if attributes is None:
        return AttributeStageResult(success=False, error_message=error_message, raw_model_response=raw_text)

    if "class" in attributes and attributes["class"] not in CARD_CLASSES:
        attributes = {key: value for key, value in attributes.items() if key != "class"}

    return AttributeStageResult(success=True, attributes=attributes, raw_model_response=raw_text)


ModelFactory = Any  # Callable[[ScannerConfig, dict[str, Any]], StructuredModel] - see picture_scanner_service.py


async def analyze_card(
    build_model: ModelFactory,
    config: ScannerConfig,
    catalog: CatalogSnapshot,
    photo_id: str,
    source_file_name: str,
    image_bytes: bytes,
    verified: VerifiedMatch | None = None,
) -> CardAnalysisResult:
    """Runs all three stages sequentially (picture-service-staged-analysis-pipeline's design.md
    "Where stage 2's output lands relative to stage 3") and combines their outcomes into the
    sidecar's Judged section (picture-service-catalog-matching's "Judged analysis status
    reflects detection, derivation, and matching outcomes").

    `verified` is the series/card number a human already confirmed for this photo, if any:
    stages 1 and 2 still re-run, but the result always carries that series and card number
    (picture-service-catalog-matching's "Verified series and card number survive re-analysis")."""
    detection = await detect_attributes(
        build_model(config, ATTRIBUTE_DETECTION_SCHEMA), config, source_file_name, image_bytes
    )
    if not detection.success:
        return _stage_failure_result(photo_id, source_file_name, config.model, detection, verified=verified)

    derivation = await compute_derived_attributes(
        build_model(config, DERIVED_ATTRIBUTES_SCHEMA), config, detection.attributes
    )
    if not derivation.success:
        return _stage_failure_result(
            photo_id, source_file_name, config.model, derivation, detected=detection.attributes, verified=verified
        )

    with _tracer.start_as_current_span("catalog.match"):
        match = match_catalog(derivation.attributes, catalog, verified)

    return CardAnalysisResult(
        photo_id=photo_id,
        analysis_status=match.analysis_status,
        source_file_name=source_file_name,
        ai_model=config.model,
        scanned_at_utc=utc_now(),
        card_name=match.card_name,
        card_number=match.card_number,
        set_name=match.set_name,
        language=match.language,
        error_message=match.error_message,
        detected=detection.attributes,
        derived=derivation.attributes,
    )


def _stage_failure_result(
    photo_id: str,
    source_file_name: str,
    model: str,
    stage_result: AttributeStageResult,
    *,
    detected: AttributeMap | None = None,
    verified: VerifiedMatch | None = None,
) -> CardAnalysisResult:
    return CardAnalysisResult(
        photo_id=photo_id,
        analysis_status=AnalysisStatuses.FAILED,
        source_file_name=source_file_name,
        ai_model=model,
        scanned_at_utc=utc_now(),
        set_name=verified.set_name if verified else None,
        card_number=verified.card_number if verified else None,
        error_message=stage_result.error_message,
        raw_model_response=stage_result.raw_model_response,
        is_transport_failure=stage_result.is_transport_failure,
        detected=dict(detected) if detected is not None else {},
    )
