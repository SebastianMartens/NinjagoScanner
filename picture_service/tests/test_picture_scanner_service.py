import dataclasses
import json
from datetime import datetime, timezone

import grpc
import pytest
from langchain_core.exceptions import ModelInvalidRequestError

from picture_service._generated import picture_service_pb2 as pb2
from picture_service.gemini_service import ATTRIBUTE_DETECTION_SCHEMA
from picture_service.models import (
    AnalysisStatuses,
    CatalogCardInfo,
    CatalogSnapshot,
    Languages,
    ReviewStatuses,
    SeriesInfo,
    SidecarRecord,
)
from picture_service.picture_scanner_service import PictureScannerService
from picture_service.sidecar_cache import SidecarCache
from tests.fakes import FakePhotoStore, FakeSidecarTable
from tests.grpc_fakes import AbortCalled, FakeServicerContext

CATALOG = CatalogSnapshot(
    series=(SeriesInfo(serie="Serie 1", jahr=2016),),
    cards=(CatalogCardInfo(series_name="Serie 1", card_number="1", card_name="Kai", category="Heroes"),),
)

_DEFAULT_STAGE1_RESULT = {"card_number": "1"}
_DEFAULT_STAGE2_RESULT = {"series_name": "Serie 1", "rarity": "common", "language": "de"}


def make_service(
    *,
    sidecar_table=None,
    photo_store=None,
    stage1_result=None,
    stage2_result=None,
    catalog=None,
    catalog_error=None,
    build_model=None,
):
    """`stage1_result`/`stage2_result` fake each stage's parsed Gemini output - by default they
    resolve to CATALOG's one series/card (analysis_status ok, card_name "Kai") so tests that
    don't care about analysis content can just call make_service(). `build_model` replaces
    that fake entirely, for tests that need a model that fails or has side effects."""
    sidecar_table = sidecar_table or FakeSidecarTable()
    photo_store = photo_store or FakePhotoStore()
    cache = SidecarCache(sidecar_table)

    async def load_catalog_snapshot(address):
        if catalog_error:
            raise catalog_error
        return catalog if catalog is not None else CATALOG

    def default_build_model(config, schema):
        parsed = (stage1_result if schema is ATTRIBUTE_DETECTION_SCHEMA else stage2_result) or (
            _DEFAULT_STAGE1_RESULT if schema is ATTRIBUTE_DETECTION_SCHEMA else _DEFAULT_STAGE2_RESULT
        )
        return _RawInvokeModel(parsed)

    build_model = build_model or default_build_model

    return (
        PictureScannerService(
            cache, photo_store, build_model=build_model, load_catalog_snapshot=load_catalog_snapshot
        ),
        sidecar_table,
        photo_store,
    )


class _RawInvokeModel:
    def __init__(self, parsed):
        self._parsed = parsed

    async def ainvoke(self, messages):
        return {"raw": _Raw(str(self._parsed)), "parsed": self._parsed, "parsing_error": None}


class _Raw:
    def __init__(self, content):
        self.content = content


# --- Collection-scoping ---


async def test_list_cards_requires_collection_id():
    service, _, _ = make_service()
    with pytest.raises(AbortCalled) as exc_info:
        await service.ListCards(pb2.ListCardsRequest(collection_id=""), FakeServicerContext())
    assert exc_info.value.code == grpc.StatusCode.INVALID_ARGUMENT


async def test_update_sidecar_requires_collection_id():
    service, _, _ = make_service()
    with pytest.raises(AbortCalled) as exc_info:
        await service.UpdateSidecar(pb2.UpdateSidecarRequest(collection_id="", photo_id="p1"), FakeServicerContext())
    assert exc_info.value.code == grpc.StatusCode.INVALID_ARGUMENT


async def test_migrate_sidecars_does_not_require_collection_id():
    service, table, _ = make_service()
    table.items[("col-a", "p1")] = SidecarRecord()

    response = await service.MigrateSidecars(pb2.MigrateSidecarsRequest(), FakeServicerContext())

    assert response.total_files == 1
    assert response.migrated == 1


