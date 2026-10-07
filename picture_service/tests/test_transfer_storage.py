"""Storage-level pieces of TransferPhotos: PhotoStore.copy (against mocked S3), the transfer
record in SidecarTable (against mocked DynamoDB) and SidecarStore.copy_record."""

import json
from collections.abc import AsyncIterator

import aioboto3
import pytest
import pytest_asyncio
from botocore.exceptions import ClientError

from picture_service.models import SidecarRecord
from picture_service.photo_store import PhotoStore
from picture_service.sidecar_store import SidecarStore
from picture_service.sidecar_table import SidecarTable
from tests.fakes import FakeSidecarTable

BUCKET = "test-photos-bucket"
TABLE_NAME = "test-sidecar-table"


@pytest_asyncio.fixture
async def photo_store(aws_session: aioboto3.Session, aws_endpoint_url: str) -> AsyncIterator[PhotoStore]:
    async with aws_session.client("s3", endpoint_url=aws_endpoint_url) as s3:
        await s3.create_bucket(Bucket=BUCKET)
        yield PhotoStore(s3, BUCKET)


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


async def test_copy_duplicates_bytes_under_new_key_and_keeps_source(photo_store: PhotoStore):
    await photo_store.put_bytes("col-1", "photo-1", b"payload")

    await photo_store.copy("col-1", "photo-1", "col-2", "photo-new")

    assert await photo_store.get_bytes("col-2", "photo-new") == b"payload"
    assert await photo_store.get_bytes("col-1", "photo-1") == b"payload"


async def test_copy_missing_source_raises_and_creates_nothing(photo_store: PhotoStore):
    with pytest.raises(ClientError):
        await photo_store.copy("col-1", "missing", "col-2", "photo-new")

    assert await photo_store.exists("col-2", "photo-new") is False


RESULTS = [{"old_photo_id": "a", "new_photo_id": "b", "source_collection_id": "c1", "dest_collection_id": "c2"}]


def transfer_record(status="pending", lease=1000, sources_removed=False):
    return {"status": status, "results": RESULTS, "lease_expires_at_ms": lease, "sources_removed": sources_removed}


async def test_transfer_record_round_trips_and_is_absent_when_unknown(sidecar_table: SidecarTable):
    assert await sidecar_table.get_transfer("t-1") is None

    assert await sidecar_table.create_transfer("t-1", transfer_record()) is True

    assert await sidecar_table.get_transfer("t-1") == transfer_record()
    assert await sidecar_table.get_transfer("t-2") is None


async def test_create_transfer_is_conditional_and_never_overwrites(sidecar_table: SidecarTable):
    await sidecar_table.create_transfer("t-1", transfer_record(status="committed"))

    assert await sidecar_table.create_transfer("t-1", transfer_record(status="pending")) is False
    assert (await sidecar_table.get_transfer("t-1"))["status"] == "committed"


async def test_replace_transfer_compare_and_swap_on_lease(sidecar_table: SidecarTable):
    await sidecar_table.create_transfer("t-1", transfer_record(lease=1000))

    assert await sidecar_table.replace_transfer("t-1", transfer_record(lease=2000), expected_lease_expires_at_ms=999) is False
    assert (await sidecar_table.get_transfer("t-1"))["lease_expires_at_ms"] == 1000
    assert await sidecar_table.replace_transfer("t-1", transfer_record(lease=2000), expected_lease_expires_at_ms=1000) is True
    assert (await sidecar_table.get_transfer("t-1"))["lease_expires_at_ms"] == 2000
    assert await sidecar_table.replace_transfer("t-1", transfer_record(status="committed", lease=2000)) is True
    assert (await sidecar_table.get_transfer("t-1"))["status"] == "committed"


async def test_delete_transfer_removes_record(sidecar_table: SidecarTable):
    await sidecar_table.create_transfer("t-1", transfer_record())

    await sidecar_table.delete_transfer("t-1")

    assert await sidecar_table.get_transfer("t-1") is None


async def test_legacy_transfer_item_without_status_reads_as_committed(sidecar_table: SidecarTable):
    table = await sidecar_table._table()
    await table.put_item(
        Item={"CollectionId": "TRANSFER#old", "PhotoId": "transfer", "Results": json.dumps(RESULTS), "ExpiresAt": 1}
    )

    record = await sidecar_table.get_transfer("old")

    assert record["status"] == "committed" and record["results"] == RESULTS


async def test_list_transfers_returns_only_transfer_records(sidecar_table: SidecarTable):
    await sidecar_table.put("col-1", "p1", SidecarRecord(card_name="Kai"))
    await sidecar_table.create_transfer("t-1", transfer_record())
    await sidecar_table.create_transfer("t-2", transfer_record(status="committed"))

    listed = {transfer_id: record["status"] async for transfer_id, record in sidecar_table.list_transfers()}

    assert listed == {"t-1": "pending", "t-2": "committed"}


async def test_transfer_record_has_ttl_attribute(sidecar_table: SidecarTable):
    await sidecar_table.create_transfer("t-1", transfer_record())

    table = await sidecar_table._table()
    item = (await table.get_item(Key={"CollectionId": "TRANSFER#t-1", "PhotoId": "transfer"}))["Item"]

    assert int(item["ExpiresAt"]) > 0


async def test_transfer_records_are_invisible_to_collection_listing_and_list_all(sidecar_table: SidecarTable):
    await sidecar_table.put("col-1", "p1", SidecarRecord(card_name="Kai"))
    await sidecar_table.create_transfer("t-1", transfer_record())

    assert [pid async for pid, _ in sidecar_table.list_by_collection("col-1")] == ["p1"]
    assert [(cid, pid) async for cid, pid, _ in sidecar_table.list_all()] == [("col-1", "p1")]


async def test_copy_record_writes_identical_record_under_destination_and_keeps_source():
    table = FakeSidecarTable()
    record = SidecarRecord(analysis_status="ok", review_status="verified", card_name="Kai", rotated_180=True)
    table.items[("col-1", "p1")] = record
    store = SidecarStore(table)

    copied = await store.copy_record("col-1", "p1", "col-2", "p2")

    assert copied is True
    assert table.items[("col-2", "p2")] == record
    assert ("col-1", "p1") in table.items
    assert await store.get("col-2", "p2") == record


async def test_copy_record_reads_table_not_stale_cache():
    table = FakeSidecarTable()
    table.items[("col-1", "p1")] = SidecarRecord(card_name="Old")
    store = SidecarStore(table)
    await store.get("col-1", "p1")
    table.items[("col-1", "p1")] = SidecarRecord(card_name="New")

    await store.copy_record("col-1", "p1", "col-2", "p2")

    assert table.items[("col-2", "p2")].card_name == "New"


async def test_copy_record_without_source_sidecar_writes_nothing():
    table = FakeSidecarTable()
    store = SidecarStore(table)

    assert await store.copy_record("col-1", "p1", "col-2", "p2") is False
    assert table.items == {}
