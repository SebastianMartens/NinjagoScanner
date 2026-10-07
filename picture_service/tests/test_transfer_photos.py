import asyncio

import grpc
import pytest

from picture_service._generated import picture_service_pb2 as pb2
from picture_service.models import ReviewStatuses, SidecarRecord
from tests.fakes import FakePhotoStore, FakeSidecarTable
from tests.grpc_fakes import AbortCalled, FakeServicerContext
from tests.test_picture_scanner_service import make_service

SRC = "col-a"
DST = "col-b"


class FailingCopyPhotoStore(FakePhotoStore):
    """Fails the Nth copy call (1-based), like S3 erroring partway through a transfer."""

    def __init__(self, fail_on_copy: int) -> None:
        super().__init__()
        self._fail_on_copy = fail_on_copy
        self._copies = 0

    async def copy(self, *args, **kwargs) -> None:
        self._copies += 1
        if self._copies == self._fail_on_copy:
            raise RuntimeError("S3 copy failed")
        await super().copy(*args, **kwargs)


class FailingSidecarPutTable(FakeSidecarTable):
    def __init__(self, fail_on_collection: str) -> None:
        super().__init__()
        self._fail_on_collection = fail_on_collection

    async def put(self, collection_id, photo_id, record) -> None:
        if collection_id == self._fail_on_collection:
            raise RuntimeError("DynamoDB put failed")
        await super().put(collection_id, photo_id, record)


class FailingSourceDeletePhotoStore(FakePhotoStore):
    def __init__(self) -> None:
        super().__init__()
        self.fail_deletes = False

    async def delete(self, collection_id, photo_id) -> None:
        if self.fail_deletes and collection_id == SRC:
            raise RuntimeError("S3 delete failed")
        await super().delete(collection_id, photo_id)


class FailingTransferCommitTable(FakeSidecarTable):
    """The claim (create) works, but every later write of the transfer record fails."""

    async def replace_transfer(self, transfer_id, record, **kwargs) -> bool:
        if record["status"] == "committed":
            raise RuntimeError("record write failed")
        return await super().replace_transfer(transfer_id, record, **kwargs)


class AmbiguousCommitTable(FakeSidecarTable):
    """The commit is stored but the caller still sees an error (timeout after the write)."""

    async def replace_transfer(self, transfer_id, record, **kwargs) -> bool:
        stored = await super().replace_transfer(transfer_id, record, **kwargs)
        if stored and record["status"] == "committed" and not record["sources_removed"]:
            raise RuntimeError("timeout after write")
        return stored


class FlakySourceDeletePhotoStore(FakePhotoStore):
    """Fails the first `failures` source deletions, then recovers."""

    def __init__(self, failures: int) -> None:
        super().__init__()
        self.failures = failures

    async def delete(self, collection_id, photo_id) -> None:
        if collection_id == SRC and self.failures > 0:
            self.failures -= 1
            raise RuntimeError("S3 delete failed")
        await super().delete(collection_id, photo_id)


class FailingDestinationDeletePhotoStore(FailingCopyPhotoStore):
    """A copy fails AND the compensation cannot delete destination objects."""

    async def delete(self, collection_id, photo_id) -> None:
        if collection_id == DST:
            raise RuntimeError("S3 delete failed")
        await super().delete(collection_id, photo_id)


def seed(table, photo_store, collection, photo_id, record=None, data=b"img"):
    photo_store.bytes_by_key[(collection, photo_id)] = data
    if record is not None:
        table.items[(collection, photo_id)] = record


def request(transfer_id="t-1", moves=(("col-a", "col-b", "p1"),)):
    return pb2.TransferPhotosRequest(
        transfer_id=transfer_id,
        moves=[pb2.PhotoMove(source_collection_id=s, dest_collection_id=d, photo_id=p) for s, d, p in moves],
    )


async def transfer(service, req):
    return await service.TransferPhotos(req, FakeServicerContext())


async def transfer_aborts(service, req) -> grpc.StatusCode:
    with pytest.raises(AbortCalled) as exc_info:
        await transfer(service, req)
    return exc_info.value.code


FULL_RECORD = SidecarRecord(
    analysis_status="ok",
    review_status=ReviewStatuses.VERIFIED,
    card_name="Kai",
    card_number="1",
    set_name="Serie 1",
    language="de",
    confidence=0.9,
    source_file_name="kai.jpg",
    ai_model="gemini-x",
    detected={"a": 1.5},
    derived={"b": "x"},
    rotated_180=True,
)


def assert_nothing_moved(table, photo_store, ids=("p1",)):
    for photo_id in ids:
        assert (SRC, photo_id) in photo_store.bytes_by_key
    assert [k for k in photo_store.bytes_by_key if k[0] == DST] == []
    assert [k for k in table.items if k[0] == DST] == []


# --- Successful move ---


async def test_successful_move_creates_destination_copies_and_removes_sources():
    service, table, photo_store = make_service()
    seed(table, photo_store, SRC, "p1", FULL_RECORD, b"one")
    seed(table, photo_store, SRC, "p2", SidecarRecord(card_name="Lloyd"), b"two")

    response = await transfer(service, request(moves=[("col-a", "col-b", "p1"), ("col-a", "col-b", "p2")]))

    mapping = {r.old_photo_id: r for r in response.results}
    assert set(mapping) == {"p1", "p2"}
    for old, r in mapping.items():
        assert r.new_photo_id and r.new_photo_id != old
        assert (r.source_collection_id, r.dest_collection_id) == (SRC, DST)
        assert (SRC, old) not in photo_store.bytes_by_key
        assert (SRC, old) not in table.items
    assert photo_store.bytes_by_key[(DST, mapping["p1"].new_photo_id)] == b"one"
    assert photo_store.bytes_by_key[(DST, mapping["p2"].new_photo_id)] == b"two"
    assert table.items[(DST, mapping["p1"].new_photo_id)] == FULL_RECORD
    assert table.items[(DST, mapping["p2"].new_photo_id)].card_name == "Lloyd"


