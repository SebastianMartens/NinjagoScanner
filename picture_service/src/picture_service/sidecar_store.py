"""DynamoDB-backed sidecar record repository, ported from SidecarCache.cs.

The entry point other code should depend on for sidecar records - mirrors PhotoStore's role for
photo bytes (see photo_store.py). Composes SidecarTable (raw DynamoDB item CRUD, see
sidecar_table.py) with an in-memory write-through SidecarCache (pure cache mechanics, see
sidecar_cache.py) so callers avoid re-reading unchanged records from DynamoDB on every request.
Every successful write updates the cache with the value that was just persisted. Read failures
are never cached, so they are retried on next read.
"""

from __future__ import annotations

from collections.abc import AsyncIterator

from picture_service.models import CardAnalysisResult, SidecarRecord
from picture_service.sidecar_cache import MISSING, SidecarCache
from picture_service.sidecar_table import SidecarTable


class SidecarStore:
    def __init__(self, sidecar_table: SidecarTable) -> None:
        self._sidecar_table = sidecar_table
        self._cache = SidecarCache()

    async def get(self, collection_id: str, photo_id: str) -> SidecarRecord | None:
        cached = self._cache.get(collection_id, photo_id)
        if cached is not MISSING:
            return cached

        record = await self._sidecar_table.get(collection_id, photo_id)
        self._cache.set(collection_id, photo_id, record)
        return record

    async def set_record(self, collection_id: str, photo_id: str, record: SidecarRecord) -> None:
        await self._sidecar_table.put(collection_id, photo_id, record)
        self._cache.set(collection_id, photo_id, record)

    async def set_from_analysis_result(
        self, collection_id: str, photo_id: str, result: CardAnalysisResult
    ) -> None:
        await self.set_record(collection_id, photo_id, SidecarRecord.from_analysis_result(result))

    async def remove(self, collection_id: str, photo_id: str) -> None:
        await self._sidecar_table.delete(collection_id, photo_id)
        self._cache.remove(collection_id, photo_id)

    async def list_by_collection(self, collection_id: str) -> AsyncIterator[tuple[str, SidecarRecord]]:
        """Enumerates every sidecar record within one collection, populating the cache along
        the way. Used by the bulk Scan RPC, where the table is authoritative for that
        collection and should overwrite whatever is cached."""
        async for photo_id, record in self._sidecar_table.list_by_collection(collection_id):
            self._cache.set(collection_id, photo_id, record)
            yield photo_id, record

    async def warm_from_store(self, collection_id: str) -> None:
        """Bulk-fills any not-yet-cached sidecar record for one collection from DynamoDB in a
        bounded, small number of requests (a paginated query), instead of leaving every
        uncached photo to be read one at a time. Unlike list_by_collection, an already-cached
        entry is left as-is rather than overwritten, so a value just written through this store
        (see set_record/set_from_analysis_result) stays visible even if DynamoDB has since
        diverged out-of-band - used by ListCards, which reads through get() afterward and must
        keep that read-your-own-writes guarantee."""
        async for photo_id, record in self._sidecar_table.list_by_collection(collection_id):
            self._cache.set_if_absent(collection_id, photo_id, record)

    async def list_source_file_names(self, collection_id: str) -> AsyncIterator[str]:
        """Passes straight through to the table's names-only query: it neither reads nor fills
        the cache, since a name listing doesn't need full records (and DynamoDB is
        authoritative - every write goes through set_record, which persists synchronously)."""
        async for name in self._sidecar_table.list_source_file_names(collection_id):
            yield name

    async def list_all(self) -> AsyncIterator[tuple[str, str, SidecarRecord]]:
        """Enumerates every sidecar record across every collection, populating the cache along
        the way. Used only by the global MigrateSidecars maintenance RPC (see
        collection-scoped-picture-access's deliberate exception for it)."""
        async for collection_id, photo_id, record in self._sidecar_table.list_all():
            self._cache.set(collection_id, photo_id, record)
            yield collection_id, photo_id, record
