"""Gemini card analysis, ported from GeminiApiService.cs.

Uses `langchain-google-genai`'s structured-output chat model call in place of the
hand-rolled HTTP request/manual-JSON-parse the C# version used (see design.md's "Decisions"
for why). The request content, retry policy (retry only on 429/5xx, `retry_delay_ms * attempt`
backoff, immediate failure otherwise), and parsed result shape are preserved 1:1 - the retry
loop and transport/content failure classification stay hand-rolled here rather than relying on
LangChain's generic built-in retry, which doesn't implement this exact policy.
"""

from __future__ import annotations

import asyncio
import base64
from typing import Any, Protocol

from langchain_core.exceptions import ModelError, ModelNotFoundError

from picture_service.config import ScannerConfig
from picture_service.models import AnalysisStatuses, CardAnalysisResult, Languages, SeriesInfo, utc_now
from picture_service.series_catalog_service import GeminiCardPayload, build_prompt, resolve_set_name

_LOW_CONFIDENCE_THRESHOLD = 0.65

_MIME_TYPES = {
    ".jpg": "image/jpeg",
    ".jpeg": "image/jpeg",
    ".png": "image/png",
    ".bmp": "image/bmp",
    ".webp": "image/webp",
}

RESPONSE_SCHEMA: dict[str, Any] = {
    "type": "object",
    "properties": {
        "status": {"type": "string", "enum": ["ok", "uncertain", "failed"]},
        "cardName": {"type": "string"},
        "cardNumber": {"type": "string"},
        "setName": {"type": "string"},
        "rarity": {"type": "string"},
        "language": {"type": "string", "enum": ["de", "en", "pl", "unknown"]},
        "confidence": {"type": "number"},
        "reasoningSummary": {"type": "string"},
        "detectedText": {"type": "array", "items": {"type": "string"}},
    },
    "required": ["status", "confidence", "reasoningSummary", "detectedText"],
}

_PROMPT_INTRO = """\
Du analysierst genau ein Foto einer Lego Ninjago Sammelkarte.
Gib ausschliesslich gueltiges JSON ohne Markdown oder Codeblock zurueck.
Wenn du dir nicht sicher bist, setze status auf "uncertain" und confidence entsprechend niedrig.
Wenn das Bild keine klar lesbare einzelne Karte zeigt, setze status auf "failed".
Bestimme setName primaer ueber die Kartennummer und den Text auf der Karte (Kartenname).
Du kannst auch das Symbol in der unteren rechten Ecke der Karte benutzen.
Wenn kein Symbol vorhanden ist, gehoert die Karte zu Serie 1.
Bestimme die Sprache (language) anhand des gedruckten Textes und Charakternamens auf der Karte:
"de" fuer deutschen Text, "en" fuer englischen Text, "pl" fuer polnischen Text,
"unknown" wenn die Sprache nicht sicher bestimmt werden kann.

Verwende exakt dieses JSON-Schema:
{
  "status": "ok|uncertain|failed",
  "cardName": "string|null",
  "cardNumber": "string|null",
  "setName": "string|null",
  "rarity": "string|null",
  "language": "de|en|pl|unknown",
  "confidence": 0.0,
  "reasoningSummary": "string",
  "detectedText": ["string"]
}

Nutze sichtbare Kartennummern in der unteren linken Ecke, Charakternamen, Set-Hinweise, das Symbol unten rechts und Seltenheitsmerkmale.
Fuelle setName nur mit einem gueltigen Seriennamen aus der folgenden Liste:
"""


class StructuredModel(Protocol):
    """Minimal shape gemini_service depends on - a LangChain `with_structured_output(...,
    include_raw=True)` runnable, or any fake providing the same `ainvoke` contract for tests.
    """

    async def ainvoke(self, messages: list[Any]) -> dict[str, Any]: ...


def build_prompt_text(series_catalog: list[SeriesInfo]) -> str:
    return _PROMPT_INTRO + build_prompt(series_catalog)


def _get_mime_type(source_file_name: str) -> str:
    return _MIME_TYPES.get(_extension(source_file_name), "application/octet-stream")


def _extension(source_file_name: str) -> str:
    dot_index = source_file_name.rfind(".")
    return source_file_name[dot_index:].lower() if dot_index != -1 else ""


def build_messages(config: ScannerConfig, series_catalog: list[SeriesInfo], source_file_name: str, image_bytes: bytes) -> list[dict]:
    prompt = build_prompt_text(series_catalog)
    mime_type = _get_mime_type(source_file_name)
    data_url = f"data:{mime_type};base64,{base64.b64encode(image_bytes).decode('ascii')}"

    return [
        {
            "role": "user",
            "content": [
                {"type": "text", "text": prompt},
                {"type": "image_url", "image_url": data_url},
            ],
        }
    ]


def build_chat_model(config: ScannerConfig) -> StructuredModel:
    from langchain_google_genai import ChatGoogleGenerativeAI

    model = ChatGoogleGenerativeAI(model=config.model, api_key=config.api_key, temperature=0.2)
    return model.with_structured_output(RESPONSE_SCHEMA, include_raw=True)


def _failure_message(exception: ModelError, model: str) -> str:
    if isinstance(exception, ModelNotFoundError) and "no longer available" in str(exception).lower():
        return (
            f"Gemini-Modell '{model}' ist nicht mehr verfuegbar. Setze Gemini:Model oder "
            "GEMINI_MODEL auf ein aktuelles Modell, z. B. 'gemini-2.5-flash'."
        )
    return f"Gemini API Fehler: {exception}"