async def test_bidirectional_trade_is_one_call():
    service, table, photo_store = make_service()
    seed(table, photo_store, SRC, "p1", SidecarRecord(card_name="Kai"))
    seed(table, photo_store, DST, "q1", SidecarRecord(card_name="Jay"))

    response = await transfer(service, request(moves=[("col-a", "col-b", "p1"), ("col-b", "col-a", "q1")]))

    assert len(response.results) == 2
    new_in_dst = [pid for (cid, pid) in photo_store.bytes_by_key if cid == DST]
    new_in_src = [pid for (cid, pid) in photo_store.bytes_by_key if cid == SRC]
    assert len(new_in_dst) == 1 and len(new_in_src) == 1
    assert table.items[(DST, new_in_dst[0])].card_name == "Kai"
    assert table.items[(SRC, new_in_src[0])].card_name == "Jay"


async def test_photo_without_sidecar_moves_bytes_only():
    service, table, photo_store = make_service()
    seed(table, photo_store, SRC, "p1", None)

    response = await transfer(service, request())

    new_id = response.results[0].new_photo_id
    assert (DST, new_id) in photo_store.bytes_by_key
    assert (DST, new_id) not in table.items
    assert (SRC, "p1") not in photo_store.bytes_by_key


async def test_moves_do_not_touch_other_photos():
    service, table, photo_store = make_service()
    seed(table, photo_store, SRC, "p1", SidecarRecord(card_name="Kai"))
    seed(table, photo_store, SRC, "other", SidecarRecord(card_name="Cole"))

    await transfer(service, request())

    assert (SRC, "other") in photo_store.bytes_by_key
    assert table.items[(SRC, "other")].card_name == "Cole"


# --- Unknown photo ---


async def test_unknown_photo_is_not_found_and_nothing_moves():
    service, table, photo_store = make_service()
    seed(table, photo_store, SRC, "p1", SidecarRecord(card_name="Kai"))

    code = await transfer_aborts(service, request(moves=[("col-a", "col-b", "p1"), ("col-a", "col-b", "missing")]))

    assert code == grpc.StatusCode.NOT_FOUND
    assert_nothing_moved(table, photo_store)
    assert table.transfers == {}


async def test_photo_in_other_collection_is_not_found():
    service, table, photo_store = make_service()
    seed(table, photo_store, "col-c", "p1", SidecarRecord())

    assert await transfer_aborts(service, request()) == grpc.StatusCode.NOT_FOUND
    assert ("col-c", "p1") in photo_store.bytes_by_key


async def test_unknown_source_collection_is_not_found():
    service, _, _ = make_service()

    code = await transfer_aborts(service, request(moves=[("nonexistent", "col-b", "p1")]))

    assert code == grpc.StatusCode.NOT_FOUND


# --- Same collection / invalid input ---


async def test_same_collection_is_invalid_argument():
    service, table, photo_store = make_service()
    seed(table, photo_store, SRC, "p1", SidecarRecord())

    code = await transfer_aborts(service, request(moves=[("col-a", "col-a", "p1")]))

    assert code == grpc.StatusCode.INVALID_ARGUMENT
    assert list(photo_store.bytes_by_key) == [(SRC, "p1")]


@pytest.mark.parametrize(
    "moves",
    [[("", "col-b", "p1")], [("col-a", "", "p1")], [("  ", "col-b", "p1")], [("col-a", " ", "p1")]],
)
async def test_empty_collection_is_invalid_argument(moves):
    service, table, photo_store = make_service()
    seed(table, photo_store, SRC, "p1", SidecarRecord())

    assert await transfer_aborts(service, request(moves=moves)) == grpc.StatusCode.INVALID_ARGUMENT
    assert list(photo_store.bytes_by_key) == [(SRC, "p1")]


async def test_empty_photo_id_is_invalid_argument():
    service, _, _ = make_service()
    assert await transfer_aborts(service, request(moves=[("col-a", "col-b", "")])) == grpc.StatusCode.INVALID_ARGUMENT


async def test_empty_transfer_id_is_invalid_argument():
    service, _, _ = make_service()
    assert await transfer_aborts(service, request(transfer_id=" ")) == grpc.StatusCode.INVALID_ARGUMENT


async def test_no_moves_is_invalid_argument():
    service, _, _ = make_service()
    assert await transfer_aborts(service, request(moves=[])) == grpc.StatusCode.INVALID_ARGUMENT


async def test_duplicate_photo_in_moves_is_invalid_argument():
    service, table, photo_store = make_service()
    seed(table, photo_store, SRC, "p1", SidecarRecord())

    code = await transfer_aborts(service, request(moves=[("col-a", "col-b", "p1"), ("col-a", "col-c", "p1")]))

    assert code == grpc.StatusCode.INVALID_ARGUMENT
    assert list(photo_store.bytes_by_key) == [(SRC, "p1")]


async def test_reserved_transfer_prefix_collection_is_invalid_argument():
    service, _, _ = make_service()
    code = await transfer_aborts(service, request(moves=[("col-a", "TRANSFER#x", "p1")]))
    assert code == grpc.StatusCode.INVALID_ARGUMENT


async def test_validation_failure_in_a_later_move_moves_nothing():
    service, table, photo_store = make_service()
    seed(table, photo_store, SRC, "p1", SidecarRecord())

    code = await transfer_aborts(service, request(moves=[("col-a", "col-b", "p1"), ("col-b", "col-b", "p2")]))

    assert code == grpc.StatusCode.INVALID_ARGUMENT
    assert_nothing_moved(table, photo_store)


