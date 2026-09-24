"""In-memory cache mechanics for sidecar records, keyed by (collection_id, photo_id).

Pure cache: no DynamoDB access and no sidecar-domain logic (e.g. turning a CardAnalysisResult
into a SidecarRecord). SidecarStore (sidecar_store.py) is the entry point other code should
depend on - it composes this cache with SidecarTable and is where the write-through and
read-through behavior actually lives.
"""

from __future__ import annotations

from picture_service.models import SidecarRecord

MISSING = object()


class SidecarCache:
    def __init__(self) -> None:
        self._entries: dict[tuple[str, str], SidecarRecord | None] = {}

    def get(self, collection_id: str, photo_id: str) -> SidecarRecord | None | object:
        """Returns the cached value, or the MISSING sentinel when nothing is cached for this
        key - distinct from a cached `None`, which means "confirmed not to exist"."""
        return self._entries.get((collection_id, photo_id), MISSING)

    def set(self, collection_id: str, photo_id: str, record: SidecarRecord | None) -> None:
        self._entries[(collection_id, photo_id)] = record

    def set_if_absent(self, collection_id: str, photo_id: str, record: SidecarRecord | None) -> None:
        """Leaves an already-cached entry as-is rather than overwriting it - see
        SidecarStore.warm_from_store, which relies on this to preserve a value just written
        through the cache even if the backing store has since diverged out-of-band."""
        self._entries.setdefault((collection_id, photo_id), record)

    def remove(self, collection_id: str, photo_id: str) -> None:
        self._entries.pop((collection_id, photo_id), None)
