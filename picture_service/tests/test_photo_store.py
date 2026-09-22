import asyncio
import time
from collections.abc import AsyncIterator

import aioboto3
import pytest_asyncio

from picture_service.photo_store import PhotoStore, build_object_key

BUCKET = "test-photos-bucket"


@pytest_asyncio.fixture
async def photo_store(aws_session: aioboto3.Session, aws_endpoint_url: str) -> AsyncIterator[PhotoStore]:
    async with aws_session.client("s3", endpoint_url=aws_endpoint_url) as s3:
        await s3.create_bucket(Bucket=BUCKET)
        yield PhotoStore(s3, BUCKET)


def test_build_object_key_uses_photos_prefix_and_collection():
    assert build_object_key("col-1", "photo-1") == "photos/col-1/photo-1"


async def test_put_and_get_bytes_round_trip(photo_store: PhotoStore):
    await photo_store.put_bytes("col-1", "photo-1", b"hello world")

    data = await photo_store.get_bytes("col-1", "photo-1")

    assert data == b"hello world"


async def test_exists_true_after_put(photo_store: PhotoStore):
    await photo_store.put_bytes("col-1", "photo-1", b"data")

    assert await photo_store.exists("col-1", "photo-1") is True


async def test_exists_false_when_missing(photo_store: PhotoStore):
    assert await photo_store.exists("col-1", "does-not-exist") is False


async def test_delete_removes_object(photo_store: PhotoStore):
    await photo_store.put_bytes("col-1", "photo-1", b"data")

    await photo_store.delete("col-1", "photo-1")

    assert await photo_store.exists("col-1", "photo-1") is False


async def test_list_by_collection_returns_only_that_collections_ids(photo_store: PhotoStore):
    await photo_store.put_bytes("col-1", "photo-a", b"a")
    await photo_store.put_bytes("col-1", "photo-b", b"b")
    await photo_store.put_bytes("col-2", "photo-c", b"c")

    ids = sorted([photo_id async for photo_id in photo_store.list_photo_ids("col-1")])

    assert ids == ["photo-a", "photo-b"]


async def test_list_by_collection_empty_when_no_photos(photo_store: PhotoStore):
    ids = [photo_id async for photo_id in photo_store.list_photo_ids("empty-collection")]

    assert ids == []


async def test_create_download_url_points_at_object_key(photo_store: PhotoStore):
    await photo_store.put_bytes("col-1", "photo-1", b"data")

    url = await photo_store.create_download_url("col-1", "photo-1")

    assert "photos/col-1/photo-1" in url


class _StubPresignClient:
    """Mimics aiobotocore's `generate_presigned_url`: an async method that does no real
    async I/O and instead blocks synchronously (here via `time.sleep`, standing in for the
    real client's synchronous HMAC signing work) for the duration of the call."""

    def __init__(self, delay_seconds: float, url: str) -> None:
        self._delay_seconds = delay_seconds
        self._url = url

    async def generate_presigned_url(self, *args: object, **kwargs: object) -> str:
        time.sleep(self._delay_seconds)
        return self._url


async def test_create_download_url_returns_stub_result_unchanged():
    store = PhotoStore(_StubPresignClient(delay_seconds=0, url="https://example.com/signed"), BUCKET)

    url = await store.create_download_url("col-1", "photo-1")

    assert url == "https://example.com/signed"


async def test_create_download_url_does_not_block_the_event_loop():
    # 200ms of simulated signing work vs. a concurrent coroutine that only needs 10ms -
    # if the event loop were blocked for the duration of the presign call (as with a plain
    # `await self._s3.generate_presigned_url(...)`), the fast coroutine could not run until
    # the slow one finished, so it would still complete second.
    store = PhotoStore(_StubPresignClient(delay_seconds=0.2, url="https://example.com/signed"), BUCKET)
    completion_order: list[str] = []

    async def slow_presign() -> None:
        await store.create_download_url("col-1", "photo-1")
        completion_order.append("slow")

    async def fast_unrelated_work() -> None:
        await asyncio.sleep(0.01)
        completion_order.append("fast")

    await asyncio.gather(slow_presign(), fast_unrelated_work())

    assert completion_order == ["fast", "slow"]