# --- All-or-nothing with compensation ---


async def test_failure_copying_second_of_three_removes_first_copy_and_keeps_all_sources():
    photo_store = FailingCopyPhotoStore(fail_on_copy=2)
    service, table, _ = make_service(photo_store=photo_store)
    for pid in ("p1", "p2", "p3"):
        seed(table, photo_store, SRC, pid, SidecarRecord(card_name=pid))

    code = await transfer_aborts(service, request(moves=[("col-a", "col-b", p) for p in ("p1", "p2", "p3")]))

    assert code == grpc.StatusCode.INTERNAL
    assert_nothing_moved(table, photo_store, ("p1", "p2", "p3"))
    assert set(table.items) == {(SRC, "p1"), (SRC, "p2"), (SRC, "p3")}
    assert table.transfers == {}


async def test_failure_writing_destination_sidecar_rolls_back_copies():
    table = FailingSidecarPutTable(fail_on_collection=DST)
    service, _, photo_store = make_service(sidecar_table=table)
    seed(table, photo_store, SRC, "p1", SidecarRecord(card_name="Kai"))

    code = await transfer_aborts(service, request())

    assert code == grpc.StatusCode.INTERNAL
    assert_nothing_moved(table, photo_store)


async def test_failure_in_bidirectional_transfer_leaves_both_sides_untouched():
    photo_store = FailingCopyPhotoStore(fail_on_copy=2)
    service, table, _ = make_service(photo_store=photo_store)
    seed(table, photo_store, SRC, "p1", SidecarRecord(card_name="Kai"))
    seed(table, photo_store, DST, "q1", SidecarRecord(card_name="Jay"))

    code = await transfer_aborts(service, request(moves=[("col-a", "col-b", "p1"), ("col-b", "col-a", "q1")]))

    assert code == grpc.StatusCode.INTERNAL
    assert set(photo_store.bytes_by_key) == {(SRC, "p1"), (DST, "q1")}
    assert set(table.items) == {(SRC, "p1"), (DST, "q1")}


async def test_failed_transfer_can_be_retried_with_same_id_after_cause_is_fixed():
    photo_store = FailingCopyPhotoStore(fail_on_copy=1)
    service, table, _ = make_service(photo_store=photo_store)
    seed(table, photo_store, SRC, "p1", SidecarRecord(card_name="Kai"))
    await transfer_aborts(service, request())

    response = await transfer(service, request())

    assert (SRC, "p1") not in photo_store.bytes_by_key
    assert (DST, response.results[0].new_photo_id) in photo_store.bytes_by_key


async def test_failure_committing_transfer_rolls_back_before_any_source_removal():
    table = FailingTransferCommitTable()
    service, _, photo_store = make_service(sidecar_table=table)
    seed(table, photo_store, SRC, "p1", SidecarRecord(card_name="Kai"))

    code = await transfer_aborts(service, request())

    assert code == grpc.StatusCode.INTERNAL
    assert_nothing_moved(table, photo_store)
    assert table.transfers == {}


async def test_sources_are_removed_only_after_all_destination_writes():
    photo_store = FailingCopyPhotoStore(fail_on_copy=3)
    service, table, _ = make_service(photo_store=photo_store)
    for pid in ("p1", "p2", "p3"):
        seed(table, photo_store, SRC, pid, SidecarRecord())

    await transfer_aborts(service, request(moves=[("col-a", "col-b", p) for p in ("p1", "p2", "p3")]))

    assert {pid for (cid, pid) in photo_store.bytes_by_key if cid == SRC} == {"p1", "p2", "p3"}


async def test_persistent_source_removal_failure_still_succeeds_and_retry_finishes_removal():
    photo_store = FailingSourceDeletePhotoStore()
    service, table, _ = make_service(photo_store=photo_store)
    seed(table, photo_store, SRC, "p1", SidecarRecord(card_name="Kai"))
    photo_store.fail_deletes = True

    first = await transfer(service, request())  # does not abort: the transfer is committed

    new_id = first.results[0].new_photo_id
    assert table.transfers["t-1"]["status"] == "committed"
    assert table.transfers["t-1"]["sources_removed"] is False
    assert (DST, new_id) in photo_store.bytes_by_key  # destination kept, never rolled back
    assert (SRC, "p1") in photo_store.bytes_by_key  # transient second copy, to be cleaned up

    photo_store.fail_deletes = False
    second = await transfer(service, request())

    assert list(second.results) == list(first.results)
    assert (SRC, "p1") not in photo_store.bytes_by_key
    assert (SRC, "p1") not in table.items
    assert [k for k in photo_store.bytes_by_key if k[0] == DST] == [(DST, new_id)]
    assert table.transfers["t-1"]["sources_removed"] is True


async def test_transient_source_removal_failure_is_retried_inside_the_call():
    photo_store = FlakySourceDeletePhotoStore(failures=2)
    service, table, _ = make_service(photo_store=photo_store, source_removal_backoff_seconds=(0, 0, 0))
    seed(table, photo_store, SRC, "p1", SidecarRecord(card_name="Kai"))

    response = await transfer(service, request())

    assert (SRC, "p1") not in photo_store.bytes_by_key
    assert (SRC, "p1") not in table.items
    assert [k for k in photo_store.bytes_by_key if k[0] == DST] == [(DST, response.results[0].new_photo_id)]
    assert table.transfers["t-1"]["sources_removed"] is True


async def test_source_removal_backoff_sleeps_between_attempts(monkeypatch):
    sleeps = []

    async def fake_sleep(seconds):
        sleeps.append(seconds)

    monkeypatch.setattr(asyncio, "sleep", fake_sleep)
    photo_store = FailingSourceDeletePhotoStore()
    service, table, _ = make_service(photo_store=photo_store, source_removal_backoff_seconds=(0.1, 0.5))
    seed(table, photo_store, SRC, "p1", SidecarRecord())
    photo_store.fail_deletes = True

    await transfer(service, request())

    assert sleeps == [0.1, 0.5]


