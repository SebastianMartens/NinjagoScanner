"""gRPC server bootstrap, ported from Program.cs.

Env vars: PORT (gRPC port, default 8080), HEALTH_CHECK_PORT (optional plain-HTTP liveness
port - grpc.aio serves gRPC over its own HTTP/2 implementation, so unlike Kestrel it has no
ALPN conflict with a separate HTTP/1.1 port, but the split is kept to match the existing
Fly.io health-check setup).
"""

from __future__ import annotations

import asyncio
import logging
import os
import signal

import aioboto3
import grpc
from opentelemetry import trace
from opentelemetry.exporter.otlp.proto.http._log_exporter import OTLPLogExporter
from opentelemetry.exporter.otlp.proto.http.trace_exporter import OTLPSpanExporter
from opentelemetry.instrumentation.grpc import GrpcAioInstrumentorServer
from opentelemetry.sdk._logs import LoggerProvider, LoggingHandler
from opentelemetry.sdk._logs.export import BatchLogRecordProcessor
from opentelemetry.sdk.resources import SERVICE_NAME, Resource
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor

from picture_service._generated import picture_service_pb2_grpc as pb2_grpc
from picture_service.config import resolve_aws_region, resolve_photos_bucket_name, resolve_sidecar_table_name
from picture_service.photo_store import PhotoStore
from picture_service.picture_scanner_service import PictureScannerService
from picture_service.sidecar_cache import SidecarCache
from picture_service.sidecar_table import SidecarTable

logger = logging.getLogger("picture_service")

_SERVICE_NAME = "ninjago-scanner-picture-service"


def _resource() -> Resource:
    """Shared across tracing and logging so `service.name` can't drift between the two signals."""
    return Resource.create({SERVICE_NAME: _SERVICE_NAME})


def _configure_tracing(resource: Resource) -> None:
    """OTLP endpoint/headers (OTEL_EXPORTER_OTLP_ENDPOINT / OTEL_EXPORTER_OTLP_HEADERS) are
    read automatically by OTLPSpanExporter from the standard OTel environment variables. Uses
    the *-proto-http exporter package specifically (not the combined package that picks a
    protocol based on OTEL_EXPORTER_OTLP_PROTOCOL, defaulting to gRPC) so the protocol is
    unambiguously HTTP/protobuf, matching what Grafana Cloud's OTLP gateway accepts - see the
    .NET version's explicit HttpProtobuf override in the old Program.cs and design.md's Risk
    about not assuming the Python SDK's default matches.
    """
    provider = TracerProvider(resource=resource)
    provider.add_span_processor(BatchSpanProcessor(OTLPSpanExporter()))
    trace.set_tracer_provider(provider)
    GrpcAioInstrumentorServer().instrument()


def _configure_logging(resource: Resource) -> None:
    """Attaches an OTLP log handler to the root logger alongside the existing stdout
    `basicConfig` handler (both stay active), so every existing `logging.getLogger(...)` call
    site gains OTLP export with no call-site changes - mirroring the .NET services'
    `WithLogging(...)`. `LoggingHandler` reads the current OTel context on each log record,
    so trace/span IDs are attached automatically when a span is active (e.g. during an inbound
    RPC) and omitted otherwise - no manual correlation needed. Same *-proto-http exporter
    reasoning as `_configure_tracing`.
    """
    provider = LoggerProvider(resource=resource)
    provider.add_log_record_processor(BatchLogRecordProcessor(OTLPLogExporter()))
    logging.getLogger().addHandler(LoggingHandler(logger_provider=provider))

_LIVENESS_RESPONSE = (
    b"HTTP/1.1 200 OK\r\n"
    b"Content-Type: text/plain\r\n"
    b"Content-Length: 78\r\n"
    b"Connection: close\r\n"
    b"\r\n"
    b"This service exposes card photo scanning via gRPC. Use a gRPC client to call it."
)


async def _handle_liveness_connection(reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
    try:
        await reader.readline()
        writer.write(_LIVENESS_RESPONSE)
        await writer.drain()
    finally:
        writer.close()


async def _serve() -> None:
    logging.basicConfig(level=logging.INFO)
    resource = _resource()
    _configure_tracing(resource)
    _configure_logging(resource)

    grpc_port = int(os.environ.get("PORT", "8080"))
    health_check_port_raw = os.environ.get("HEALTH_CHECK_PORT")

    session = aioboto3.Session(region_name=resolve_aws_region())
    photo_store = PhotoStore(session, resolve_photos_bucket_name())
    sidecar_table = SidecarTable(session, resolve_sidecar_table_name())
    sidecar_cache = SidecarCache(sidecar_table)

    server = grpc.aio.server()
    pb2_grpc.add_CardPictureServiceServicer_to_server(PictureScannerService(sidecar_cache, photo_store), server)
    server.add_insecure_port(f"[::]:{grpc_port}")

    liveness_server = None
    if health_check_port_raw:
        liveness_server = await asyncio.start_server(_handle_liveness_connection, "::", int(health_check_port_raw))

    await server.start()
    logger.info("PictureService listening on port %s", grpc_port)

    stop_event = asyncio.Event()
    loop = asyncio.get_running_loop()
    for sig in (signal.SIGTERM, signal.SIGINT):
        try:
            loop.add_signal_handler(sig, stop_event.set)
        except NotImplementedError:
            # Windows' ProactorEventLoop doesn't support add_signal_handler - only relevant for
            # local dev, since this service runs on Linux (Fly.io) in production.
            signal.signal(sig, lambda *_: stop_event.set())

    await stop_event.wait()

    if liveness_server is not None:
        liveness_server.close()
    await server.stop(grace=5)


def main() -> None:
    asyncio.run(_serve())


if __name__ == "__main__":
    main()
