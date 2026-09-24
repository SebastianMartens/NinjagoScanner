from picture_service.models import SidecarRecord
from picture_service.sidecar_store import SidecarStore
from tests.fakes import FakeSidecarTable


async def test_first_read_loads_from_table_and_caches():
    table = FakeSidecarTable()
    table.items[("col-1", "p1")] = SidecarRecord(analysis_status="ok")
    store = SidecarStore(table)

    result = await store.get("col-1", "p1")

    assert result.analysis_status == "ok"
    assert table.get_calls == [("col-1", "p1")]


async def test_repeated_reads_of_unchanged_sidecar_hit_cache_not_table():
    table = FakeSidecarTable()
    table.items[("col-1", "p1")] = SidecarRecord(analysis_status="ok")
    store = SidecarStore(table)

    await store.get("col-1", "p1")
    await store.get("col-1", "p1")
    await store.get("col-1", "p1")

    assert table.get_calls == [("col-1", "p1")]


async def test_read_immediately_following_write_returns_written_data():
    table = FakeSidecarTable()
    store = SidecarStore(table)

    await store.set_record("col-1", "p1", SidecarRecord(analysis_status="ok", card_name="Kai"))
    result = await store.get("col-1", "p1")

    assert result.card_name == "Kai"
    assert table.get_calls == []  # served from cache, no table read needed


async def test_second_write_overrides_first_on_subsequent_read():
    table = FakeSidecarTable()
    store = SidecarStore(table)

    await store.set_record("col-1", "p1", SidecarRecord(analysis_status="ok", card_name="Kai"))
    await store.set_record("col-1", "p1", SidecarRecord(analysis_status="ok", card_name="Jay"))

    result = await store.get("col-1", "p1")
    assert result.card_name == "Jay"


async def test_same_photo_id_in_two_collections_cached_independently():
    table = FakeSidecarTable()
    store = SidecarStore(table)

    await store.set_record("col-a", "p1", SidecarRecord(card_name="Kai"))
    await store.set_record("col-b", "p1", SidecarRecord(card_name="Jay"))

    assert (await store.get("col-a", "p1")).card_name == "Kai"
    assert (await store.get("col-b", "p1")).card_name == "Jay"


async def test_remove_deletes_from_table_and_evicts_cache_entry():
    table = FakeSidecarTable()
    store = SidecarStore(table)
    await store.set_record("col-1", "p1", SidecarRecord(card_name="Kai"))

    await store.remove("col-1", "p1")

    assert await store.get("col-1", "p1") is None
    assert ("col-1", "p1") not in table.items


async def test_warm_from_store_does_not_overwrite_already_cached_entry():
    table = FakeSidecarTable()
    table.items[("col-1", "p1")] = SidecarRecord(card_name="TableValue")
    store = SidecarStore(table)
    # A write-through write puts a value in the cache that hasn't reached the table's read
    # path yet in this fake, simulating a value diverged from the table out-of-band.
    store._cache.set("col-1", "p1", SidecarRecord(card_name="CachedValue"))

    await store.warm_from_store("col-1")
    result = await store.get("col-1", "p1")

    assert result.card_name == "CachedValue"


async def test_warm_from_store_fills_uncached_entries():
    table = FakeSidecarTable()
    table.items[("col-1", "p1")] = SidecarRecord(card_name="TableValue")
    store = SidecarStore(table)

    await store.warm_from_store("col-1")
    result = await store.get("col-1", "p1")

    assert result.card_name == "TableValue"
    assert table.get_calls == []  # served from the warm-filled cache, no extra get() call


async def test_list_by_collection_populates_cache_and_overwrites_existing_entries():
    table = FakeSidecarTable()
    table.items[("col-1", "p1")] = SidecarRecord(card_name="Fresh")
    store = SidecarStore(table)
    store._cache.set("col-1", "p1", SidecarRecord(card_name="Stale"))

    entries = [entry async for entry in store.list_by_collection("col-1")]

    assert entries == [("p1", SidecarRecord(card_name="Fresh"))]
    assert (await store.get("col-1", "p1")).card_name == "Fresh"


async def test_list_all_spans_every_collection_and_populates_cache():
    table = FakeSidecarTable()
    table.items[("col-a", "p1")] = SidecarRecord(card_name="A")
    table.items[("col-b", "p2")] = SidecarRecord(card_name="B")
    store = SidecarStore(table)

    entries = sorted(
        [(cid, pid) async for cid, pid, _ in store.list_all()]
    )

    assert entries == [("col-a", "p1"), ("col-b", "p2")]
