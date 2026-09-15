"""The CardPictureService gRPC servicer, ported RPC-by-RPC from PictureScannerGrpcService.cs."""

from __future__ import annotations

import asyncio
import dataclasses
import logging
import uuid
from collections.abc import AsyncIterator, Awaitable, Callable

import grpc

from picture_service import catalog_client, gemini_service
from picture_service._generated import picture_service_pb2 as pb2
from picture_service._generated import picture_service_pb2_grpc as pb2_grpc
from picture_service.config import SUPPORTED_EXTENSIONS, ScannerConfig
from picture_service.models import AnalysisStatuses, CardAnalysisResult, Languages, ReviewStatuses, SidecarRecord, SeriesInfo, utc_now
from picture_service.photo_store import PhotoStore
from picture_service.sidecar_cache import SidecarCache

logger = logging.getLogger("picture_service")

ModelFactory = Callable[[ScannerConfig], gemini_service.StructuredModel]
CatalogLoader = Callable[[str], Awaitable[list[SeriesInfo]]]


class PictureScannerService(pb2_grpc.CardPictureServiceServicer):
    def __init__(
        self,
        sidecar_cache: SidecarCache,
        photo_store: PhotoStore,
        *,
        build_model: ModelFactory = gemini_service.build_chat_model,
        load_series_catalog: CatalogLoader = catalog_client.load_series_catalog,
    ) -> None:
        self._sidecar_cache = sidecar_cache
        self._photo_store = photo_store
        self._build_model = build_model
        self._load_series_catalog = load_series_catalog

    # --- Scan (bulk backfill) ---

    async def Scan(self, request: pb2.ScanRequest, context: grpc.aio.ServicerContext) -> pb2.ScanSummary:
        if not request.collection_id.strip():
            await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Keine collection_id angegeben.")

        config = ScannerConfig.load_for_scan(
            api_key=request.api_key if request.HasField("api_key") else None,
            model=request.model if request.HasField("model") else None,
            catalog_service_address=request.catalog_service_address if request.HasField("catalog_service_address") else None,
            overwrite_existing_sidecars=request.overwrite_existing_sidecars if request.HasField("overwrite_existing_sidecars") else None,
            delay_between_requests_ms=request.delay_between_requests_ms if request.HasField("delay_between_requests_ms") else None,
            retry_delay_ms=request.retry_delay_ms if request.HasField("retry_delay_ms") else None,
            max_attempts=request.max_attempts if request.HasField("max_attempts") else None,
            timeout_seconds=request.timeout_seconds if request.HasField("timeout_seconds") else None,
        )

        if not config.api_key:
            return pb2.ScanSummary(has_configuration_error=True, message="GEMINI_API_KEY ist nicht gesetzt.")

        try:
            series_catalog = await self._load_series_catalog(config.catalog_service_address)
        except Exception:
            logger.exception("Katalog-Service unter %s nicht erreichbar", config.catalog_service_address)
            return pb2.ScanSummary(
                has_configuration_error=True,
                message=f"Der CatalogService unter '{config.catalog_service_address}' ist nicht erreichbar.",
            )

        if not series_catalog:
            return pb2.ScanSummary(has_configuration_error=True, message="Der CatalogService hat keine Seriendaten geliefert.")

        photo_ids = sorted([photo_id async for photo_id in self._photo_store.list_photo_ids(request.collection_id)])

        if not photo_ids:
            return pb2.ScanSummary(total_images=0, message="Im Foto-Bucket wurden keine Kartenbilder gefunden.")

        model = self._build_model(config)

        processed_count = 0
        skipped_count = 0
        failed_count = 0
        uncertain_count = 0
        stopped_early = False

        for index, photo_id in enumerate(photo_ids):
            existing = await self._sidecar_cache.get(request.collection_id, photo_id)

            if _should_skip_existing_sidecar(existing, config.overwrite_existing_sidecars):
                skipped_count += 1
                continue

            source_file_name = existing.source_file_name if existing and existing.source_file_name else photo_id

            try:
                image_bytes = await self._photo_store.get_bytes(request.collection_id, photo_id)
                result = await gemini_service.analyze_card(
                    model, config, series_catalog, photo_id, source_file_name, image_bytes
                )
            except Exception as exception:
                logger.exception("Unerwarteter Fehler bei der Analyse von %s", photo_id)
                result = CardAnalysisResult(
                    photo_id=photo_id,
                    analysis_status=AnalysisStatuses.FAILED,
                    source_file_name=source_file_name,
                    ai_model=config.model,
                    scanned_at_utc=utc_now(),
                    error_message=f"Unerwarteter Fehler: {exception}",
                    is_transport_failure=True,
                )

            if existing is not None and existing.review_status:
                result = dataclasses.replace(result, review_status=existing.review_status)

            await self._sidecar_cache.set_from_analysis_result(request.collection_id, photo_id, result)

            processed_count += 1
            if result.analysis_status.lower() == AnalysisStatuses.FAILED:
                failed_count += 1
            elif result.analysis_status.lower() == AnalysisStatuses.UNCERTAIN:
                uncertain_count += 1

            if result.is_transport_failure:
                stopped_early = True
                logger.warning(
                    "Scan wird nach %s abgebrochen: Gemini war ueber die Transportebene nicht erreichbar (%s)",
                    photo_id,
                    result.error_message,
                )
                break

            if index < len(photo_ids) - 1 and config.delay_between_requests_ms > 0:
                await asyncio.sleep(config.delay_between_requests_ms / 1000)

        return pb2.ScanSummary(
            total_images=len(photo_ids),
            processed=processed_count,
            skipped=skipped_count,
            uncertain=uncertain_count,
            failed=failed_count,
            stopped_early=stopped_early,
            message=(
                "Scan vorzeitig abgebrochen: Gemini war wiederholt nicht erreichbar. Spaeter erneut versuchen."
                if stopped_early
                else "Batch abgeschlossen."
            ),
        )

    # --- UploadPhoto (client streaming) ---

    async def UploadPhoto(
        self, request_iterator: AsyncIterator[pb2.UploadPhotoRequest], context: grpc.aio.ServicerContext
    ) -> pb2.UploadPhotoResponse:
        first_message = await anext(request_iterator, None)
        if first_message is None or first_message.WhichOneof("payload") != "metadata":
            await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Der Upload-Stream muss mit einer Metadaten-Nachricht beginnen.")

        metadata = first_message.metadata

        if not metadata.collection_id.strip():
            await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Keine collection_id angegeben.")

        source_file_name = metadata.source_file_name.strip() or "upload"
        extension = _extension(source_file_name)
        if extension not in SUPPORTED_EXTENSIONS:
            await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Dateityp wird nicht unterstuetzt. Erlaubt: JPG, PNG, BMP, WEBP.")

        chunks = bytearray()
        async for message in request_iterator:
            if message.WhichOneof("payload") != "chunk":
                await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Nach der Metadaten-Nachricht werden nur Byte-Chunks erwartet.")
            chunks.extend(message.chunk)

        if len(chunks) == 0:
            await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Die hochgeladene Datei ist leer.")

        photo_id = uuid.uuid4().hex
        image_bytes = bytes(chunks)
        await self._photo_store.put_bytes(metadata.collection_id, photo_id, image_bytes)

        config = ScannerConfig.load_for_upload(
            api_key=metadata.api_key if metadata.HasField("api_key") else None,
            model=metadata.model if metadata.HasField("model") else None,
            catalog_service_address=metadata.catalog_service_address if metadata.HasField("catalog_service_address") else None,
            retry_delay_ms=metadata.retry_delay_ms if metadata.HasField("retry_delay_ms") else None,
            max_attempts=metadata.max_attempts if metadata.HasField("max_attempts") else None,
            timeout_seconds=metadata.timeout_seconds if metadata.HasField("timeout_seconds") else None,
        )

        if not config.api_key:
            await context.abort(grpc.StatusCode.FAILED_PRECONDITION, "GEMINI_API_KEY ist nicht gesetzt.")

        try:
            series_catalog = await self._load_series_catalog(config.catalog_service_address)
        except Exception:
            logger.exception("Katalog-Service unter %s nicht erreichbar", config.catalog_service_address)
            await context.abort(
                grpc.StatusCode.UNAVAILABLE, f"Der CatalogService unter '{config.catalog_service_address}' ist nicht erreichbar."
            )

        try:
            model = self._build_model(config)
            result = await gemini_service.analyze_card(
                model, config, series_catalog, photo_id, source_file_name, image_bytes
            )
        except Exception as exception:
            logger.exception("Unerwarteter Fehler bei der Analyse von %s", photo_id)
            result = CardAnalysisResult(
                photo_id=photo_id,
                analysis_status=AnalysisStatuses.FAILED,
                source_file_name=source_file_name,
                ai_model=config.model,
                scanned_at_utc=utc_now(),
                error_message=f"Unerwarteter Fehler: {exception}",
            )

        await self._sidecar_cache.set_from_analysis_result(metadata.collection_id, photo_id, result)

        record = await self._sidecar_cache.get(metadata.collection_id, photo_id)
        return pb2.UploadPhotoResponse(card=_to_card_entry(photo_id, record))

    # --- Download URLs ---

    async def GetPhotoDownloadUrl(
        self, request: pb2.GetPhotoDownloadUrlRequest, context: grpc.aio.ServicerContext
    ) -> pb2.GetPhotoDownloadUrlResponse:
        if not request.photo_id.strip():
            await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Keine photo_id angegeben.")
        if not request.collection_id.strip():
            await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Keine collection_id angegeben.")

        if not await self._photo_store.exists(request.collection_id, request.photo_id):
            await context.abort(grpc.StatusCode.NOT_FOUND, f"Foto '{request.photo_id}' wurde im Speicher nicht gefunden.")

        download_url = await self._photo_store.create_download_url(request.collection_id, request.photo_id)
        return pb2.GetPhotoDownloadUrlResponse(download_url=download_url)

    # --- Listing ---

    async def ListCards(self, request: pb2.ListCardsRequest, context: grpc.aio.ServicerContext) -> pb2.ListCardsResponse:
        if not request.collection_id.strip():
            await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Keine collection_id angegeben.")

        response = pb2.ListCardsResponse()

        await self._sidecar_cache.warm_from_store(request.collection_id)

        async for photo_id in self._photo_store.list_photo_ids(request.collection_id):
            record = await self._sidecar_cache.get(request.collection_id, photo_id)
            download_url = await self._photo_store.create_download_url(request.collection_id, photo_id)
            response.cards.append(_to_card_entry(photo_id, record, download_url))

        return response

    async def GetCardDetails(
        self, request: pb2.GetCardDetailsRequest, context: grpc.aio.ServicerContext
    ) -> pb2.GetCardDetailsResponse:
        if not request.photo_id.strip():
            await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Keine photo_id angegeben.")
        if not request.collection_id.strip():
            await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Keine collection_id angegeben.")

        record = await self._sidecar_cache.get(request.collection_id, request.photo_id)
        return pb2.GetCardDetailsResponse(details=_to_card_details(request.photo_id, record))

    # --- Sidecar editing ---

    async def UpdateSidecar(self, request: pb2.UpdateSidecarRequest, context: grpc.aio.ServicerContext) -> pb2.UpdateSidecarResponse:
        await _ensure_collection_id(request.collection_id, context)
        existing = await self._sidecar_cache.get(request.collection_id, request.photo_id) or SidecarRecord()

        updated = SidecarRecord(
            analysis_status=_normalize_nullable(request.analysis_status),
            card_name=_normalize_nullable(request.card_name),
            card_number=_normalize_nullable(request.card_number),
            set_name=_normalize_nullable(request.set_name),
            rarity=_normalize_nullable(request.rarity),
            language=_normalize_nullable(request.language),
            confidence=request.confidence,
            reasoning_summary=_normalize_nullable(request.reasoning_summary),
            detected_text=tuple(text.strip() for text in request.detected_text if text.strip()),
            error_message=_normalize_nullable(request.error_message),
            review_status=_normalize_nullable(request.review_status),
            scanned_at_utc=existing.scanned_at_utc,
            source_file_name=existing.source_file_name,
            ai_model=existing.ai_model,
            raw_model_response=existing.raw_model_response,
        )

        await self._sidecar_cache.set_record(request.collection_id, request.photo_id, updated)
        return pb2.UpdateSidecarResponse(success=True)

    async def UpdateSetName(self, request: pb2.UpdateSetNameRequest, context: grpc.aio.ServicerContext) -> pb2.UpdateSetNameResponse:
        await self._apply_single_field_update(request.collection_id, request.photo_id, "set_name", request.set_name, context)
        return pb2.UpdateSetNameResponse(success=True)

    async def UpdateCardNumber(
        self, request: pb2.UpdateCardNumberRequest, context: grpc.aio.ServicerContext
    ) -> pb2.UpdateCardNumberResponse:
        await self._apply_single_field_update(request.collection_id, request.photo_id, "card_number", request.card_number, context)
        return pb2.UpdateCardNumberResponse(success=True)

    async def UpdateCardLanguage(
        self, request: pb2.UpdateCardLanguageRequest, context: grpc.aio.ServicerContext
    ) -> pb2.UpdateCardLanguageResponse:
        await self._apply_single_field_update(request.collection_id, request.photo_id, "language", request.language, context)
        return pb2.UpdateCardLanguageResponse(success=True)

    async def UpdateReviewStatus(
        self, request: pb2.UpdateReviewStatusRequest, context: grpc.aio.ServicerContext
    ) -> pb2.UpdateReviewStatusResponse:
        await self._apply_single_field_update(
            request.collection_id, request.photo_id, "review_status", request.review_status, context
        )
        return pb2.UpdateReviewStatusResponse(success=True)

    async def _apply_single_field_update(
        self, collection_id: str, photo_id: str, field_name: str, value: str, context: grpc.aio.ServicerContext
    ) -> None:
        await _ensure_collection_id(collection_id, context)
        existing = await self._sidecar_cache.get(collection_id, photo_id) or SidecarRecord()
        updated = dataclasses.replace(existing, **{field_name: _normalize_nullable(value)})
        await self._sidecar_cache.set_record(collection_id, photo_id, updated)

    # --- Maintenance ---

    async def MigrateSidecars(
        self, request: pb2.MigrateSidecarsRequest, context: grpc.aio.ServicerContext
    ) -> pb2.MigrateSidecarsResponse:
        total_files = 0
        migrated = 0
        already_current = 0
        errors = 0

        async for collection_id, photo_id, record in self._sidecar_cache.list_all():
            total_files += 1

            if record.analysis_status and record.analysis_status.strip():
                already_current += 1
                continue

            try:
                repaired = dataclasses.replace(
                    record,
                    analysis_status=AnalysisStatuses.FAILED,
                    error_message=record.error_message or "Sidecar-Datensatz wurde ohne AnalysisStatus migriert.",
                )
                await self._sidecar_cache.set_record(collection_id, photo_id, repaired)
                migrated += 1
            except Exception:
                logger.exception("Sidecar-Migration fuer %s/%s fehlgeschlagen", collection_id, photo_id)
                errors += 1

        return pb2.MigrateSidecarsResponse(
            total_files=total_files, migrated=migrated, already_current=already_current, errors=errors
        )

    async def DeletePhoto(self, request: pb2.DeletePhotoRequest, context: grpc.aio.ServicerContext) -> pb2.DeletePhotoResponse:
        await _ensure_collection_id(request.collection_id, context)

        if not await self._photo_store.exists(request.collection_id, request.photo_id):
            await context.abort(grpc.StatusCode.NOT_FOUND, f"Das Foto '{request.photo_id}' wurde nicht gefunden.")

        await self._photo_store.delete(request.collection_id, request.photo_id)
        await self._sidecar_cache.remove(request.collection_id, request.photo_id)

        return pb2.DeletePhotoResponse(success=True)


