"""The CardPictureService gRPC service, ported RPC-by-RPC from PictureScannerGrpcService.cs."""

from __future__ import annotations

import asyncio
import dataclasses
import json
import logging
import time
import uuid
from collections.abc import AsyncIterator, Awaitable, Callable

import grpc
from opentelemetry import trace

from picture_service import card_analysis, catalog_client
from picture_service._generated import picture_service_pb2 as pb2
from picture_service._generated import picture_service_pb2_grpc as pb2_grpc
from picture_service.card_analysis_stage_1_and_2 import StructuredModel, build_chat_model
from picture_service.config import SUPPORTED_EXTENSIONS, ScannerConfig
from picture_service.models import (
    AnalysisStatuses,
    CardAnalysisResult,
    CatalogSnapshot,
    Languages,
    ReviewStatuses,
    SidecarRecord,
    VerifiedMatch,
    utc_now,
)
from picture_service.photo_store import PhotoStore
from picture_service.sidecar_store import SidecarStore

logger = logging.getLogger("picture_service")
_tracer = trace.get_tracer("picture_service.scan")

ModelFactory = Callable[[ScannerConfig, dict], StructuredModel]
CatalogLoader = Callable[[str], Awaitable[CatalogSnapshot]]


class PictureScannerService(pb2_grpc.CardPictureServiceServicer):
    def __init__(
        self,
        sidecar_store: SidecarStore,
        photo_store: PhotoStore,
        *,
        build_model: ModelFactory = build_chat_model,
        load_catalog_snapshot: CatalogLoader = catalog_client.load_catalog_snapshot,
    ) -> None:
        self._sidecar_store = sidecar_store
        self._photo_store = photo_store
        self._build_model = build_model
        self._load_catalog_snapshot = load_catalog_snapshot

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
            max_attempts=request.max_attempts if request.HasField("max_attempts") else None,
            timeout_seconds=request.timeout_seconds if request.HasField("timeout_seconds") else None,
        )

        if not config.api_key:
            return pb2.ScanSummary(has_configuration_error=True, message="GEMINI_API_KEY ist nicht gesetzt.")

        try:
            with _tracer.start_as_current_span("catalog.load_snapshot"):
                catalog = await self._load_catalog_snapshot(config.catalog_service_address)
        except Exception:
            logger.exception("Katalog-Service unter %s nicht erreichbar", config.catalog_service_address)
            return pb2.ScanSummary(
                has_configuration_error=True,
                message=f"Der CatalogService unter '{config.catalog_service_address}' ist nicht erreichbar.",
            )

        if not catalog.series:
            return pb2.ScanSummary(has_configuration_error=True, message="Der CatalogService hat keine Seriendaten geliefert.")

        with _tracer.start_as_current_span("photo_store.list_photo_ids"):
            photo_ids = sorted([photo_id async for photo_id in self._photo_store.list_photo_ids(request.collection_id)])

        trace.get_current_span().set_attributes(
            {
                "scan.total_images": len(photo_ids),
                "scan.model": config.model,
                "scan.overwrite_existing": config.overwrite_existing_sidecars,
                "scan.delay_between_requests_ms": config.delay_between_requests_ms,
                "scan.max_attempts": config.max_attempts,
                "scan.timeout_seconds": config.timeout_seconds,
            }
        )

        if not photo_ids:
            return pb2.ScanSummary(total_images=0, message="Im Foto-Bucket wurden keine Kartenbilder gefunden.")

        processed_count = 0
        skipped_count = 0
        failed_count = 0
        uncertain_count = 0
        stopped_early = False

        for index, photo_id in enumerate(photo_ids):
            with _tracer.start_as_current_span(
                "scan.photo", attributes={"photo.id": photo_id, "photo.index": index}
            ) as photo_span:
                existing = await self._sidecar_store.get(request.collection_id, photo_id)

                if _should_skip_existing_sidecar(existing, config.overwrite_existing_sidecars):
                    photo_span.set_attribute("scan.outcome", "skipped")
                    skipped_count += 1
                    continue

                source_file_name = existing.source_file_name if existing and existing.source_file_name else photo_id
                verified = _verified_match(existing)

                try:
                    with _tracer.start_as_current_span("photo_store.get_bytes") as fetch_span:
                        image_bytes = await self._photo_store.get_bytes(request.collection_id, photo_id)
                        fetch_span.set_attribute("image.bytes", len(image_bytes))
                    result = await card_analysis.analyze_card(
                        self._build_model, config, catalog, photo_id, source_file_name, image_bytes, verified
                    )
                except Exception as exception:
                    logger.exception("Unerwarteter Fehler bei der Analyse von %s", photo_id)
                    result = CardAnalysisResult(
                        photo_id=photo_id,
                        analysis_status=AnalysisStatuses.FAILED,
                        source_file_name=source_file_name,
                        ai_model=config.model,
                        scanned_at_utc=utc_now(),
                        set_name=verified.set_name if verified else None,
                        card_number=verified.card_number if verified else None,
                        error_message=f"Unerwarteter Fehler: {exception}",
                        is_transport_failure=True,
                    )

                if existing is not None and existing.review_status:
                    result = dataclasses.replace(result, review_status=existing.review_status)

                with _tracer.start_as_current_span("sidecar.set_from_analysis_result"):
                    await self._sidecar_store.set_from_analysis_result(request.collection_id, photo_id, result)

                photo_span.set_attribute("scan.outcome", result.analysis_status.lower())
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
                with _tracer.start_as_current_span("scan.delay_between_requests"):
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

        if metadata.skip_analysis:
            # Batch upload: only the file name is recorded, so a later Scan (which retries anything
            # not ok/uncertain) picks the photo up. Deliberately before ScannerConfig/catalog are
            # touched - no Gemini key or CatalogService is needed for this path.
            await self._sidecar_store.set_record(
                metadata.collection_id,
                photo_id,
                SidecarRecord(source_file_name=source_file_name, analysis_status=AnalysisStatuses.NOT_ANALYZED),
            )
            record = await self._sidecar_store.get(metadata.collection_id, photo_id)
            return pb2.UploadPhotoResponse(card=_to_card_entry(photo_id, record))

        config = ScannerConfig.load_for_upload(
            api_key=metadata.api_key if metadata.HasField("api_key") else None,
            model=metadata.model if metadata.HasField("model") else None,
            catalog_service_address=metadata.catalog_service_address if metadata.HasField("catalog_service_address") else None,
            max_attempts=metadata.max_attempts if metadata.HasField("max_attempts") else None,
            timeout_seconds=metadata.timeout_seconds if metadata.HasField("timeout_seconds") else None,
        )

        result = await self._analyze_stored_photo(config, context, photo_id, source_file_name, image_bytes)

        await self._sidecar_store.set_from_analysis_result(metadata.collection_id, photo_id, result)

        record = await self._sidecar_store.get(metadata.collection_id, photo_id)
        return pb2.UploadPhotoResponse(card=_to_card_entry(photo_id, record))

    async def _analyze_stored_photo(
        self,
        config: ScannerConfig,
        context: grpc.aio.ServicerContext,
        photo_id: str,
        source_file_name: str,
        image_bytes: bytes,
        verified: VerifiedMatch | None = None,
    ) -> CardAnalysisResult:
        """The analysis steps UploadPhoto and ReanalyzePhoto share: aborts if the API key or the
        catalog is unavailable, otherwise runs the staged pipeline. An unexpected exception
        becomes a failed result flagged as a transport failure (same as Scan), so callers that
        must not overwrite a good sidecar with it can tell it apart from a content failure."""
        if not config.api_key:
            await context.abort(grpc.StatusCode.FAILED_PRECONDITION, "GEMINI_API_KEY ist nicht gesetzt.")

        try:
            with _tracer.start_as_current_span("catalog.load_snapshot"):
                catalog = await self._load_catalog_snapshot(config.catalog_service_address)
        except Exception:
            logger.exception("Katalog-Service unter %s nicht erreichbar", config.catalog_service_address)
            await context.abort(
                grpc.StatusCode.UNAVAILABLE, f"Der CatalogService unter '{config.catalog_service_address}' ist nicht erreichbar."
            )

        try:
            with _tracer.start_as_current_span("analyze_stored_photo", attributes={"photo.id": photo_id}):
                return await card_analysis.analyze_card(
                    self._build_model, config, catalog, photo_id, source_file_name, image_bytes, verified
                )
        except Exception as exception:
            logger.exception("Unerwarteter Fehler bei der Analyse von %s", photo_id)
            return CardAnalysisResult(
                photo_id=photo_id,
                analysis_status=AnalysisStatuses.FAILED,
                source_file_name=source_file_name,
                ai_model=config.model,
                scanned_at_utc=utc_now(),
                set_name=verified.set_name if verified else None,
                card_number=verified.card_number if verified else None,
                error_message=f"Unerwarteter Fehler: {exception}",
                is_transport_failure=True,
            )

    # --- ReanalyzePhoto ---

    async def ReanalyzePhoto(
        self, request: pb2.ReanalyzePhotoRequest, context: grpc.aio.ServicerContext
    ) -> pb2.ReanalyzePhotoResponse:
        if not request.photo_id.strip():
            await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Keine photo_id angegeben.")
        await _ensure_collection_id(request.collection_id, context)

        if not await self._photo_store.exists(request.collection_id, request.photo_id):
            await context.abort(grpc.StatusCode.NOT_FOUND, f"Foto '{request.photo_id}' wurde im Speicher nicht gefunden.")

        existing = await self._sidecar_store.get(request.collection_id, request.photo_id)
        source_file_name = existing.source_file_name if existing and existing.source_file_name else request.photo_id
        image_bytes = await self._photo_store.get_bytes(request.collection_id, request.photo_id)

        result = await self._analyze_stored_photo(
            ScannerConfig.load_for_upload(), context, request.photo_id, source_file_name, image_bytes, _verified_match(existing)
        )

        # Unlike UploadPhoto/Scan, a transport failure must not replace what's already there:
        # the caller is asking to improve an existing result, and a Gemini outage would
        # otherwise silently downgrade it to "failed".
        if result.is_transport_failure:
            await context.abort(grpc.StatusCode.UNAVAILABLE, result.error_message or "Gemini ist nicht erreichbar.")

        # Re-read now rather than reusing `existing`: the analysis takes seconds, and a review
        # status set meanwhile must not be reverted.
        latest = await self._sidecar_store.get(request.collection_id, request.photo_id)
        if latest is not None and latest.review_status:
            result = dataclasses.replace(result, review_status=latest.review_status)

        await self._sidecar_store.set_from_analysis_result(request.collection_id, request.photo_id, result)

        record = await self._sidecar_store.get(request.collection_id, request.photo_id)
        return pb2.ReanalyzePhotoResponse(card=_to_card_entry(request.photo_id, record))

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

    async def GetPhotoDownloadUrls(
        self, request: pb2.GetPhotoDownloadUrlsRequest, context: grpc.aio.ServicerContext
    ) -> pb2.GetPhotoDownloadUrlsResponse:
        await _ensure_collection_id(request.collection_id, context)

        # Callers build this list from locally-held data that can be slightly stale, so an ID with
        # no stored photo is omitted rather than failing the whole call.
        photo_ids = list(dict.fromkeys(request.photo_ids))

        async def resolve(photo_id: str) -> pb2.PhotoDownloadUrl | None:
            if not await self._photo_store.exists(request.collection_id, photo_id):
                return None
            download_url = await self._photo_store.create_download_url(request.collection_id, photo_id)
            return pb2.PhotoDownloadUrl(photo_id=photo_id, download_url=download_url)

        resolved = await asyncio.gather(*(resolve(photo_id) for photo_id in photo_ids))
        return pb2.GetPhotoDownloadUrlsResponse(urls=[entry for entry in resolved if entry is not None])

    # --- Listing ---

    async def ListCards(self, request: pb2.ListCardsRequest, context: grpc.aio.ServicerContext) -> pb2.ListCardsResponse:
        if not request.collection_id.strip():
            await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Keine collection_id angegeben.")

        response = pb2.ListCardsResponse()

        # Diagnostic spans only (no behavior change): at collection sizes in the thousands, the
        # bulk DynamoDB scan and the paginated S3 listing are each plausible bottlenecks and were
        # previously invisible - aioboto3/botocore has no automatic OTel instrumentation, so every
        # AWS call otherwise vanishes into one opaque ListCards span. A span per photo would flood
        # the trace at collection scale, so the per-photo cache-get cost is accumulated and
        # reported as an attribute instead. Download URLs are not resolved here - see
        # GetPhotoDownloadUrls.
        with _tracer.start_as_current_span("sidecar_store.warm_from_store"):
            await self._sidecar_store.warm_from_store(request.collection_id)

        with _tracer.start_as_current_span("photo_store.list_photo_ids"):
            photo_ids = [photo_id async for photo_id in self._photo_store.list_photo_ids(request.collection_id)]

        with _tracer.start_as_current_span(
            "list_cards.build_entries", attributes={"list_cards.photo_count": len(photo_ids)}
        ) as build_span:
            cache_get_seconds = 0.0

            for photo_id in photo_ids:
                start = time.perf_counter()
                record = await self._sidecar_store.get(request.collection_id, photo_id)
                cache_get_seconds += time.perf_counter() - start

                response.cards.append(_to_card_entry(photo_id, record))

            build_span.set_attributes({"list_cards.cache_get_seconds": cache_get_seconds})

        return response

    async def ListSourceFileNames(
        self, request: pb2.ListSourceFileNamesRequest, context: grpc.aio.ServicerContext
    ) -> pb2.ListSourceFileNamesResponse:
        await _ensure_collection_id(request.collection_id, context)

        names = {name async for name in self._sidecar_store.list_source_file_names(request.collection_id)}
        return pb2.ListSourceFileNamesResponse(source_file_names=sorted(names))

    async def GetCardDetails(
        self, request: pb2.GetCardDetailsRequest, context: grpc.aio.ServicerContext
    ) -> pb2.GetCardDetailsResponse:
        if not request.photo_id.strip():
            await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Keine photo_id angegeben.")
        if not request.collection_id.strip():
            await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Keine collection_id angegeben.")

        record = await self._sidecar_store.get(request.collection_id, request.photo_id)
        return pb2.GetCardDetailsResponse(details=_to_card_details(request.photo_id, record))

    # --- Sidecar editing ---

    async def UpdateSidecar(self, request: pb2.UpdateSidecarRequest, context: grpc.aio.ServicerContext) -> pb2.UpdateSidecarResponse:
        await _ensure_collection_id(request.collection_id, context)
        existing = await self._sidecar_store.get(request.collection_id, request.photo_id) or SidecarRecord()

        updated = SidecarRecord(
            analysis_status=_normalize_nullable(request.analysis_status),
            card_name=_normalize_nullable(request.card_name),
            card_number=_normalize_nullable(request.card_number),
            set_name=_normalize_nullable(request.set_name),
            rarity=_normalize_nullable(request.rarity),
            language=_normalize_nullable(request.language),
            error_message=_normalize_nullable(request.error_message),
            review_status=_normalize_nullable(request.review_status),
            # Not settable via this RPC (see picture-service-staged-analysis-pipeline's task
            # 7.1 - confidence/reasoning_summary/detected_text are PictureService-internal-only
            # now that nothing in the staged pipeline populates them) - preserved as-is, same as
            # the other fields below.
            confidence=existing.confidence,
            reasoning_summary=existing.reasoning_summary,
            detected_text=existing.detected_text,
            scanned_at_utc=existing.scanned_at_utc,
            source_file_name=existing.source_file_name,
            ai_model=existing.ai_model,
            raw_model_response=existing.raw_model_response,
            detected=existing.detected,
            derived=existing.derived,
        )

        await self._sidecar_store.set_record(request.collection_id, request.photo_id, updated)
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
        existing = await self._sidecar_store.get(collection_id, photo_id) or SidecarRecord()
        updated = dataclasses.replace(existing, **{field_name: _normalize_nullable(value)})
        await self._sidecar_store.set_record(collection_id, photo_id, updated)

    # --- Maintenance ---

    async def MigrateSidecars(
        self, request: pb2.MigrateSidecarsRequest, context: grpc.aio.ServicerContext
    ) -> pb2.MigrateSidecarsResponse:
        """Idempotently repairs legacy sidecar records on two independent fronts (a record can
        need either, both, or neither): the pre-existing `status` -> `AnalysisStatus` rename,
        and picture-service-sidecar-sections' flat-to-three-section shape (a legacy record's
        `detected`/`derived` are `None` - see SidecarRecord's docstring - until explicitly set
        to `{}` here, moving its existing fields into the Judged section as a no-op since they
        were never nested under a literal `Detected`/`Derived` key to begin with)."""
        total_files = 0
        migrated = 0
        already_current = 0
        errors = 0

        async for collection_id, photo_id, record in self._sidecar_store.list_all():
            total_files += 1

            needs_status_migration = not (record.analysis_status and record.analysis_status.strip())
            needs_sections_migration = record.detected is None or record.derived is None

            if not needs_status_migration and not needs_sections_migration:
                already_current += 1
                continue

            try:
                repaired = record
                if needs_status_migration:
                    repaired = dataclasses.replace(
                        repaired,
                        analysis_status=AnalysisStatuses.FAILED,
                        error_message=repaired.error_message or "Sidecar-Datensatz wurde ohne AnalysisStatus migriert.",
                    )
                if needs_sections_migration:
                    repaired = dataclasses.replace(
                        repaired,
                        detected=repaired.detected if repaired.detected is not None else {},
                        derived=repaired.derived if repaired.derived is not None else {},
                    )
                await self._sidecar_store.set_record(collection_id, photo_id, repaired)
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
        await self._sidecar_store.remove(request.collection_id, request.photo_id)

        return pb2.DeletePhotoResponse(success=True)