async def analyze_card(
    model: StructuredModel,
    config: ScannerConfig,
    series_catalog: list[SeriesInfo],
    photo_id: str,
    source_file_name: str,
    image_bytes: bytes,
) -> CardAnalysisResult:
    """Retries only on conditions LangChain classifies as retryable (rate limits, server
    errors, connection/timeout issues - see langchain_core.exceptions.ModelError.is_retryable),
    the provider-agnostic equivalent of the original HTTP 429/5xx-only policy. Any other
    exception (not a ModelError at all) propagates uncaught, mirroring the C# version, where an
    exception raised while attempting the call was never retried in this loop - only the RPC
    handler's outer try/except classified it as a transport failure.
    """
    messages = build_messages(config, series_catalog, source_file_name, image_bytes)

    for attempt in range(1, config.max_attempts + 1):
        try:
            raw_result = await model.ainvoke(messages)
        except ModelError as exception:
            if exception.is_retryable and attempt < config.max_attempts:
                await asyncio.sleep(config.retry_delay_ms * attempt / 1000)
                continue

            message = _failure_message(exception, config.model)
            return _create_failure_result(photo_id, source_file_name, config.model, message, is_transport_failure=True)

        return _parse_success(photo_id, source_file_name, config.model, series_catalog, raw_result)

    return _create_failure_result(photo_id, source_file_name, config.model, "Unbekannter API-Fehler.", is_transport_failure=True)


def _parse_success(
    photo_id: str,
    source_file_name: str,
    model: str,
    series_catalog: list[SeriesInfo],
    raw_result: dict[str, Any],
) -> CardAnalysisResult:
    raw_message = raw_result.get("raw")
    raw_text = getattr(raw_message, "content", None) if raw_message is not None else None
    if isinstance(raw_text, list):
        raw_text = "".join(part.get("text", "") if isinstance(part, dict) else str(part) for part in raw_text)

    parsed = raw_result.get("parsed")
    if parsed is None:
        if not raw_text or not str(raw_text).strip():
            return _create_failure_result(
                photo_id, source_file_name, model, "Gemini hat kein JSON-Ergebnis geliefert.", raw_text
            )

        parsing_error = raw_result.get("parsing_error")
        detail = f": {parsing_error}" if parsing_error else ""
        return _create_failure_result(
            photo_id,
            source_file_name,
            model,
            f"Gemini-Antwort war kein gueltiges JSON{detail}",
            str(raw_text),
        )

    payload = GeminiCardPayload(
        status=parsed.get("status"),
        card_name=parsed.get("cardName"),
        card_number=parsed.get("cardNumber"),
        set_name=parsed.get("setName"),
        rarity=parsed.get("rarity"),
        language=parsed.get("language"),
        confidence=parsed.get("confidence", 0.0) or 0.0,
        reasoning_summary=parsed.get("reasoningSummary"),
        detected_text=tuple(parsed.get("detectedText") or ()),
    )

    normalized_status = _normalize_status(payload.status, payload.confidence)
    resolved_set_name = resolve_set_name(payload, series_catalog)

    final_status = normalized_status
    if normalized_status == AnalysisStatuses.FAILED:
        final_set_name = None
    elif resolved_set_name is not None:
        final_set_name = resolved_set_name
    else:
        final_set_name = payload.set_name.strip() if payload.set_name else None
        final_status = AnalysisStatuses.FAILED

    return CardAnalysisResult(
        photo_id=photo_id,
        analysis_status=final_status,
        source_file_name=source_file_name,
        ai_model=model,
        scanned_at_utc=utc_now(),
        card_name=payload.card_name,
        card_number=payload.card_number,
        set_name=final_set_name,
        rarity=payload.rarity,
        language=_normalize_language(payload.language),
        confidence=_clamp_confidence(payload.confidence),
        reasoning_summary=payload.reasoning_summary,
        detected_text=payload.detected_text,
        raw_model_response=str(raw_text) if raw_text else None,
    )


def _create_failure_result(
    photo_id: str,
    source_file_name: str,
    model: str,
    error_message: str,
    raw_model_response: str | None = None,
    *,
    is_transport_failure: bool = False,
) -> CardAnalysisResult:
    return CardAnalysisResult(
        photo_id=photo_id,
        analysis_status=AnalysisStatuses.FAILED,
        source_file_name=source_file_name,
        ai_model=model,
        scanned_at_utc=utc_now(),
        error_message=error_message,
        raw_model_response=raw_model_response,
        is_transport_failure=is_transport_failure,
    )


def _normalize_status(status: str | None, confidence: float) -> str:
    if status is not None and status.lower() == AnalysisStatuses.FAILED:
        return AnalysisStatuses.FAILED

    if (status is not None and status.lower() == AnalysisStatuses.UNCERTAIN) or confidence < _LOW_CONFIDENCE_THRESHOLD:
        return AnalysisStatuses.UNCERTAIN

    return AnalysisStatuses.OK


def _normalize_language(language: str | None) -> str:
    if language is not None:
        lowered = language.lower()
        for known in (Languages.GERMAN, Languages.ENGLISH, Languages.POLISH):
            if lowered == known:
                return known
    return Languages.UNKNOWN


def _clamp_confidence(value: float) -> float:
    try:
        if value != value or value in (float("inf"), float("-inf")):  # NaN check
            return 0.0
    except TypeError:
        return 0.0
    return max(0.0, min(1.0, value))
