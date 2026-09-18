from collections.abc import AsyncIterator
from datetime import datetime, timezone

import aioboto3
import pytest_asyncio

from picture_service.models import SidecarRecord
from picture_service.sidecar_table import SidecarTable

TABLE_NAME = "test-sidecar-table"


@pytest_asyncio.fixture
async def sidecar_table(aws_session: aioboto3.Session, aws_endpoint_url: str) -> AsyncIterator[SidecarTable]:
    async with aws_session.resource("dynamodb", endpoint_url=aws_endpoint_url) as dynamodb:
        await dynamodb.create_table(
            TableName=TABLE_NAME,
            KeySchema=[
                {"AttributeName": "CollectionId", "KeyType": "HASH"},
                {"AttributeName": "PhotoId", "KeyType": "RANGE"},
            ],
            AttributeDefinitions=[
                {"AttributeName": "CollectionId", "AttributeType": "S"},
                {"AttributeName": "PhotoId", "AttributeType": "S"},
            ],
            BillingMode="PAY_PER_REQUEST",
        )
        yield SidecarTable(dynamodb, TABLE_NAME)


async def test_get_missing_returns_none(sidecar_table: SidecarTable):
    assert await sidecar_table.get("col-1", "missing") is None


async def test_put_and_get_round_trips_all_fields(sidecar_table: SidecarTable):
    scanned_at = datetime(2024, 1, 15, 10, 30, 0, 123456, tzinfo=timezone.utc)
    record = SidecarRecord(
        analysis_status="ok",
        review_status="verified",
        card_name="Kai",
        card_number="1",
        set_name="Serie 1",
        rarity="common",
        language="de",
        confidence=0.92,
        reasoning_summary="clear image",
        detected_text=("Kai", "1"),
        scanned_at_utc=scanned_at,
        error_message=None,
        source_file_name="a.jpg",
        ai_model="gemini-x",
        raw_model_response='{"status":"ok"}',
    )

    await sidecar_table.put("col-1", "photo-1", record)
    result = await sidecar_table.get("col-1", "photo-1")

    assert result == record


async def test_detected_text_with_duplicates_is_preserved(sidecar_table: SidecarTable):
    record = SidecarRecord(detected_text=("Kai", "Kai", "1"))

    await sidecar_table.put("col-1", "photo-dup", record)
    result = await sidecar_table.get("col-1", "photo-dup")

    assert result.detected_text == ("Kai", "Kai", "1")


async def test_delete_removes_item(sidecar_table: SidecarTable):
    await sidecar_table.put("col-1", "photo-1", SidecarRecord(analysis_status="ok"))

    await sidecar_table.delete("col-1", "photo-1")

    assert await sidecar_table.get("col-1", "photo-1") is None


async def test_list_by_collection_returns_only_that_collection(sidecar_table: SidecarTable):
    await sidecar_table.put("col-a", "p1", SidecarRecord(analysis_status="ok"))
    await sidecar_table.put("col-a", "p2", SidecarRecord(analysis_status="ok"))
    await sidecar_table.put("col-b", "p3", SidecarRecord(analysis_status="ok"))

    photo_ids = sorted([photo_id async for photo_id, _ in sidecar_table.list_by_collection("col-a")])

    assert photo_ids == ["p1", "p2"]


async def test_list_all_returns_every_collection(sidecar_table: SidecarTable):
    await sidecar_table.put("col-a", "p1", SidecarRecord(analysis_status="ok"))
    await sidecar_table.put("col-b", "p2", SidecarRecord(analysis_status="ok"))

    entries = sorted(
        [(cid, pid) async for cid, pid, _ in sidecar_table.list_all()]
    )

    assert entries == [("col-a", "p1"), ("col-b", "p2")]


async def test_confidence_defaults_to_zero_when_absent(sidecar_table: SidecarTable):
    await sidecar_table.put("col-1", "photo-1", SidecarRecord())

    result = await sidecar_table.get("col-1", "photo-1")

    assert result.confidence == 0.0


async def test_detected_and_derived_round_trip(sidecar_table: SidecarTable):
    record = SidecarRecord(
        detected={"number_top_left": "1", "color_area": 0.5},
        derived={"class": "character", "confident": True},
    )

    await sidecar_table.put("col-1", "photo-1", record)
    result = await sidecar_table.get("col-1", "photo-1")

    assert result.detected == {"number_top_left": "1", "color_area": 0.5}
    assert result.derived == {"class": "character", "confident": True}


async def test_reading_legacy_record_surfaces_empty_detected_and_derived(sidecar_table: SidecarTable):
    """A record put before this change never wrote the Detected/Derived attributes at all -
    simulated here by writing the item directly rather than through SidecarRecord, since
    SidecarTable.put always reflects whatever the (already-updated) SidecarRecord carries."""
    table = await sidecar_table._table()
    await table.put_item(
        Item={"CollectionId": "col-1", "PhotoId": "legacy-1", "AnalysisStatus": "ok", "CardName": "Kai"}
    )

    result = await sidecar_table.get("col-1", "legacy-1")

    assert result.analysis_status == "ok"
    assert result.card_name == "Kai"
    assert result.detected is None
    assert result.derived is None