async def test_successful_transfer_marks_sources_removed():
    service, table, photo_store = make_service()
    seed(table, photo_store, SRC, "p1", SidecarRecord())

    await transfer(service, request())

    assert table.transfers["t-1"]["status"] == "committed"
    assert table.transfers["t-1"]["sources_removed"] is True


async def test_ambiguous_commit_failure_never_rolls_back_a_committed_transfer():
    table = AmbiguousCommitTable()
    service, _, photo_store = make_service(sidecar_table=table)
    seed(table, photo_store, SRC, "p1", SidecarRecord(card_name="Kai"))

    code = await transfer_aborts(service, request())

    assert code == grpc.StatusCode.INTERNAL
    new_id = table.transfers["t-1"]["results"][0]["new_photo_id"]
    assert (DST, new_id) in photo_store.bytes_by_key  # committed destination is live data
    assert table.items[(DST, new_id)].card_name == "Kai"

    response = await transfer(service, request())  # retry finishes, no duplicate

    assert response.results[0].new_photo_id == new_id
    assert (SRC, "p1") not in photo_store.bytes_by_key
    assert [k for k in photo_store.bytes_by_key if k[0] == DST] == [(DST, new_id)]


async def test_failed_rollback_leaves_resumable_record_and_retry_leaves_no_orphans():
    photo_store = FailingDestinationDeletePhotoStore(fail_on_copy=2)
    service, table, _ = make_service(photo_store=photo_store)
    seed(table, photo_store, SRC, "p1", SidecarRecord(card_name="Kai"))
    seed(table, photo_store, SRC, "p2", SidecarRecord(card_name="Lloyd"))
    moves = [("col-a", "col-b", "p1"), ("col-a", "col-b", "p2")]

    code = await transfer_aborts(service, request(moves=moves))

    assert code == grpc.StatusCode.INTERNAL
    record = table.transfers["t-1"]
    assert record["status"] == "pending" and record["lease_expires_at_ms"] == 0
    recorded_ids = {r["new_photo_id"] for r in record["results"]}
    assert {pid for (cid, pid) in photo_store.bytes_by_key if cid == DST} <= recorded_ids  # no unknown orphans

    photo_store._fail_on_copy = -1  # copies work again
    response = await transfer(service, request(moves=moves))

    assert {r.new_photo_id for r in response.results} == recorded_ids
    assert {pid for (cid, pid) in photo_store.bytes_by_key if cid == DST} == recorded_ids
    assert {pid for (cid, pid) in photo_store.bytes_by_key if cid == SRC} == set()


# --- Idempotency ---


async def test_idempotent_retry_returns_original_mapping_without_moving_again():
    service, table, photo_store = make_service()
    seed(table, photo_store, SRC, "p1", SidecarRecord(card_name="Kai"))

    first = await transfer(service, request())
    snapshot = (dict(photo_store.bytes_by_key), dict(table.items))
    copies_before = photo_store.calls.count("copy")
    second = await transfer(service, request())

    assert list(second.results) == list(first.results)
    assert (dict(photo_store.bytes_by_key), dict(table.items)) == snapshot
    assert photo_store.calls.count("copy") == copies_before


async def test_retry_after_success_does_not_fail_even_though_source_is_gone():
    service, table, photo_store = make_service()
    seed(table, photo_store, SRC, "p1", SidecarRecord())
    await transfer(service, request())

    response = await transfer(service, request())

    assert len(response.results) == 1


async def test_replay_does_not_remove_the_moved_photo_the_new_owner_traded_onward():
    service, table, photo_store = make_service()
    seed(table, photo_store, SRC, "p1", SidecarRecord(card_name="Kai"))
    first = await transfer(service, request())
    new_id = first.results[0].new_photo_id
    await transfer(service, request("t-2", moves=[("col-b", "col-c", new_id)]))

    await transfer(service, request())  # replay of t-1

    assert [k for k in photo_store.bytes_by_key if k[0] == "col-c"] != []


async def test_different_transfer_ids_are_independent():
    service, table, photo_store = make_service()
    seed(table, photo_store, SRC, "p1", SidecarRecord())
    seed(table, photo_store, SRC, "p2", SidecarRecord())

    r1 = await transfer(service, request("t-1", [("col-a", "col-b", "p1")]))
    r2 = await transfer(service, request("t-2", [("col-a", "col-b", "p2")]))

    assert r1.results[0].new_photo_id != r2.results[0].new_photo_id
    assert len([k for k in photo_store.bytes_by_key if k[0] == DST]) == 2


async def test_concurrent_calls_with_same_transfer_id_move_once():
    service, table, photo_store = make_service()
    seed(table, photo_store, SRC, "p1", SidecarRecord())

    a, b = await asyncio.gather(transfer(service, request()), transfer(service, request()))

    assert list(a.results) == list(b.results)
    assert len([k for k in photo_store.bytes_by_key if k[0] == DST]) == 1


# --- Caches stay consistent ---


async def test_list_cards_for_both_collections_reflects_transfer_immediately():
    service, table, photo_store = make_service()
    seed(table, photo_store, SRC, "p1", SidecarRecord(card_name="Kai", analysis_status="ok"))
    seed(table, photo_store, DST, "q1", SidecarRecord(card_name="Jay", analysis_status="ok"))
    ctx = FakeServicerContext()
    await service.ListCards(pb2.ListCardsRequest(collection_id=SRC), ctx)
    await service.ListCards(pb2.ListCardsRequest(collection_id=DST), ctx)

    await transfer(service, request(moves=[("col-a", "col-b", "p1")]))

    src_cards = (await service.ListCards(pb2.ListCardsRequest(collection_id=SRC), ctx)).cards
    dst_cards = (await service.ListCards(pb2.ListCardsRequest(collection_id=DST), ctx)).cards
    assert list(src_cards) == []
    assert sorted(c.card_name for c in dst_cards) == ["Jay", "Kai"]
    assert "p1" not in {c.photo_id for c in dst_cards}


