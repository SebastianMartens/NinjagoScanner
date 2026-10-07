"""In-memory fakes for the storage seams (SidecarTable/PhotoStore-shaped), used by unit tests
that don't need a real (mocked) AWS backend - mirrors NinjagoScanner.PictureService.Tests'
FakePhotoStore/FakeSidecarStore.
"""

from __future__ import annotations

import copy
from collections.abc import AsyncIterator

from picture_service.models import SidecarRecord


class FakeSidecarTable:
    def __init__(self) -> None:
        self.items: dict[tuple[str, str], SidecarRecord] = {}
        self.get_calls: list[tuple[str, str]] = []
        self.transfers: dict[str, dict] = {}

    async def get(self, collection_id: str, photo_id: str) -> SidecarRecord | None:
        self.get_calls.append((collection_id, photo_id))
        return self.items.get((collection_id, photo_id))

    async def put(self, collection_id: str, photo_id: str, record: SidecarRecord) -> None:
        self.items[(collection_id, photo_id)] = record

    async def delete(self, collection_id: str, photo_id: str) -> None:
        self.items.pop((collection_id, photo_id), None)

    async def get_transfer(self, transfer_id: str) -> dict | None:
        record = self.transfers.get(transfer_id)
        return None if record is None else copy.deepcopy(record)

    async def create_transfer(self, transfer_id: str, record: dict) -> bool:
        if transfer_id in self.transfers:
            return False
        self.transfers[transfer_id] = copy.deepcopy(record)
        return True

    def _transfer_condition_holds(self, transfer_id, expected_lease_expires_at_ms, expected_status) -> bool:
        current = self.transfers.get(transfer_id)
        if expected_lease_expires_at_ms is None and expected_status is None:
            return True
        if current is None:
            return False
        if expected_lease_expires_at_ms is not None and current["lease_expires_at_ms"] != expected_lease_expires_at_ms:
            return False
        return expected_status is None or current["status"] == expected_status

    def _transfer_condition_holds(self, transfer_id, expected_lease_expires_at_ms, expected_status) -> bool:
        current = self.transfers.get(transfer_id)
        if expected_lease_expires_at_ms is None and expected_status is None:
            return True
        if current is None:
            return False
        if expected_lease_expires_at_ms is not None and current["lease_expires_at_ms"] != expected_lease_expires_at_ms:
            return False
        return expected_status is None or current["status"] == expected_status

    async def replace_transfer(
        self,
        transfer_id: str,
        record: dict,
        *,
        expected_lease_expires_at_ms: int | None = None,
        expected_status: str | None = None,
    ) -> bool:
        if not self._transfer_condition_holds(transfer_id, expected_lease_expires_at_ms, expected_status):
            return False
        self.transfers[transfer_id] = copy.deepcopy(record)
        return True

    async def delete_transfer(
        self,
        transfer_id: str,
        *,
        expected_lease_expires_at_ms: int | None = None,
        expected_status: str | None = None,
    ) -> bool:
        if not self._transfer_condition_holds(transfer_id, expected_lease_expires_at_ms, expected_status):
            return False
        return self.transfers.pop(transfer_id, None) is not None

    async def list_transfers(self) -> AsyncIterator[tuple[str, dict]]:
        for transfer_id, record in list(self.transfers.items()):
            yield transfer_id, copy.deepcopy(record)

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

    async def copy(
        self, source_collection_id: str, source_photo_id: str, dest_collection_id: str, dest_photo_id: str
    ) -> None:
        self.calls.append("copy")
        self.bytes_by_key[(dest_collection_id, dest_photo_id)] = self.bytes_by_key[
            (source_collection_id, source_photo_id)
        ]

    async def create_download_url(self, collection_id: str, photo_id: str) -> str:
        self.calls.append("create_download_url")
        return f"https://example.test/{collection_id}/{photo_id}"

    async def list_photo_ids(self, collection_id: str) -> AsyncIterator[str]:
        self.calls.append("list_photo_ids")
        for cid, pid in list(self.bytes_by_key.keys()):
            if cid == collection_id:
                yield pid