async def test_list_cards_excludes_other_collections_photos():
    service, table, photo_store = make_service()
    photo_store.bytes_by_key[("col-a", "p1")] = b"a"
    photo_store.bytes_by_key[("col-b", "p2")] = b"b"

    response = await service.ListCards(pb2.ListCardsRequest(collection_id="col-a"), FakeServicerContext())

    assert [card.photo_id for card in response.cards] == ["p1"]


async def test_get_photo_download_url_not_found_when_photo_in_different_collection():
    service, _, photo_store = make_service()
    photo_store.bytes_by_key[("col-b", "p1")] = b"data"

    with pytest.raises(AbortCalled) as exc_info:
        await service.GetPhotoDownloadUrl(
            pb2.GetPhotoDownloadUrlRequest(photo_id="p1", collection_id="col-a"), FakeServicerContext()
        )
    assert exc_info.value.code == grpc.StatusCode.NOT_FOUND


# --- Card listing defaults ---


async def test_list_cards_reports_not_analyzed_for_unscanned_image():
    service, _, photo_store = make_service()
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"

    response = await service.ListCards(pb2.ListCardsRequest(collection_id="col-a"), FakeServicerContext())

    card = response.cards[0]
    assert card.analysis_status == AnalysisStatuses.NOT_ANALYZED
    assert card.review_status == ReviewStatuses.UNREVIEWED
    assert card.language == Languages.DEFAULT
    assert card.card_name == ""


async def test_list_cards_reports_legacy_pending_as_not_analyzed():
    service, table, photo_store = make_service()
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"
    table.items[("col-a", "p1")] = SidecarRecord(analysis_status="pending")

    response = await service.ListCards(pb2.ListCardsRequest(collection_id="col-a"), FakeServicerContext())

    assert response.cards[0].analysis_status == AnalysisStatuses.NOT_ANALYZED


async def test_list_cards_preserves_explicit_unknown_language():
    service, table, photo_store = make_service()
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"
    table.items[("col-a", "p1")] = SidecarRecord(language="unknown")

    response = await service.ListCards(pb2.ListCardsRequest(collection_id="col-a"), FakeServicerContext())

    assert response.cards[0].language == "unknown"


async def test_list_cards_includes_download_url_on_every_entry():
    service, _, photo_store = make_service()
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"

    response = await service.ListCards(pb2.ListCardsRequest(collection_id="col-a"), FakeServicerContext())

    assert response.cards[0].download_url != ""


# --- UploadPhoto ---


class _FakeRequestIterator:
    def __init__(self, messages):
        self._messages = iter(messages)

    def __aiter__(self):
        return self

    async def __anext__(self):
        try:
            return next(self._messages)
        except StopIteration:
            raise StopAsyncIteration


async def test_upload_photo_happy_path_assigns_id_and_analyzes():
    service, table, photo_store = make_service()
    messages = [
        pb2.UploadPhotoRequest(
            metadata=pb2.UploadPhotoMetadata(source_file_name="a.jpg", collection_id="col-a", api_key="key")
        ),
        pb2.UploadPhotoRequest(chunk=b"hello "),
        pb2.UploadPhotoRequest(chunk=b"world"),
    ]

    response = await service.UploadPhoto(_FakeRequestIterator(messages), FakeServicerContext())

    photo_id = response.card.photo_id
    assert photo_id
    assert photo_store.bytes_by_key[("col-a", photo_id)] == b"hello world"
    assert response.card.analysis_status == AnalysisStatuses.OK
    assert response.card.card_name == "Kai"


async def test_upload_photo_rejects_missing_collection_id():
    service, _, _ = make_service()
    messages = [
        pb2.UploadPhotoRequest(metadata=pb2.UploadPhotoMetadata(source_file_name="a.jpg", collection_id="")),
    ]

    with pytest.raises(AbortCalled) as exc_info:
        await service.UploadPhoto(_FakeRequestIterator(messages), FakeServicerContext())
    assert exc_info.value.code == grpc.StatusCode.INVALID_ARGUMENT


async def test_upload_photo_rejects_unsupported_extension():
    service, _, _ = make_service()
    messages = [
        pb2.UploadPhotoRequest(metadata=pb2.UploadPhotoMetadata(source_file_name="a.gif", collection_id="col-a")),
        pb2.UploadPhotoRequest(chunk=b"data"),
    ]

    with pytest.raises(AbortCalled) as exc_info:
        await service.UploadPhoto(_FakeRequestIterator(messages), FakeServicerContext())
    assert exc_info.value.code == grpc.StatusCode.INVALID_ARGUMENT