async def test_failed_transfer_leaves_caches_consistent():
    photo_store = FailingCopyPhotoStore(fail_on_copy=2)
    service, table, _ = make_service(photo_store=photo_store)
    seed(table, photo_store, SRC, "p1", SidecarRecord(card_name="Kai", analysis_status="ok"))
    seed(table, photo_store, SRC, "p2", SidecarRecord(card_name="Lloyd", analysis_status="ok"))
    ctx = FakeServicerContext()
    await service.ListCards(pb2.ListCardsRequest(collection_id=SRC), ctx)

    await transfer_aborts(service, request(moves=[("col-a", "col-b", "p1"), ("col-a", "col-b", "p2")]))

    src_cards = (await service.ListCards(pb2.ListCardsRequest(collection_id=SRC), ctx)).cards
    dst_cards = (await service.ListCards(pb2.ListCardsRequest(collection_id=DST), ctx)).cards
    assert sorted(c.card_name for c in src_cards) == ["Kai", "Lloyd"]
    assert list(dst_cards) == []


# --- Crash recovery, cross-machine safety, payload check, validation ---


def pending_record(moves, new_ids, lease_expires_at_ms, sources_removed=False, status="pending"):
    return {
        "status": status,
        "results": [
            {"old_photo_id": p, "new_photo_id": n, "source_collection_id": s, "dest_collection_id": d}
            for (s, d, p), n in zip(moves, new_ids)
        ],
        "lease_expires_at_ms": lease_expires_at_ms,
        "sources_removed": sources_removed,
    }


async def test_crash_after_claim_and_partial_copy_is_resumed_with_the_recorded_ids():
    service, table, photo_store = make_service(now_ms=lambda: 1_000_000)
    seed(table, photo_store, SRC, "p1", SidecarRecord(card_name="Kai"), b"one")
    seed(table, photo_store, SRC, "p2", SidecarRecord(card_name="Lloyd"), b"two")
    moves = [("col-a", "col-b", "p1"), ("col-a", "col-b", "p2")]
    # The crashed attempt claimed the transfer and copied the first photo, then died.
    table.transfers["t-1"] = pending_record(moves, ["n1", "n2"], lease_expires_at_ms=999_999)
    photo_store.bytes_by_key[(DST, "n1")] = b"one"

    response = await transfer(service, request(moves=moves))

    assert [(r.old_photo_id, r.new_photo_id) for r in response.results] == [("p1", "n1"), ("p2", "n2")]
    assert {pid for (cid, pid) in photo_store.bytes_by_key if cid == DST} == {"n1", "n2"}
    assert table.items[(DST, "n2")].card_name == "Lloyd"
    assert [k for k in photo_store.bytes_by_key if k[0] == SRC] == []
    assert table.transfers["t-1"]["status"] == "committed"


async def test_pending_transfer_with_live_lease_is_not_touched_by_another_attempt():
    service, table, photo_store = make_service(now_ms=lambda: 1_000_000)
    seed(table, photo_store, SRC, "p1", SidecarRecord())
    table.transfers["t-1"] = pending_record([("col-a", "col-b", "p1")], ["n1"], lease_expires_at_ms=1_030_000)

    code = await transfer_aborts(service, request())

    assert code == grpc.StatusCode.ABORTED
    assert_nothing_moved(table, photo_store)
    assert table.transfers["t-1"]["lease_expires_at_ms"] == 1_030_000


async def test_losing_the_lease_takeover_race_aborts_without_touching_anything():
    class LosingTakeoverTable(FakeSidecarTable):
        async def replace_transfer(self, transfer_id, record, *, expected_lease_expires_at_ms=None, **kwargs) -> bool:
            if expected_lease_expires_at_ms is not None:
                return False
            return await super().replace_transfer(transfer_id, record, **kwargs)

    table = LosingTakeoverTable()
    service, _, photo_store = make_service(sidecar_table=table, now_ms=lambda: 1_000_000)
    seed(table, photo_store, SRC, "p1", SidecarRecord())
    table.transfers["t-1"] = pending_record([("col-a", "col-b", "p1")], ["n1"], lease_expires_at_ms=10)

    assert await transfer_aborts(service, request()) == grpc.StatusCode.ABORTED
    assert_nothing_moved(table, photo_store)


async def test_losing_the_claim_race_to_a_committed_attempt_returns_its_mapping():
    class OtherMachineWinsTable(FakeSidecarTable):
        async def create_transfer(self, transfer_id, record) -> bool:
            # Between our read and our claim another machine completed this very transfer.
            self.transfers[transfer_id] = {
                **record,
                "status": "committed",
                "sources_removed": True,
                "results": [{**r, "new_photo_id": "other-machine-id"} for r in record["results"]],
            }
            return False

    table = OtherMachineWinsTable()
    service, _, photo_store = make_service(sidecar_table=table)
    seed(table, photo_store, SRC, "p1", SidecarRecord())

    response = await transfer(service, request())

    assert response.results[0].new_photo_id == "other-machine-id"
    assert [k for k in photo_store.bytes_by_key if k[0] == DST] == []  # this attempt moved nothing


