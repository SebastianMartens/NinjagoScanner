"""DynamoDB-backed sidecar record storage, ported from SidecarTable.cs.

Keyed by collection ID (partition key) and generated photo ID (sort key) - see
picture-service-photo-storage's "Photo storage is partitioned by collection" (matching the
S3 object identity in photo_store.py). Same PascalCase attribute names as the C# service so
this reads/writes the exact item shape already in production (see design.md's "Preserve the
exact DynamoDB item shape and S3 key layout").
"""

from __future__ import annotations

from collections.abc import AsyncIterator
from datetime import datetime
from decimal import Decimal
from typing import Any

from picture_service.models import SidecarRecord

_COLLECTION_ID_ATTR = "CollectionId"
_PHOTO_ID_ATTR = "PhotoId"

# SidecarRecord field name -> DynamoDB attribute name (PascalCase, matching the C# item shape).
_STRING_ATTRS = {
    "analysis_status": "AnalysisStatus",
    "review_status": "ReviewStatus",
    "card_name": "CardName",
    "card_number": "CardNumber",
    "set_name": "SetName",
    "rarity": "Rarity",
    "language": "Language",
    "reasoning_summary": "ReasoningSummary",
    "error_message": "ErrorMessage",
    "source_file_name": "SourceFileName",
    "ai_model": "AiModel",
    "raw_model_response": "RawModelResponse",
}
_CONFIDENCE_ATTR = "Confidence"
_DETECTED_TEXT_ATTR = "DetectedText"
_SCANNED_AT_UTC_ATTR = "ScannedAtUtc"


class SidecarTable:
    def __init__(self, dynamodb_resource: Any, table_name: str) -> None:
        """`dynamodb_resource` is an already-open aioboto3 DynamoDB resource, held for the
        caller's lifetime (see main.py's `_serve`) rather than opened fresh per call - a fresh
        resource per call was the picture-service-aws-client-reuse regression this replaced."""
        self._dynamodb = dynamodb_resource
        self._table_name = table_name

    async def _table(self):
        return await self._dynamodb.Table(self._table_name)

    async def get(self, collection_id: str, photo_id: str) -> SidecarRecord | None:
        table = await self._table()
        response = await table.get_item(
            Key={_COLLECTION_ID_ATTR: collection_id, _PHOTO_ID_ATTR: photo_id}
        )
        item = response.get("Item")
        return None if item is None else _from_item(item)

    async def put(self, collection_id: str, photo_id: str, record: SidecarRecord) -> None:
        item = _to_item(collection_id, photo_id, record)
        table = await self._table()
        await table.put_item(Item=item)

    async def delete(self, collection_id: str, photo_id: str) -> None:
        table = await self._table()
        await table.delete_item(Key={_COLLECTION_ID_ATTR: collection_id, _PHOTO_ID_ATTR: photo_id})

    async def list_by_collection(self, collection_id: str) -> AsyncIterator[tuple[str, SidecarRecord]]:
        table = await self._table()
        last_evaluated_key = None
        while True:
            kwargs = {"KeyConditionExpression": "CollectionId = :cid", "ExpressionAttributeValues": {":cid": collection_id}}
            if last_evaluated_key is not None:
                kwargs["ExclusiveStartKey"] = last_evaluated_key
            response = await table.query(**kwargs)
            for item in response.get("Items", []):
                yield item[_PHOTO_ID_ATTR], _from_item(item)
            last_evaluated_key = response.get("LastEvaluatedKey")
            if last_evaluated_key is None:
                break

    async def list_all(self) -> AsyncIterator[tuple[str, str, SidecarRecord]]:
        table = await self._table()
        last_evaluated_key = None
        while True:
            kwargs = {}
            if last_evaluated_key is not None:
                kwargs["ExclusiveStartKey"] = last_evaluated_key
            response = await table.scan(**kwargs)
            for item in response.get("Items", []):
                yield item[_COLLECTION_ID_ATTR], item[_PHOTO_ID_ATTR], _from_item(item)
            last_evaluated_key = response.get("LastEvaluatedKey")
            if last_evaluated_key is None:
                break


def _to_item(collection_id: str, photo_id: str, record: SidecarRecord) -> dict:
    item: dict = {_COLLECTION_ID_ATTR: collection_id, _PHOTO_ID_ATTR: photo_id}

    for field_name, attr_name in _STRING_ATTRS.items():
        value = getattr(record, field_name)
        if value:
            item[attr_name] = value

    # DynamoDB's number type has no float representation - boto3's resource layer requires
    # Decimal for numeric values (see boto3.dynamodb.types.TypeSerializer).
    item[_CONFIDENCE_ATTR] = Decimal(str(record.confidence))

    if record.detected_text:
        # Explicit list (not DynamoDB's Set type) because OCR-detected text legitimately
        # contains duplicate entries, which a Set would silently collapse.
        item[_DETECTED_TEXT_ATTR] = list(record.detected_text)

    if record.scanned_at_utc is not None:
        item[_SCANNED_AT_UTC_ATTR] = record.scanned_at_utc.isoformat()

    return item


def _from_item(item: dict) -> SidecarRecord:
    scanned_at_raw = item.get(_SCANNED_AT_UTC_ATTR)
    scanned_at_utc: datetime | None = None
    if scanned_at_raw:
        try:
            scanned_at_utc = datetime.fromisoformat(scanned_at_raw)
        except ValueError:
            scanned_at_utc = None

    detected_text_raw = item.get(_DETECTED_TEXT_ATTR)
    confidence = item.get(_CONFIDENCE_ATTR, 0)

    return SidecarRecord(
        **{
            field_name: item.get(attr_name)
            for field_name, attr_name in _STRING_ATTRS.items()
        },
        confidence=float(confidence),
        detected_text=tuple(detected_text_raw) if detected_text_raw is not None else None,
        scanned_at_utc=scanned_at_utc,
    )
