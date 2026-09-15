from picture_service.models import SidecarRecord
from picture_service.sidecar_cache import SidecarCache
from tests.fakes import FakeSidecarTable


async def test_first_read_loads_from_store_and_caches():
    table = FakeSidecarTable()
    table.items[("col-1", "p1")] = SidecarRecord(analysis_status="ok")
    cache = SidecarCache(table)

    result = await cache.get("col-1", "p1")

    assert result.analysis_status == "ok"
    assert table.get_calls == [("col-1", "p1")]


async def test_repeated_reads_of_unchanged_sidecar_hit_cache_not_store():
    table = FakeSidecarTable()
    table.items[("col-1", "p1")] = SidecarRecord(analysis_status="ok")
    cache = SidecarCache(table)

    await cache.get("col-1", "p1")
    await cache.get("col-1", "p1")
    await cache.get("col-1", "p1")

    assert table.get_calls == [("col-1", "p1")]


async def test_read_immediately_following_write_returns_written_data():
    table = FakeSidecarTable()
    cache = SidecarCache(table)

    await cache.set_record("col-1", "p1", SidecarRecord(analysis_status="ok", card_name="Kai"))
    result = await cache.get("col-1", "p1")

    assert result.card_name == "Kai"
    assert table.get_calls == []  # served from cache, no store read needed


async def test_second_write_overrides_first_on_subsequent_read():
    table = FakeSidecarTable()
    cache = SidecarCache(table)

    await cache.set_record("col-1", "p1", SidecarRecord(analysis_status="ok", card_name="Kai"))
    await cache.set_record("col-1", "p1", SidecarRecord(analysis_status="ok", card_name="Jay"))

    result = await cache.get("col-1", "p1")
    assert result.card_name == "Jay"


async def test_same_photo_id_in_two_collections_cached_independently():
    table = FakeSidecarTable()
    cache = SidecarCache(table)

    await cache.set_record("col-a", "p1", SidecarRecord(card_name="Kai"))
    await cache.set_record("col-b", "p1", SidecarRecord(card_name="Jay"))

    assert (await cache.get("col-a", "p1")).card_name == "Kai"
    assert (await cache.get("col-b", "p1")).card_name == "Jay"


async def test_remove_deletes_from_store_and_evicts_cache_entry():
    table = FakeSidecarTable()
    cache = SidecarCache(table)
    await cache.set_record("col-1", "p1", SidecarRecord(card_name="Kai"))

    await cache.remove("col-1", "p1")

    assert await cache.get("col-1", "p1") is None
    assert ("col-1", "p1") not in table.items


async def test_warm_from_store_does_not_overwrite_already_cached_entry():
    table = FakeSidecarTable()
    table.items[("col-1", "p1")] = SidecarRecord(card_name="StoreValue")
    cache = SidecarCache(table)
    # A write-through write puts a value in the cache that hasn't reached the store's read
    # path yet in this fake, simulating a value diverged from the store out-of-band.
    cache._entries[("col-1", "p1")] = SidecarRecord(card_name="CachedValue")

    await cache.warm_from_store("col-1")
    result = await cache.get("col-1", "p1")

    assert result.card_name == "CachedValue"


async def test_warm_from_store_fills_uncached_entries():
    table = FakeSidecarTable()
    table.items[("col-1", "p1")] = SidecarRecord(card_name="StoreValue")
    cache = SidecarCache(table)

    await cache.warm_from_store("col-1")
    result = await cache.get("col-1", "p1")

    assert result.card_name == "StoreValue"
    assert table.get_calls == []  # served from the warm-filled cache, no extra get() call


async def test_list_by_collection_populates_cache_and_overwrites_existing_entries():
    table = FakeSidecarTable()
    table.items[("col-1", "p1")] = SidecarRecord(card_name="Fresh")
    cache = SidecarCache(table)
    cache._entries[("col-1", "p1")] = SidecarRecord(card_name="Stale")

    entries = [entry async for entry in cache.list_by_collection("col-1")]

    assert entries == [("p1", SidecarRecord(card_name="Fresh"))]
    assert (await cache.get("col-1", "p1")).card_name == "Fresh"


async def test_list_all_spans_every_collection_and_populates_cache():
    table = FakeSidecarTable()
    table.items[("col-a", "p1")] = SidecarRecord(card_name="A")
    table.items[("col-b", "p2")] = SidecarRecord(card_name="B")
    cache = SidecarCache(table)

    entries = sorted(
        [(cid, pid) async for cid, pid, _ in cache.list_all()]
    )

    assert entries == [("col-a", "p1"), ("col-b", "p2")]