async def test_resume_with_source_deleted_meanwhile_is_not_found_and_cleans_up():
    service, table, photo_store = make_service(now_ms=lambda: 1_000_000)
    table.transfers["t-1"] = pending_record([("col-a", "col-b", "p1")], ["n1"], lease_expires_at_ms=5)
    photo_store.bytes_by_key[(DST, "n1")] = b"orphan"

    code = await transfer_aborts(service, request())

    assert code == grpc.StatusCode.NOT_FOUND
    assert (DST, "n1") not in photo_store.bytes_by_key
    assert table.transfers == {}


@pytest.mark.parametrize("status", ["pending", "committed"])
async def test_same_transfer_id_with_different_moves_is_rejected_without_side_effects(status):
    service, table, photo_store = make_service(now_ms=lambda: 1_000_000)
    seed(table, photo_store, SRC, "p1", SidecarRecord())
    seed(table, photo_store, SRC, "p2", SidecarRecord())
    table.transfers["t-1"] = pending_record(
        [("col-a", "col-b", "p1")], ["n1"], lease_expires_at_ms=5, status=status, sources_removed=True
    )

    code = await transfer_aborts(service, request("t-1", [("col-a", "col-b", "p2")]))

    assert code == grpc.StatusCode.ALREADY_EXISTS
    assert (SRC, "p1") in photo_store.bytes_by_key and (SRC, "p2") in photo_store.bytes_by_key
    assert [k for k in photo_store.bytes_by_key if k[0] == DST] == []


async def test_same_transfer_id_with_different_destination_or_extra_move_is_rejected():
    service, table, photo_store = make_service()
    seed(table, photo_store, SRC, "p1", SidecarRecord())
    seed(table, photo_store, SRC, "p2", SidecarRecord())
    await transfer(service, request("t-1", [("col-a", "col-b", "p1")]))

    wrong_destination = await transfer_aborts(service, request("t-1", [("col-a", "col-c", "p1")]))
    extra_move = await transfer_aborts(service, request("t-1", [("col-a", "col-b", "p1"), ("col-a", "col-b", "p2")]))

    assert wrong_destination == grpc.StatusCode.ALREADY_EXISTS
    assert extra_move == grpc.StatusCode.ALREADY_EXISTS
    assert (SRC, "p2") in photo_store.bytes_by_key


async def test_same_moves_in_different_order_is_the_same_transfer():
    service, table, photo_store = make_service()
    seed(table, photo_store, SRC, "p1", SidecarRecord())
    seed(table, photo_store, SRC, "p2", SidecarRecord())
    first = await transfer(service, request(moves=[("col-a", "col-b", "p1"), ("col-a", "col-b", "p2")]))

    second = await transfer(service, request(moves=[("col-a", "col-b", "p2"), ("col-a", "col-b", "p1")]))

    assert {r.old_photo_id: r.new_photo_id for r in second.results} == {r.old_photo_id: r.new_photo_id for r in first.results}


@pytest.mark.parametrize("bad_id", ["a/b", "..", "a b", "TRANSFER#x", "aä", "x" * 129, "a/../b"])
@pytest.mark.parametrize("position", ["source", "dest"])
async def test_malformed_collection_id_is_invalid_argument_and_nothing_moves(bad_id, position):
    service, table, photo_store = make_service()
    seed(table, photo_store, SRC, "p1", SidecarRecord())
    move = (bad_id, "col-b", "p1") if position == "source" else ("col-a", bad_id, "p1")

    assert await transfer_aborts(service, request(moves=[move])) == grpc.StatusCode.INVALID_ARGUMENT
    assert list(photo_store.bytes_by_key) == [(SRC, "p1")]
    assert table.transfers == {}


async def test_unknown_source_collection_creates_nothing_and_leaves_no_record():
    service, table, photo_store = make_service()
    seed(table, photo_store, SRC, "p1", SidecarRecord())

    code = await transfer_aborts(service, request(moves=[("unknown-col", "col-b", "p1")]))

    assert code == grpc.StatusCode.NOT_FOUND
    assert list(photo_store.bytes_by_key) == [(SRC, "p1")]
    assert table.transfers == {}


async def test_failed_transfer_into_never_seen_destination_leaves_no_copies_and_no_record():
    # Unknown destinations are valid (created implicitly), but a failure must not leave a trace there.
    photo_store = FailingCopyPhotoStore(fail_on_copy=2)
    service, table, _ = make_service(photo_store=photo_store)
    for pid in ("p1", "p2"):
        seed(table, photo_store, SRC, pid, SidecarRecord(card_name=pid))

    code = await transfer_aborts(service, request(moves=[("col-a", "brand-new-collection", p) for p in ("p1", "p2")]))

    assert code == grpc.StatusCode.INTERNAL
    assert set(photo_store.bytes_by_key) == {(SRC, "p1"), (SRC, "p2")}
    assert set(table.items) == {(SRC, "p1"), (SRC, "p2")}
    assert table.transfers == {}


async def test_destination_without_photos_yet_is_valid():
    service, table, photo_store = make_service()
    seed(table, photo_store, SRC, "p1", SidecarRecord())

    response = await transfer(service, request(moves=[("col-a", "brand-new-collection", "p1")]))

    assert ("brand-new-collection", response.results[0].new_photo_id) in photo_store.bytes_by_key


async def test_error_messages_use_umlauts():
    service, _, _ = make_service()
    with pytest.raises(AbortCalled) as invalid:
        await service.TransferPhotos(request(moves=[("col-a", "bad/id", "p1")]), FakeServicerContext())
    with pytest.raises(AbortCalled) as same:
        await service.TransferPhotos(request(moves=[("col-a", "col-a", "p1")]), FakeServicerContext())

    assert "Ungültige" in str(invalid.value.details)
    assert "müssen" in str(same.value.details)


# --- Reconciler ---