def _should_skip_existing_sidecar(existing: SidecarRecord | None, overwrite_existing_sidecars: bool) -> bool:
    """A photo is skipped only when it already has a sidecar recording a completed analysis
    (ok or uncertain) and overwrite wasn't requested. A missing sidecar, one recording failed,
    or one that isn't analyzed yet is always retry-eligible."""
    if overwrite_existing_sidecars or existing is None:
        return False

    status = (existing.analysis_status or "").lower()
    return status in (AnalysisStatuses.OK, AnalysisStatuses.UNCERTAIN)


def _verified_match(existing: SidecarRecord | None) -> VerifiedMatch | None:
    """A human-verified sidecar's series and card number are pinned during re-analysis. A
    verified record missing either value has nothing to pin, so it is analyzed like any other."""
    if existing is None or existing.review_status != ReviewStatuses.VERIFIED:
        return None
    if not existing.set_name or not existing.card_number:
        return None
    return VerifiedMatch(set_name=existing.set_name, card_number=existing.card_number)


async def _ensure_collection_id(collection_id: str, context: grpc.aio.ServicerContext) -> None:
    """Guards every collection-scoped RPC per collection-scoped-picture-access's "Every
    collection-scoped RPC requires a collection_id". MigrateSidecars is the one deliberate
    exception (it's a global maintenance RPC) and does not call this."""
    if not collection_id.strip():
        await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Keine collection_id angegeben.")