async def test_upload_photo_rejects_empty_file():
    service, _, _ = make_service()
    messages = [
        pb2.UploadPhotoRequest(metadata=pb2.UploadPhotoMetadata(source_file_name="a.jpg", collection_id="col-a")),
    ]

    with pytest.raises(AbortCalled) as exc_info:
        await service.UploadPhoto(_FakeRequestIterator(messages), FakeServicerContext())
    assert exc_info.value.code == grpc.StatusCode.INVALID_ARGUMENT


async def test_two_uploads_with_same_file_name_get_distinct_ids():
    service, photo_store_table, photo_store = make_service()

    async def upload():
        messages = [
            pb2.UploadPhotoRequest(
                metadata=pb2.UploadPhotoMetadata(source_file_name="a.jpg", collection_id="col-a", api_key="k")
            ),
            pb2.UploadPhotoRequest(chunk=b"data"),
        ]
        return await service.UploadPhoto(_FakeRequestIterator(messages), FakeServicerContext())

    r1 = await upload()
    r2 = await upload()

    assert r1.card.photo_id != r2.card.photo_id


# --- Sidecar editing ---


async def test_update_set_name_creates_sidecar_reporting_not_analyzed():
    service, _, photo_store = make_service()
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"

    await service.UpdateSetName(
        pb2.UpdateSetNameRequest(photo_id="p1", set_name="Serie 1", collection_id="col-a"), FakeServicerContext()
    )
    response = await service.ListCards(pb2.ListCardsRequest(collection_id="col-a"), FakeServicerContext())

    assert response.cards[0].set_name == "Serie 1"
    assert response.cards[0].analysis_status == AnalysisStatuses.NOT_ANALYZED


async def test_update_set_name_only_changes_set_name():
    service, table, _ = make_service()
    table.items[("col-a", "p1")] = SidecarRecord(analysis_status="ok", card_name="Kai", set_name="Old")

    await service.UpdateSetName(
        pb2.UpdateSetNameRequest(photo_id="p1", set_name="New", collection_id="col-a"), FakeServicerContext()
    )

    updated = table.items[("col-a", "p1")]
    assert updated.set_name == "New"
    assert updated.analysis_status == "ok"
    assert updated.card_name == "Kai"


async def test_update_card_number_blank_normalizes_to_none():
    service, table, _ = make_service()
    table.items[("col-a", "p1")] = SidecarRecord(card_number="1")

    await service.UpdateCardNumber(
        pb2.UpdateCardNumberRequest(photo_id="p1", card_number="   ", collection_id="col-a"), FakeServicerContext()
    )

    assert table.items[("col-a", "p1")].card_number is None


async def test_update_sidecar_overwrites_all_editable_fields():
    service, table, _ = make_service()
    table.items[("col-a", "p1")] = SidecarRecord(card_name="Old", source_file_name="a.jpg")

    await service.UpdateSidecar(
        pb2.UpdateSidecarRequest(
            photo_id="p1",
            collection_id="col-a",
            analysis_status="ok",
            card_name="Kai",
            card_number="1",
            set_name="Serie 1",
            rarity="common",
            review_status="verified",
        ),
        FakeServicerContext(),
    )

    updated = table.items[("col-a", "p1")]
    assert updated.card_name == "Kai"
    assert updated.review_status == "verified"
    assert updated.source_file_name == "a.jpg"  # preserved


async def test_update_sidecar_preserves_detected_and_derived():
    service, table, _ = make_service()
    table.items[("col-a", "p1")] = SidecarRecord(
        card_name="Old", detected={"number_top_left": "1"}, derived={"class": "character"}
    )

    await service.UpdateSidecar(
        pb2.UpdateSidecarRequest(photo_id="p1", collection_id="col-a", card_name="Kai"), FakeServicerContext()
    )

    updated = table.items[("col-a", "p1")]
    assert updated.card_name == "Kai"
    assert updated.detected == {"number_top_left": "1"}
    assert updated.derived == {"class": "character"}