async def test_reconciler_finishes_source_removal_of_committed_transfer():
    photo_store = FailingSourceDeletePhotoStore()
    service, table, _ = make_service(photo_store=photo_store)
    seed(table, photo_store, SRC, "p1", SidecarRecord(card_name="Kai"))
    photo_store.fail_deletes = True
    response = await transfer(service, request())
    photo_store.fail_deletes = False

    handled = await service.reconcile_transfers()

    assert handled == 1
    assert (SRC, "p1") not in photo_store.bytes_by_key and (SRC, "p1") not in table.items
    assert (DST, response.results[0].new_photo_id) in photo_store.bytes_by_key
    assert table.transfers["t-1"]["sources_removed"] is True
    assert await service.reconcile_transfers() == 0


async def test_reconciler_rolls_back_abandoned_pending_transfer_and_keeps_sources():
    service, table, photo_store = make_service(now_ms=lambda: 1_000_000)
    seed(table, photo_store, SRC, "p1", SidecarRecord(card_name="Kai"))
    table.transfers["t-1"] = pending_record([("col-a", "col-b", "p1")], ["n1"], lease_expires_at_ms=5)
    photo_store.bytes_by_key[(DST, "n1")] = b"orphan"
    table.items[(DST, "n1")] = SidecarRecord(card_name="Kai")

    assert await service.reconcile_transfers() == 1

    assert_nothing_moved(table, photo_store)
    assert table.transfers == {}


async def test_reconciler_leaves_pending_transfer_with_live_lease_alone():
    service, table, photo_store = make_service(now_ms=lambda: 1_000_000)
    seed(table, photo_store, SRC, "p1", SidecarRecord())
    table.transfers["t-1"] = pending_record([("col-a", "col-b", "p1")], ["n1"], lease_expires_at_ms=2_000_000)
    photo_store.bytes_by_key[(DST, "n1")] = b"in progress"

    assert await service.reconcile_transfers() == 0

    assert (DST, "n1") in photo_store.bytes_by_key
    assert "t-1" in table.transfers


async def test_after_reconciler_rollback_the_same_transfer_id_starts_over():
    service, table, photo_store = make_service(now_ms=lambda: 1_000_000)
    seed(table, photo_store, SRC, "p1", SidecarRecord(card_name="Kai"))
    table.transfers["t-1"] = pending_record([("col-a", "col-b", "p1")], ["n1"], lease_expires_at_ms=5)
    await service.reconcile_transfers()

    response = await transfer(service, request())

    assert response.results[0].new_photo_id != "n1"
    assert (SRC, "p1") not in photo_store.bytes_by_key


# --- Lease fencing: a stalled attempt must never commit or roll back over a newer owner ---


class StalledVerifyPhotoStore(FakePhotoStore):
    """Stalls the first attempt right after it verified its destination copy (before the commit),
    returning the stale verification result - like a process paused by GC or a slow network."""

    def __init__(self) -> None:
        super().__init__()
        self.entered = asyncio.Event()
        self.gate = asyncio.Event()
        self.stalled = False
        self.raise_after_gate = False

    async def exists(self, collection_id, photo_id) -> bool:
        result = await super().exists(collection_id, photo_id)
        if collection_id == DST and not self.stalled:
            self.stalled = True
            self.entered.set()
            await self.gate.wait()
            if self.raise_after_gate:
                raise RuntimeError("S3 exists failed after stall")
        return result


async def start_stalled_attempt(clock):
    store = StalledVerifyPhotoStore()
    service_a, table, _ = make_service(photo_store=store, now_ms=lambda: clock[0])
    seed(table, store, SRC, "p1", SidecarRecord(card_name="Kai"), b"img")
    task = asyncio.create_task(transfer_aborts(service_a, request()))
    await store.entered.wait()
    return service_a, table, store, task


async def test_stalled_attempt_cannot_commit_after_reconciler_rolled_back_and_loses_nothing():
    clock = [1_000_000]
    _, table, store, task = await start_stalled_attempt(clock)
    clock[0] += 120_000  # A's lease expired while it was stalled
    reconciler, _, _ = make_service(sidecar_table=table, photo_store=store, now_ms=lambda: clock[0])

    assert await reconciler.reconcile_transfers() == 1
    assert table.transfers == {}
    assert [k for k in store.bytes_by_key if k[0] == DST] == []
    store.gate.set()

    assert await task == grpc.StatusCode.ABORTED
    assert (SRC, "p1") in store.bytes_by_key and (SRC, "p1") in table.items  # source never removed
    assert table.transfers == {}  # the stale commit did not resurrect a record
    assert_nothing_moved(table, store)


async def test_stalled_attempts_late_destination_writes_are_discarded_after_reconciler_rollback():
    clock = [1_000_000]
    _, table, store, task = await start_stalled_attempt(clock)
    clock[0] += 120_000
    reconciler, _, _ = make_service(sidecar_table=table, photo_store=store, now_ms=lambda: clock[0])
    await reconciler.reconcile_transfers()
    # A's stale in-flight writes land after the rollback.
    store.bytes_by_key[(DST, "late")] = b"x"
    table.items[(DST, "late")] = SidecarRecord()
    store.gate.set()

    await task

    assert [k for k in table.items if k[0] == DST] == [(DST, "late")]  # unrelated data untouched
    assert (SRC, "p1") in store.bytes_by_key


async def test_stalled_attempt_after_takeover_and_completion_by_another_attempt_keeps_the_other_result():
    clock = [1_000_000]
    _, table, store, task = await start_stalled_attempt(clock)
    clock[0] += 120_000
    other, _, _ = make_service(sidecar_table=table, photo_store=store, now_ms=lambda: clock[0])

    winner = await transfer(other, request())  # takes over the expired lease and completes
    new_id = winner.results[0].new_photo_id
    assert (SRC, "p1") not in store.bytes_by_key
    store.gate.set()

    assert await task == grpc.StatusCode.ABORTED
    assert [k for k in store.bytes_by_key if k[0] == DST] == [(DST, new_id)]  # B's copy survives
    assert table.items[(DST, new_id)].card_name == "Kai"
    assert table.transfers["t-1"]["status"] == "committed"
    assert table.transfers["t-1"]["results"][0]["new_photo_id"] == new_id
    replay = await transfer(other, request())
    assert replay.results[0].new_photo_id == new_id


