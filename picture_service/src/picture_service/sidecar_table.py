"""Raw DynamoDB item CRUD for sidecar records, ported from SidecarTable.cs.

Internal collaborator of SidecarStore (sidecar_store.py), the repository-like entry point other
code should depend on - this module only knows about DynamoDB item shapes, not caching or any
sidecar-domain logic. Keyed by collection ID (partition key) and generated photo ID (sort key) -
see picture-service-photo-storage's "Photo storage is partitioned by collection" (matching the
S3 object identity in photo_store.py). Same PascalCase attribute names as the C# service so
this reads/writes the exact item shape already in production (see design.md's "Preserve the
exact DynamoDB item shape and S3 key layout").
"""

from __future__ import annotations

import json
import time
from collections.abc import AsyncIterator
from datetime import datetime
from decimal import Decimal
from typing import Any

from boto3.dynamodb.conditions import Attr
from botocore.exceptions import ClientError

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
_DETECTED_ATTR = "Detected"
_DERIVED_ATTR = "Derived"
_ROTATED_180_ATTR = "Rotated180"

# Completed-transfer records (TransferPhotos idempotency) share the table but live under a
# partition key that can never be a real collection ID, so per-collection queries never see them.
TRANSFER_KEY_PREFIX = "TRANSFER#"
_TRANSFER_SORT_KEY = "transfer"
_TRANSFER_RESULTS_ATTR = "Results"
_TRANSFER_STATUS_ATTR = "Status"
_TRANSFER_LEASE_ATTR = "LeaseExpiresAtMs"
_TRANSFER_SOURCES_REMOVED_ATTR = "SourcesRemoved"
_TRANSFER_EXPIRES_AT_ATTR = "ExpiresAt"  # epoch seconds; DynamoDB TTL attribute
TRANSFER_RECORD_LIFETIME_SECONDS = 30 * 24 * 60 * 60


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

    async def get_transfer(self, transfer_id: str) -> dict | None:
        """Returns {"status", "results", "lease_expires_at_ms", "sources_removed"} or None."""
        table = await self._table()
        response = await table.get_item(Key=_transfer_key(transfer_id), ConsistentRead=True)
        item = response.get("Item")
        return None if item is None else _transfer_from_item(item)

    async def create_transfer(self, transfer_id: str, record: dict) -> bool:
        """Atomically creates the transfer record; False when one already exists (another
        attempt, possibly on another machine, owns this transfer_id)."""
        table = await self._table()
        try:
            await table.put_item(
                Item=_transfer_to_item(transfer_id, record),
                ConditionExpression=Attr(_COLLECTION_ID_ATTR).not_exists(),
            )
        except ClientError as error:
            if error.response.get("Error", {}).get("Code") == "ConditionalCheckFailedException":
                return False
            raise
        return True

    async def replace_transfer(
        self, transfer_id: str, record: dict, *, expected_lease_expires_at_ms: int | None = None
    ) -> bool:
        """Overwrites the transfer record. With `expected_lease_expires_at_ms` the write only
        succeeds while the stored lease still has that value (compare-and-swap for taking over an
        expired lease); returns False when it lost that race."""
        table = await self._table()
        kwargs: dict[str, Any] = {}
        if expected_lease_expires_at_ms is not None:
            kwargs["ConditionExpression"] = Attr(_TRANSFER_LEASE_ATTR).eq(expected_lease_expires_at_ms)
        try:
            await table.put_item(Item=_transfer_to_item(transfer_id, record), **kwargs)
        except ClientError as error:
            if error.response.get("Error", {}).get("Code") == "ConditionalCheckFailedException":
                return False
            raise
        return True

    async def delete_transfer(self, transfer_id: str) -> None:
        table = await self._table()
        await table.delete_item(Key=_transfer_key(transfer_id))

    async def list_transfers(self) -> AsyncIterator[tuple[str, dict]]:
        table = await self._table()
        last_evaluated_key = None
        while True:
            kwargs: dict[str, Any] = {"FilterExpression": Attr(_COLLECTION_ID_ATTR).begins_with(TRANSFER_KEY_PREFIX)}
            if last_evaluated_key is not None:
                kwargs["ExclusiveStartKey"] = last_evaluated_key
            response = await table.scan(**kwargs)
            for item in response.get("Items", []):
                yield item[_COLLECTION_ID_ATTR][len(TRANSFER_KEY_PREFIX) :], _transfer_from_item(item)
            last_evaluated_key = response.get("LastEvaluatedKey")
            if last_evaluated_key is None:
                break

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

    async def list_source_file_names(self, collection_id: str) -> AsyncIterator[str]:
        """Yields the SourceFileName of every sidecar in one collection (once per record that
        has one) from a paginated, projected query. The projection shrinks the payload but not
        the consumed read capacity, which DynamoDB bills on full item size."""
        table = await self._table()
        source_file_name_attr = _STRING_ATTRS["source_file_name"]
        last_evaluated_key = None
        while True:
            kwargs = {
                "KeyConditionExpression": "CollectionId = :cid",
                "ExpressionAttributeValues": {":cid": collection_id},
                "ProjectionExpression": source_file_name_attr,
            }
            if last_evaluated_key is not None:
                kwargs["ExclusiveStartKey"] = last_evaluated_key
            response = await table.query(**kwargs)
            for item in response.get("Items", []):
                name = item.get(source_file_name_attr)
                if name:
                    yield name
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
                if item[_COLLECTION_ID_ATTR].startswith(TRANSFER_KEY_PREFIX):
                    continue
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

    # `None` (never written by the staged pipeline) omits the attribute entirely, distinct
    # from `{}` (an explicit empty section, written once analyzed/migrated) - see
    # SidecarRecord's `detected`/`derived` docstring.
    if record.detected is not None:
        item[_DETECTED_ATTR] = _encode_attribute_map(record.detected)
    if record.derived is not None:
        item[_DERIVED_ATTR] = _encode_attribute_map(record.derived)

    # Omitted when False, like every other optional field - absent reads back as unrotated
    # (see SidecarRecord.rotated_180's default and picture-service-sidecar-sections' "Rotation
    # flag defaults to unrotated").
    if record.rotated_180:
        item[_ROTATED_180_ATTR] = True

    return item