async def test_update_sidecar_blank_fields_normalized_to_none():
    service, table, _ = make_service()

    await service.UpdateSidecar(
        pb2.UpdateSidecarRequest(photo_id="p1", collection_id="col-a", card_name="   "), FakeServicerContext()
    )

    assert table.items[("col-a", "p1")].card_name is None


async def test_same_file_name_two_collections_edited_independently():
    service, table, _ = make_service()

    await service.UpdateSetName(
        pb2.UpdateSetNameRequest(photo_id="p1", set_name="A", collection_id="col-a"), FakeServicerContext()
    )
    await service.UpdateSetName(
        pb2.UpdateSetNameRequest(photo_id="p1", set_name="B", collection_id="col-b"), FakeServicerContext()
    )

    assert table.items[("col-a", "p1")].set_name == "A"
    assert table.items[("col-b", "p1")].set_name == "B"


async def test_rescanning_does_not_change_review_status():
    service, table, photo_store = make_service()
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"
    table.items[("col-a", "p1")] = SidecarRecord(analysis_status="ok", review_status="verified")

    await service.Scan(
        pb2.ScanRequest(collection_id="col-a", api_key="key", overwrite_existing_sidecars=True), FakeServicerContext()
    )

    assert table.items[("col-a", "p1")].review_status == "verified"


async def test_rescanning_verified_sidecar_keeps_series_and_card_number():
    service, table, photo_store = make_service(
        stage1_result={"card_number": "7"}, stage2_result={"series_name": "Serie 9", "rarity": "rare"}
    )
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"
    table.items[("col-a", "p1")] = SidecarRecord(
        analysis_status="ok", review_status="verified", set_name="Serie 1", card_number="1"
    )

    await service.Scan(
        pb2.ScanRequest(collection_id="col-a", api_key="key", overwrite_existing_sidecars=True), FakeServicerContext()
    )

    updated = table.items[("col-a", "p1")]
    assert updated.set_name == "Serie 1"
    assert updated.card_number == "1"
    assert updated.review_status == "verified"
    assert updated.rarity == "rare"
    assert updated.detected == {"card_number": "7"}
    assert updated.derived == {"series_name": "Serie 9", "rarity": "rare"}


async def test_rescanning_unverified_sidecar_re_resolves_series_and_card_number():
    service, table, photo_store = make_service()
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"
    table.items[("col-a", "p1")] = SidecarRecord(
        analysis_status="ok", review_status="incorrect", set_name="Serie 9", card_number="42"
    )

    await service.Scan(
        pb2.ScanRequest(collection_id="col-a", api_key="key", overwrite_existing_sidecars=True), FakeServicerContext()
    )

    updated = table.items[("col-a", "p1")]
    assert updated.set_name == "Serie 1"
    assert updated.card_number == "1"


# --- ReanalyzePhoto ---


@pytest.fixture
def gemini_api_key(monkeypatch):
    monkeypatch.setenv("GEMINI_API_KEY", "key")


def _reanalyze(service, photo_id="p1", collection_id="col-a"):
    return service.ReanalyzePhoto(
        pb2.ReanalyzePhotoRequest(photo_id=photo_id, collection_id=collection_id), FakeServicerContext()
    )


async def test_reanalyze_photo_requires_photo_id(gemini_api_key):
    service, _, _ = make_service()
    with pytest.raises(AbortCalled) as exc_info:
        await _reanalyze(service, photo_id=" ")
    assert exc_info.value.code == grpc.StatusCode.INVALID_ARGUMENT


async def test_reanalyze_photo_requires_collection_id(gemini_api_key):
    service, _, _ = make_service()
    with pytest.raises(AbortCalled) as exc_info:
        await _reanalyze(service, collection_id="")
    assert exc_info.value.code == grpc.StatusCode.INVALID_ARGUMENT


async def test_reanalyze_photo_not_found_when_photo_missing(gemini_api_key):
    service, table, _ = make_service()

    with pytest.raises(AbortCalled) as exc_info:
        await _reanalyze(service)

    assert exc_info.value.code == grpc.StatusCode.NOT_FOUND
    assert table.items == {}


async def test_reanalyze_photo_not_found_when_photo_only_in_different_collection(gemini_api_key):
    service, table, photo_store = make_service()
    photo_store.bytes_by_key[("col-b", "p1")] = b"data"

    with pytest.raises(AbortCalled) as exc_info:
        await _reanalyze(service, collection_id="col-a")

    assert exc_info.value.code == grpc.StatusCode.NOT_FOUND
    assert table.items == {}


