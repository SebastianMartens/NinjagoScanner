from picture_service.models import SidecarRecord
from picture_service.sidecar_cache import MISSING, SidecarCache


def test_get_on_empty_cache_returns_missing_sentinel():
    cache = SidecarCache()

    assert cache.get("col-1", "p1") is MISSING


def test_set_then_get_returns_the_stored_value():
    cache = SidecarCache()

    cache.set("col-1", "p1", SidecarRecord(card_name="Kai"))

    assert cache.get("col-1", "p1").card_name == "Kai"


def test_set_can_store_none_distinct_from_missing():
    cache = SidecarCache()

    cache.set("col-1", "p1", None)

    assert cache.get("col-1", "p1") is None


def test_second_set_overwrites_the_first():
    cache = SidecarCache()

    cache.set("col-1", "p1", SidecarRecord(card_name="Kai"))
    cache.set("col-1", "p1", SidecarRecord(card_name="Jay"))

    assert cache.get("col-1", "p1").card_name == "Jay"


def test_same_photo_id_in_two_collections_is_cached_independently():
    cache = SidecarCache()

    cache.set("col-a", "p1", SidecarRecord(card_name="Kai"))
    cache.set("col-b", "p1", SidecarRecord(card_name="Jay"))

    assert cache.get("col-a", "p1").card_name == "Kai"
    assert cache.get("col-b", "p1").card_name == "Jay"


def test_set_if_absent_fills_an_uncached_entry():
    cache = SidecarCache()

    cache.set_if_absent("col-1", "p1", SidecarRecord(card_name="Kai"))

    assert cache.get("col-1", "p1").card_name == "Kai"


def test_set_if_absent_leaves_an_already_cached_entry_untouched():
    cache = SidecarCache()
    cache.set("col-1", "p1", SidecarRecord(card_name="Cached"))

    cache.set_if_absent("col-1", "p1", SidecarRecord(card_name="FromStore"))

    assert cache.get("col-1", "p1").card_name == "Cached"


def test_remove_evicts_a_cached_entry():
    cache = SidecarCache()
    cache.set("col-1", "p1", SidecarRecord(card_name="Kai"))

    cache.remove("col-1", "p1")

    assert cache.get("col-1", "p1") is MISSING


def test_remove_on_missing_entry_is_a_no_op():
    cache = SidecarCache()

    cache.remove("col-1", "p1")

    assert cache.get("col-1", "p1") is MISSING