def _extension(source_file_name: str) -> str:
    dot_index = source_file_name.rfind(".")
    return source_file_name[dot_index:].lower() if dot_index != -1 else ""


def _to_card_entry(photo_id: str, sidecar: SidecarRecord | None) -> pb2.CardEntry:
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
    )


def _normalize_analysis_status(status: str | None) -> str:
    """Reports a recognized status as-is; anything else (missing sidecar, unset field, or a
    legacy/unrecognized value such as the retired "pending") falls back to NotAnalyzed."""
    if status and status.lower() in (AnalysisStatuses.OK, AnalysisStatuses.UNCERTAIN, AnalysisStatuses.FAILED):
        return status
    return AnalysisStatuses.NOT_ANALYZED


def _to_card_details(photo_id: str, sidecar: SidecarRecord | None) -> pb2.CardDetails:
    return pb2.CardDetails(
        photo_id=photo_id,
        scanned_at_utc=sidecar.scanned_at_utc.isoformat() if sidecar and sidecar.scanned_at_utc else "",
        error_message=sidecar.error_message if sidecar and sidecar.error_message else "",
        attributes_json=_attributes_json(sidecar),
    )


def _attributes_json(sidecar: SidecarRecord | None) -> str:
    """The staged pipeline's Detected/Derived output, for debugging/quality checks on the
    Review page - see picture-service-staged-analysis-pipeline's task 7.1."""
    detected = sidecar.detected if sidecar and sidecar.detected else {}
    derived = sidecar.derived if sidecar and sidecar.derived else {}
    return json.dumps({"detected": detected, "derived": derived}, ensure_ascii=False, sort_keys=True)


def _normalize_nullable(value: str | None) -> str | None:
    return value.strip() if value and value.strip() else None