async def test_reanalyze_photo_creates_sidecar_when_none_exists(gemini_api_key):
    service, table, photo_store = make_service()
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"

    response = await _reanalyze(service)

    assert response.card.photo_id == "p1"
    assert response.card.analysis_status == AnalysisStatuses.OK
    assert response.card.card_name == "Kai"
    assert table.items[("col-a", "p1")].analysis_status == AnalysisStatuses.OK


async def test_reanalyze_photo_is_not_skipped_for_an_ok_sidecar_and_replaces_the_result(gemini_api_key):
    service, table, photo_store = make_service(
        stage1_result={"card_number": "1", "new": "detected"}, stage2_result={"series_name": "Serie 1", "new": "derived"}
    )
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"
    old_scanned_at = datetime(2020, 1, 1, tzinfo=timezone.utc)
    table.items[("col-a", "p1")] = SidecarRecord(
        analysis_status=AnalysisStatuses.OK,
        card_number="9",
        set_name="Serie 9",
        detected={"old": "detected"},
        derived={"old": "derived"},
        scanned_at_utc=old_scanned_at,
    )

    response = await _reanalyze(service)

    updated = table.items[("col-a", "p1")]
    assert updated.detected == {"card_number": "1", "new": "detected"}
    assert updated.derived == {"series_name": "Serie 1", "new": "derived"}
    assert updated.scanned_at_utc > old_scanned_at
    assert (response.card.set_name, response.card.card_number) == ("Serie 1", "1")

    listed = await service.ListCards(pb2.ListCardsRequest(collection_id="col-a"), FakeServicerContext())
    assert listed.cards[0].card_number == "1"
    details = await service.GetCardDetails(
        pb2.GetCardDetailsRequest(photo_id="p1", collection_id="col-a"), FakeServicerContext()
    )
    assert json.loads(details.details.attributes_json)["detected"]["new"] == "detected"


async def test_reanalyze_photo_clears_the_error_of_a_previously_failed_analysis(gemini_api_key):
    service, table, photo_store = make_service()
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"
    table.items[("col-a", "p1")] = SidecarRecord(analysis_status=AnalysisStatuses.FAILED, error_message="boom")

    response = await _reanalyze(service)

    assert response.card.analysis_status == AnalysisStatuses.OK
    assert table.items[("col-a", "p1")].error_message is None


@pytest.mark.parametrize("review_status", [ReviewStatuses.INCORRECT, ReviewStatuses.VERIFIED])
async def test_reanalyze_photo_preserves_review_status(gemini_api_key, review_status):
    service, table, photo_store = make_service()
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"
    table.items[("col-a", "p1")] = SidecarRecord(analysis_status=AnalysisStatuses.OK, review_status=review_status)

    response = await _reanalyze(service)

    assert table.items[("col-a", "p1")].review_status == review_status
    assert response.card.review_status == review_status


async def test_reanalyze_photo_verified_sidecar_keeps_series_and_card_number(gemini_api_key):
    service, table, photo_store = make_service(
        stage1_result={"card_number": "7"}, stage2_result={"series_name": "Serie 9", "rarity": "rare"}
    )
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"
    table.items[("col-a", "p1")] = SidecarRecord(
        analysis_status=AnalysisStatuses.OK, review_status=ReviewStatuses.VERIFIED, set_name="Serie 1", card_number="1"
    )

    await _reanalyze(service)

    updated = table.items[("col-a", "p1")]
    assert (updated.set_name, updated.card_number) == ("Serie 1", "1")
    assert updated.rarity == "rare"
    assert updated.detected == {"card_number": "7"}


async def test_reanalyze_photo_unverified_sidecar_takes_the_new_match(gemini_api_key):
    service, table, photo_store = make_service()
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"
    table.items[("col-a", "p1")] = SidecarRecord(
        analysis_status=AnalysisStatuses.OK,
        review_status=ReviewStatuses.UNREVIEWED,
        set_name="Serie 9",
        card_number="42",
    )

    await _reanalyze(service)

    updated = table.items[("col-a", "p1")]
    assert (updated.set_name, updated.card_number) == ("Serie 1", "1")