def _encode_attribute_map(attributes: dict) -> dict:
    # DynamoDB's number type has no float representation (see the Confidence attribute above).
    return {key: Decimal(str(value)) if isinstance(value, float) else value for key, value in attributes.items()}


def _decode_attribute_map(attributes: dict) -> dict:
    return {key: float(value) if isinstance(value, Decimal) else value for key, value in attributes.items()}


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

    detected_raw = item.get(_DETECTED_ATTR)
    derived_raw = item.get(_DERIVED_ATTR)

    return SidecarRecord(
        **{
            field_name: item.get(attr_name)
            for field_name, attr_name in _STRING_ATTRS.items()
        },
        confidence=float(confidence),
        detected_text=tuple(detected_text_raw) if detected_text_raw is not None else None,
        scanned_at_utc=scanned_at_utc,
        detected=_decode_attribute_map(detected_raw) if detected_raw is not None else None,
        derived=_decode_attribute_map(derived_raw) if derived_raw is not None else None,
        rotated_180=bool(item.get(_ROTATED_180_ATTR, False)),
    )


def _transfer_key(transfer_id: str) -> dict[str, str]:
    return {_COLLECTION_ID_ATTR: TRANSFER_KEY_PREFIX + transfer_id, _PHOTO_ID_ATTR: _TRANSFER_SORT_KEY}


def _transfer_to_item(transfer_id: str, record: dict) -> dict[str, Any]:
    return {
        **_transfer_key(transfer_id),
        _TRANSFER_RESULTS_ATTR: json.dumps(record["results"]),
        _TRANSFER_STATUS_ATTR: record["status"],
        _TRANSFER_LEASE_ATTR: int(record.get("lease_expires_at_ms", 0)),
        _TRANSFER_SOURCES_REMOVED_ATTR: bool(record.get("sources_removed", False)),
        _TRANSFER_EXPIRES_AT_ATTR: int(time.time()) + TRANSFER_RECORD_LIFETIME_SECONDS,
    }


def _transfer_from_item(item: dict[str, Any]) -> dict:
    # Items written before the status field existed were only ever stored once committed.
    return {
        "status": item.get(_TRANSFER_STATUS_ATTR, "committed"),
        "results": json.loads(item[_TRANSFER_RESULTS_ATTR]),
        "lease_expires_at_ms": int(item.get(_TRANSFER_LEASE_ATTR, 0)),
        "sources_removed": bool(item.get(_TRANSFER_SOURCES_REMOVED_ATTR, False)),
    }