def _should_skip_existing_sidecar(existing: SidecarRecord | None, overwrite_existing_sidecars: bool) -> bool:
    """A photo is skipped only when it already has a sidecar recording a completed analysis
    (ok or uncertain) and overwrite wasn't requested. A missing sidecar, one recording failed,
    or one that isn't analyzed yet is always retry-eligible."""
    if overwrite_existing_sidecars or existing is None:
        return False

    status = (existing.analysis_status or "").lower()
    return status in (AnalysisStatuses.OK, AnalysisStatuses.UNCERTAIN)


async def _ensure_collection_id(collection_id: str, context: grpc.aio.ServicerContext) -> None:
    """Guards every collection-scoped RPC per collection-scoped-picture-access's "Every
    collection-scoped RPC requires a collection_id". MigrateSidecars is the one deliberate
    exception (it's a global maintenance RPC) and does not call this."""
    if not collection_id.strip():
        await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Keine collection_id angegeben.")


def _extension(source_file_name: str) -> str:
    dot_index = source_file_name.rfind(".")
    return source_file_name[dot_index:].lower() if dot_index != -1 else ""


def _to_card_entry(photo_id: str, sidecar: SidecarRecord | None, download_url: str = "") -> pb2.CardEntry:
    return pb2.CardEntry(
        photo_id=photo_id,
        source_file_name=sidecar.source_file_name if sidecar and sidecar.source_file_name else "",
        analysis_status=_normalize_analysis_status(sidecar.analysis_status if sidecar else None),
        card_name=sidecar.card_name if sidecar and sidecar.card_name else "",
        card_number=sidecar.card_number if sidecar and sidecar.card_number else "",
        set_name=sidecar.set_name if sidecar and sidecar.set_name else "",
        rarity=sidecar.rarity if sidecar and sidecar.rarity else "",
        language=sidecar.language if sidecar and sidecar.language else Languages.DEFAULT,
        review_status=sidecar.review_status if sidecar and sidecar.review_status else ReviewStatuses.UNREVIEWED,
        download_url=download_url,
    )


def _normalize_analysis_status(status: str | None) -> str:
    """Reports a recognized status as-is; anything else (missing sidecar, unset field, or a
    legacy/unrecognized value such as the retired "pending") falls back to NotAnalyzed."""
    if status and status.lower() in (AnalysisStatuses.OK, AnalysisStatuses.UNCERTAIN, AnalysisStatuses.FAILED):
        return status
    return AnalysisStatuses.NOT_ANALYZED


def _to_card_details(photo_id: str, sidecar: SidecarRecord | None) -> pb2.CardDetails:
    details = pb2.CardDetails(
        photo_id=photo_id,
        confidence=sidecar.confidence if sidecar else 0,
        reasoning_summary=sidecar.reasoning_summary if sidecar and sidecar.reasoning_summary else "",
        scanned_at_utc=sidecar.scanned_at_utc.isoformat() if sidecar and sidecar.scanned_at_utc else "",
        error_message=sidecar.error_message if sidecar and sidecar.error_message else "",
    )
    if sidecar and sidecar.detected_text:
        details.detected_text.extend(sidecar.detected_text)
    return details


def _normalize_nullable(value: str | None) -> str | None:
    return value.strip() if value and value.strip() else None