async def test_reanalyze_photo_keeps_a_review_status_changed_during_the_analysis(gemini_api_key):
    holder = {}

    class ReviewingDuringAnalysisModel:
        async def ainvoke(self, messages):
            cache = holder["service"]._sidecar_cache
            current = await cache.get("col-a", "p1")
            await cache.set_record("col-a", "p1", dataclasses.replace(current, review_status=ReviewStatuses.VERIFIED))
            parsed = {"card_number": "1"}
            return {"raw": _Raw(str(parsed)), "parsed": parsed, "parsing_error": None}

    service, table, photo_store = make_service(build_model=lambda config, schema: ReviewingDuringAnalysisModel())
    holder["service"] = service
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"
    table.items[("col-a", "p1")] = SidecarRecord(
        analysis_status=AnalysisStatuses.OK, review_status=ReviewStatuses.UNREVIEWED
    )

    response = await _reanalyze(service)

    assert table.items[("col-a", "p1")].review_status == ReviewStatuses.VERIFIED
    assert response.card.review_status == ReviewStatuses.VERIFIED


class _TransportFailingModel:
    async def ainvoke(self, messages):
        raise ModelInvalidRequestError("gemini unreachable")


class _UnusableResponseModel:
    async def ainvoke(self, messages):
        return {"raw": _Raw("not json"), "parsed": None, "parsing_error": "bad"}


def _existing_ok_sidecar():
    return SidecarRecord(
        analysis_status=AnalysisStatuses.OK,
        card_name="Old",
        card_number="9",
        set_name="Serie 9",
        review_status=ReviewStatuses.VERIFIED,
        detected={"old": "x"},
        derived={"old": "y"},
        scanned_at_utc=datetime(2020, 1, 1, tzinfo=timezone.utc),
    )


async def test_reanalyze_photo_transport_failure_aborts_unavailable_and_leaves_sidecar_unchanged(gemini_api_key):
    service, table, photo_store = make_service(build_model=lambda config, schema: _TransportFailingModel())
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"
    before = _existing_ok_sidecar()
    table.items[("col-a", "p1")] = before

    with pytest.raises(AbortCalled) as exc_info:
        await _reanalyze(service)

    assert exc_info.value.code == grpc.StatusCode.UNAVAILABLE
    assert table.items[("col-a", "p1")] == before


async def test_reanalyze_photo_without_api_key_aborts_failed_precondition_and_leaves_sidecar_unchanged(monkeypatch):
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    service, table, photo_store = make_service()
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"
    before = _existing_ok_sidecar()
    table.items[("col-a", "p1")] = before

    with pytest.raises(AbortCalled) as exc_info:
        await _reanalyze(service)

    assert exc_info.value.code == grpc.StatusCode.FAILED_PRECONDITION
    assert table.items[("col-a", "p1")] == before


async def test_reanalyze_photo_with_unreachable_catalog_aborts_unavailable_and_leaves_sidecar_unchanged(gemini_api_key):
    service, table, photo_store = make_service(catalog_error=ConnectionError("down"))
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"
    before = _existing_ok_sidecar()
    table.items[("col-a", "p1")] = before

    with pytest.raises(AbortCalled) as exc_info:
        await _reanalyze(service)

    assert exc_info.value.code == grpc.StatusCode.UNAVAILABLE
    assert table.items[("col-a", "p1")] == before


async def test_reanalyze_photo_content_failure_is_recorded_as_failed_and_returned_normally(gemini_api_key):
    service, table, photo_store = make_service(build_model=lambda config, schema: _UnusableResponseModel())
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"
    table.items[("col-a", "p1")] = _existing_ok_sidecar()

    response = await _reanalyze(service)

    assert response.card.analysis_status == AnalysisStatuses.FAILED
    updated = table.items[("col-a", "p1")]
    assert updated.analysis_status == AnalysisStatuses.FAILED
    assert updated.error_message
    assert updated.review_status == ReviewStatuses.VERIFIED


# --- DeletePhoto ---


async def test_delete_photo_not_found_when_missing():
    service, _, _ = make_service()
    with pytest.raises(AbortCalled) as exc_info:
        await service.DeletePhoto(pb2.DeletePhotoRequest(photo_id="missing", collection_id="col-a"), FakeServicerContext())
    assert exc_info.value.code == grpc.StatusCode.NOT_FOUND


