"""Card analysis orchestration and workflow: sequences the three-stage pipeline and combines
their outcomes into a `CardAnalysisResult`.

Stage 1 (attribute detection) and stage 2 (derived attributes) are the Gemini calls, implemented
in card_analysis_stage_1_and_2.py. Stage 3 (catalog matching) is deterministic, no LLM call, and
implemented in card_analysis_stage_3.py. This module owns only the sequencing and
result-combining between them - see picture-service-staged-analysis-pipeline.
"""

from __future__ import annotations

from collections.abc import Callable
from typing import Any

from opentelemetry import trace

from picture_service.card_analysis_stage_1_and_2 import (
    ATTRIBUTE_DETECTION_SCHEMA,
    DERIVED_ATTRIBUTES_SCHEMA,
    AttributeStageResult,
    StructuredModel,
    compute_derived_attributes,
    detect_attributes,
)
from picture_service.card_analysis_stage_3 import match_catalog
from picture_service.config import ScannerConfig
from picture_service.models import (
    AnalysisStatuses,
    AttributeMap,
    CardAnalysisResult,
    CatalogSnapshot,
    VerifiedMatch,
    utc_now,
)

_tracer = trace.get_tracer("picture_service.gemini")

ModelFactory = Callable[[ScannerConfig, dict[str, Any]], StructuredModel]


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
