import aioboto3
import pytest_asyncio

from picture_service.photo_store import PhotoStore, build_object_key

BUCKET = "test-photos-bucket"


@pytest_asyncio.fixture
async def photo_store(aws_session: aioboto3.Session, aws_endpoint_url: str) -> PhotoStore:
    async with aws_session.client("s3", endpoint_url=aws_endpoint_url) as s3:
        await s3.create_bucket(Bucket=BUCKET)
    return PhotoStore(aws_session, BUCKET, endpoint_url=aws_endpoint_url)


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