async def test_delete_photo_not_found_when_only_in_different_collection():
    service, _, photo_store = make_service()
    photo_store.bytes_by_key[("col-b", "p1")] = b"data"

    with pytest.raises(AbortCalled) as exc_info:
        await service.DeletePhoto(pb2.DeletePhotoRequest(photo_id="p1", collection_id="col-a"), FakeServicerContext())
    assert exc_info.value.code == grpc.StatusCode.NOT_FOUND


async def test_delete_photo_removes_bytes_and_evicts_cache():
    service, table, photo_store = make_service()
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"
    table.items[("col-a", "p1")] = SidecarRecord(card_name="Kai")

    response = await service.DeletePhoto(pb2.DeletePhotoRequest(photo_id="p1", collection_id="col-a"), FakeServicerContext())

    assert response.success is True
    assert ("col-a", "p1") not in photo_store.bytes_by_key
    assert ("col-a", "p1") not in table.items


async def test_delete_photo_succeeds_with_no_sidecar():
    service, _, photo_store = make_service()
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"

    response = await service.DeletePhoto(pb2.DeletePhotoRequest(photo_id="p1", collection_id="col-a"), FakeServicerContext())

    assert response.success is True


# --- Additional sidecar-editing coverage (UpdateCardLanguage / UpdateReviewStatus) ---


async def test_update_card_language_creates_sidecar_reporting_not_analyzed():
    service, _, photo_store = make_service()
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"

    await service.UpdateCardLanguage(
        pb2.UpdateCardLanguageRequest(photo_id="p1", language="en", collection_id="col-a"), FakeServicerContext()
    )
    response = await service.ListCards(pb2.ListCardsRequest(collection_id="col-a"), FakeServicerContext())

    assert response.cards[0].language == "en"
    assert response.cards[0].analysis_status == AnalysisStatuses.NOT_ANALYZED


async def test_update_card_language_blank_normalizes_to_none():
    service, table, _ = make_service()
    table.items[("col-a", "p1")] = SidecarRecord(language="de")

    await service.UpdateCardLanguage(
        pb2.UpdateCardLanguageRequest(photo_id="p1", language="   ", collection_id="col-a"), FakeServicerContext()
    )

    assert table.items[("col-a", "p1")].language is None


async def test_update_card_language_only_changes_language():
    service, table, _ = make_service()
    table.items[("col-a", "p1")] = SidecarRecord(
        analysis_status="ok", card_number="1", set_name="Serie 1", review_status="verified"
    )

    await service.UpdateCardLanguage(
        pb2.UpdateCardLanguageRequest(photo_id="p1", language="en", collection_id="col-a"), FakeServicerContext()
    )

    updated = table.items[("col-a", "p1")]
    assert updated.language == "en"
    assert updated.analysis_status == "ok"
    assert updated.card_number == "1"
    assert updated.set_name == "Serie 1"
    assert updated.review_status == "verified"


async def test_update_review_status_creates_sidecar_reporting_not_analyzed():
    service, _, photo_store = make_service()
    photo_store.bytes_by_key[("col-a", "p1")] = b"data"

    await service.UpdateReviewStatus(
        pb2.UpdateReviewStatusRequest(photo_id="p1", review_status="verified", collection_id="col-a"),
        FakeServicerContext(),
    )
    response = await service.ListCards(pb2.ListCardsRequest(collection_id="col-a"), FakeServicerContext())

    assert response.cards[0].review_status == "verified"
    assert response.cards[0].analysis_status == AnalysisStatuses.NOT_ANALYZED


async def test_update_review_status_only_changes_review_status():
    service, table, _ = make_service()
    table.items[("col-a", "p1")] = SidecarRecord(analysis_status="ok", set_name="Serie 1", card_name="Kai")

    await service.UpdateReviewStatus(
        pb2.UpdateReviewStatusRequest(photo_id="p1", review_status="incorrect", collection_id="col-a"),
        FakeServicerContext(),
    )

    updated = table.items[("col-a", "p1")]
    assert updated.review_status == "incorrect"
    assert updated.analysis_status == "ok"
    assert updated.set_name == "Serie 1"
    assert updated.card_name == "Kai"


