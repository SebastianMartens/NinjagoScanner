"""gRPC client for NinjagoScanner.CatalogService, ported from CatalogGrpcClient.cs."""

from __future__ import annotations

import grpc

from picture_service._generated import catalog_pb2, catalog_pb2_grpc
from picture_service.models import SeriesInfo


def _strip_scheme(address: str) -> str:
    """grpc.aio.insecure_channel expects a bare host:port, but callers configure the
    CatalogService address as a URL (e.g. "http://localhost:5073"), matching the .NET
    GrpcChannel.ForAddress convention."""
    for prefix in ("http://", "https://"):
        if address.startswith(prefix):
            return address[len(prefix) :]
    return address


async def load_series_catalog(service_address: str) -> list[SeriesInfo]:
    async with grpc.aio.insecure_channel(_strip_scheme(service_address)) as channel:
        client = catalog_pb2_grpc.CardCatalogStub(channel)
        response = await client.ListSeries(
            catalog_pb2.ListSeriesRequest(include_known_card_names=True)
        )

        return [
            SeriesInfo(
                serie=series.series_name,
                jahr=series.year,
                besonderheiten=tuple(series.special_features),
                sondereditionen=tuple(series.special_editions),
                card_names=tuple(series.known_card_names),
            )
            for series in response.series
        ]
