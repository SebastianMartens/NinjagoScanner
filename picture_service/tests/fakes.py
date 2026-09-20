"""In-memory fakes for the storage seams (SidecarTable/PhotoStore-shaped), used by unit tests
that don't need a real (mocked) AWS backend - mirrors NinjagoScanner.PictureService.Tests'
FakePhotoStore/FakeSidecarStore.
"""

from __future__ import annotations

from collections.abc import AsyncIterator

from picture_service.models import SidecarRecord


class FakeSidecarTable:
    def __init__(self) -> None:
        self.items: dict[tuple[str, str], SidecarRecord] = {}
        self.get_calls: list[tuple[str, str]] = []

    async def get(self, collection_id: str, photo_id: str) -> SidecarRecord | None:
        self.get_calls.append((collection_id, photo_id))
        return self.items.get((collection_id, photo_id))

    async def put(self, collection_id: str, photo_id: str, record: SidecarRecord) -> None:
        self.items[(collection_id, photo_id)] = record

    async def delete(self, collection_id: str, photo_id: str) -> None:
        self.items.pop((collection_id, photo_id), None)

    async def list_by_collection(self, collection_id: str) -> AsyncIterator[tuple[str, SidecarRecord]]:
        for (cid, pid), record in list(self.items.items()):
            if cid == collection_id:
                yield pid, record

    async def list_source_file_names(self, collection_id: str) -> AsyncIterator[str]:
        # Like the real table, yields one entry per record that has a name (not de-duplicated).
        for (cid, _), record in list(self.items.items()):
            if cid == collection_id and record.source_file_name:
                yield record.source_file_name

    async def list_all(self) -> AsyncIterator[tuple[str, str, SidecarRecord]]:
        for (cid, pid), record in list(self.items.items()):
            yield cid, pid, record


class FakePhotoStore:
    def __init__(self) -> None:
        self.bytes_by_key: dict[tuple[str, str], bytes] = {}
        self.calls: list[str] = []

    async def put_bytes(self, collection_id: str, photo_id: str, data: bytes) -> None:
        self.bytes_by_key[(collection_id, photo_id)] = data

    async def get_bytes(self, collection_id: str, photo_id: str) -> bytes:
        self.calls.append("get_bytes")
        return self.bytes_by_key[(collection_id, photo_id)]

    async def exists(self, collection_id: str, photo_id: str) -> bool:
        self.calls.append("exists")
        return (collection_id, photo_id) in self.bytes_by_key

    async def delete(self, collection_id: str, photo_id: str) -> None:
        self.bytes_by_key.pop((collection_id, photo_id), None)

    async def create_download_url(self, collection_id: str, photo_id: str) -> str:
        self.calls.append("create_download_url")
        return f"https://example.test/{collection_id}/{photo_id}"

    async def list_photo_ids(self, collection_id: str) -> AsyncIterator[str]:
        self.calls.append("list_photo_ids")
        for cid, pid in list(self.bytes_by_key.keys()):
            if cid == collection_id:
                yield pid