async def test_editing_other_sidecar_fields_does_not_change_review_status():
    service, table, _ = make_service()
    table.items[("col-a", "p1")] = SidecarRecord(review_status="verified", set_name="Old")

    await service.UpdateSetName(
        pb2.UpdateSetNameRequest(photo_id="p1", set_name="New", collection_id="col-a"), FakeServicerContext()
    )

    assert table.items[("col-a", "p1")].review_status == "verified"


# --- MigrateSidecars idempotency ---


async def test_migrate_sidecars_is_idempotent():
    service, table, _ = make_service()
    table.items[("col-a", "p1")] = SidecarRecord()  # missing analysis_status

    first = await service.MigrateSidecars(pb2.MigrateSidecarsRequest(), FakeServicerContext())
    assert first.migrated == 1
    assert first.already_current == 0

    second = await service.MigrateSidecars(pb2.MigrateSidecarsRequest(), FakeServicerContext())
    assert second.migrated == 0
    assert second.already_current == 1


async def test_migrate_sidecars_converts_legacy_flat_record_to_three_section_shape():
    service, table, _ = make_service()
    table.items[("col-a", "p1")] = SidecarRecord(analysis_status="ok", card_name="Kai")  # detected/derived None

    response = await service.MigrateSidecars(pb2.MigrateSidecarsRequest(), FakeServicerContext())

    assert response.migrated == 1
    assert response.already_current == 0
    migrated = table.items[("col-a", "p1")]
    assert migrated.card_name == "Kai"  # existing Judged fields preserved
    assert migrated.detected == {}
    assert migrated.derived == {}


async def test_migrate_sidecars_leaves_already_migrated_record_unchanged():
    service, table, _ = make_service()
    table.items[("col-a", "p1")] = SidecarRecord(analysis_status="ok", card_name="Kai", detected={}, derived={"class": "character"})

    response = await service.MigrateSidecars(pb2.MigrateSidecarsRequest(), FakeServicerContext())

    assert response.migrated == 0
    assert response.already_current == 1
    assert table.items[("col-a", "p1")].derived == {"class": "character"}


# --- Additional photo-download coverage ---


async def test_get_photo_download_url_not_found_when_photo_does_not_exist_anywhere():
    service, _, _ = make_service()

    with pytest.raises(AbortCalled) as exc_info:
        await service.GetPhotoDownloadUrl(
            pb2.GetPhotoDownloadUrlRequest(photo_id="missing", collection_id="col-a"), FakeServicerContext()
        )
    assert exc_info.value.code == grpc.StatusCode.NOT_FOUND


# --- Additional photo-upload coverage ---


# --- GetCardDetails ---


async def test_get_card_details_reports_detected_and_derived_as_json():
    service, table, _ = make_service()
    table.items[("col-a", "p1")] = SidecarRecord(
        detected={"number_top_left": "1"}, derived={"class": "character"}
    )

    response = await service.GetCardDetails(
        pb2.GetCardDetailsRequest(photo_id="p1", collection_id="col-a"), FakeServicerContext()
    )

    assert json.loads(response.details.attributes_json) == {
        "detected": {"number_top_left": "1"},
        "derived": {"class": "character"},
    }


async def test_get_card_details_reports_empty_json_for_legacy_record():
    service, table, _ = make_service()
    table.items[("col-a", "p1")] = SidecarRecord(card_name="Kai")  # detected/derived None

    response = await service.GetCardDetails(
        pb2.GetCardDetailsRequest(photo_id="p1", collection_id="col-a"), FakeServicerContext()
    )

    assert json.loads(response.details.attributes_json) == {"detected": {}, "derived": {}}


async def test_upload_photo_missing_file_name_fails_with_invalid_argument():
    service, _, _ = make_service()
    messages = [
        pb2.UploadPhotoRequest(metadata=pb2.UploadPhotoMetadata(source_file_name="", collection_id="col-a")),
        pb2.UploadPhotoRequest(chunk=b"data"),
    ]

    with pytest.raises(AbortCalled) as exc_info:
        await service.UploadPhoto(_FakeRequestIterator(messages), FakeServicerContext())
    assert exc_info.value.code == grpc.StatusCode.INVALID_ARGUMENT
