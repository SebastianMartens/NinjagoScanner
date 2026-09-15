import grpc
import pytest

from picture_service._generated import picture_service_pb2 as pb2
from picture_service.models import AnalysisStatuses, Languages, ReviewStatuses, SeriesInfo, SidecarRecord
from picture_service.picture_scanner_service import PictureScannerService
from picture_service.sidecar_cache import SidecarCache
from tests.fakes import FakePhotoStore, FakeSidecarTable
from tests.grpc_fakes import AbortCalled, FakeServicerContext

CATALOG = [SeriesInfo(serie="Serie 1", jahr=2016)]


class FakeGeminiModel:
    def __init__(self, result):
        self._result = result

    async def ainvoke(self, messages):
        return self._result


def make_service(*, sidecar_table=None, photo_store=None, model_result=None, catalog=None, catalog_error=None):
    sidecar_table = sidecar_table or FakeSidecarTable()
    photo_store = photo_store or FakePhotoStore()
    cache = SidecarCache(sidecar_table)

    async def load_series_catalog(address):
        if catalog_error:
            raise catalog_error
        return catalog if catalog is not None else CATALOG

    def build_model(config):
        parsed = model_result or {
            "status": "ok",
            "cardName": "Kai",
            "cardNumber": "1",
            "setName": "Serie 1",
            "rarity": "common",
            "language": "de",
            "confidence": 0.9,
            "reasoningSummary": "clear",
            "detectedText": ["Kai"],
        }
        return _RawInvokeModel(parsed)

    return (
        PictureScannerService(cache, photo_store, build_model=build_model, load_series_catalog=load_series_catalog),
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
            confidence=0.9,
            reasoning_summary="looks right",
            detected_text=["Kai", "  ", "1"],
            review_status="verified",
        ),
        FakeServicerContext(),
    )

    updated = table.items[("col-a", "p1")]
    assert updated.card_name == "Kai"
    assert updated.detected_text == ("Kai", "1")
    assert updated.review_status == "verified"
    assert updated.source_file_name == "a.jpg"  # preserved


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


# --- Additional photo-download coverage ---


async def test_get_photo_download_url_not_found_when_photo_does_not_exist_anywhere():
    service, _, _ = make_service()

    with pytest.raises(AbortCalled) as exc_info:
        await service.GetPhotoDownloadUrl(
            pb2.GetPhotoDownloadUrlRequest(photo_id="missing", collection_id="col-a"), FakeServicerContext()
        )
    assert exc_info.value.code == grpc.StatusCode.NOT_FOUND


# --- Additional photo-upload coverage ---


async def test_upload_photo_missing_file_name_fails_with_invalid_argument():
    service, _, _ = make_service()
    messages = [
        pb2.UploadPhotoRequest(metadata=pb2.UploadPhotoMetadata(source_file_name="", collection_id="col-a")),
        pb2.UploadPhotoRequest(chunk=b"data"),
    ]

    with pytest.raises(AbortCalled) as exc_info:
        await service.UploadPhoto(_FakeRequestIterator(messages), FakeServicerContext())
    assert exc_info.value.code == grpc.StatusCode.INVALID_ARGUMENT