@pytest.mark.parametrize("status", ["pending", "committed"])
async def test_stalled_attempt_failing_after_losing_the_lease_does_not_roll_back_the_new_owner(status):
    clock = [1_000_000]
    _, table, store, task = await start_stalled_attempt(clock)
    new_id = table.transfers["t-1"]["results"][0]["new_photo_id"]
    # Another attempt took the transfer over (new lease value) and is working on / committed it.
    table.transfers["t-1"] = {**table.transfers["t-1"], "lease_expires_at_ms": 9_999_999, "status": status}
    store.raise_after_gate = True
    store.gate.set()

    assert await task == grpc.StatusCode.INTERNAL

    assert (DST, new_id) in store.bytes_by_key
    assert (DST, new_id) in table.items
    assert table.transfers["t-1"]["lease_expires_at_ms"] == 9_999_999
    assert table.transfers["t-1"]["status"] == status


async def test_rollback_record_delete_is_conditional_on_the_lease_it_holds():
    class TakeoverDuringRollbackTable(FakeSidecarTable):
        """Another attempt claims the transfer right after the rollback started."""

        async def delete(self, collection_id, photo_id) -> None:
            if collection_id == DST and self.transfers.get("t-1"):
                self.transfers["t-1"] = {**self.transfers["t-1"], "lease_expires_at_ms": 9_999_999}
            await super().delete(collection_id, photo_id)

    photo_store = FailingCopyPhotoStore(fail_on_copy=1)
    table = TakeoverDuringRollbackTable()
    service, _, _ = make_service(sidecar_table=table, photo_store=photo_store, now_ms=lambda: 1_000_000)
    seed(table, photo_store, SRC, "p1", SidecarRecord(card_name="Kai"))

    assert await transfer_aborts(service, request()) == grpc.StatusCode.INTERNAL

    # The conditional record delete lost to the new owner, so its record survived.
    assert table.transfers["t-1"]["lease_expires_at_ms"] == 9_999_999
    assert (SRC, "p1") in photo_store.bytes_by_key


async def test_lease_is_extended_between_copy_steps_so_a_long_transfer_is_not_taken_over():
    clock = [1_000_000]
    observed: list[grpc.StatusCode] = []
    moves = [("col-a", "col-b", p) for p in ("p1", "p2", "p3")]

    class SlowCopyPhotoStore(FakePhotoStore):
        copies = 0
        competitor = None

        async def copy(self, *args) -> None:
            await super().copy(*args)
            clock[0] += 50_000  # each copy takes 50s; the initial 60s lease alone would expire
            SlowCopyPhotoStore.copies += 1
            if SlowCopyPhotoStore.copies == 2:
                observed.append(await transfer_aborts(SlowCopyPhotoStore.competitor, request(moves=moves)))

    store = SlowCopyPhotoStore()
    service, table, _ = make_service(photo_store=store, now_ms=lambda: clock[0])
    SlowCopyPhotoStore.competitor, _, _ = make_service(sidecar_table=table, photo_store=store, now_ms=lambda: clock[0])
    for pid in ("p1", "p2", "p3"):
        seed(table, store, SRC, pid, SidecarRecord(card_name=pid))

    response = await transfer(service, request(moves=moves))

    assert observed == [grpc.StatusCode.ABORTED]  # 100s in, the extended lease is still live
    assert len(response.results) == 3
    assert [k for k in store.bytes_by_key if k[0] == SRC] == []
    assert len([k for k in store.bytes_by_key if k[0] == DST]) == 3
    assert table.transfers["t-1"]["status"] == "committed"


async def test_lease_value_strictly_increases_even_with_a_frozen_clock():
    service, table, photo_store = make_service(now_ms=lambda: 1_000_000)
    seed(table, photo_store, SRC, "p1", SidecarRecord())
    seed(table, photo_store, SRC, "p2", SidecarRecord())

    await transfer(service, request(moves=[("col-a", "col-b", "p1"), ("col-a", "col-b", "p2")]))

    assert table.transfers["t-1"]["lease_expires_at_ms"] > 1_000_000 + 60_000


async def test_reconciler_rollback_is_skipped_when_the_lease_was_renewed_meanwhile():
    class RenewedDuringClaimTable(FakeSidecarTable):
        """The original owner renews its lease between the reconciler's read and its claim."""

        async def replace_transfer(self, transfer_id, record, **kwargs) -> bool:
            if kwargs.get("expected_lease_expires_at_ms") == 5 and "t-1" in self.transfers:
                self.transfers["t-1"] = {**self.transfers["t-1"], "lease_expires_at_ms": 2_000_000}
            return await super().replace_transfer(transfer_id, record, **kwargs)

    table = RenewedDuringClaimTable()
    service, _, photo_store = make_service(sidecar_table=table, now_ms=lambda: 1_000_000)
    seed(table, photo_store, SRC, "p1", SidecarRecord())
    table.transfers["t-1"] = pending_record([("col-a", "col-b", "p1")], ["n1"], lease_expires_at_ms=5)
    photo_store.bytes_by_key[(DST, "n1")] = b"in progress"

    assert await service.reconcile_transfers() == 0

    assert (DST, "n1") in photo_store.bytes_by_key
    assert table.transfers["t-1"]["lease_expires_at_ms"] == 2_000_000
