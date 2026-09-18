"""gRPC client for NinjagoScanner.CatalogService, ported from CatalogGrpcClient.cs."""

from __future__ import annotations

import grpc
from google.protobuf import empty_pb2

from picture_service._generated import catalog_pb2, catalog_pb2_grpc
from picture_service.models import CatalogCardInfo, CatalogSnapshot, SeriesInfo


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


async def load_catalog_cards(service_address: str) -> list[CatalogCardInfo]:
    """Loads every card across all series via `ListAllCards` - used by stage 3
    (picture-service-catalog-matching) to resolve a card number within a matched series.
    `card_class` is always `None` for now: `CatalogCardEntry` doesn't carry a class field yet
    (see catalog-service-card-class, not yet shipped) - see CatalogCardInfo's docstring.
    """
    async with grpc.aio.insecure_channel(_strip_scheme(service_address)) as channel:
        client = catalog_pb2_grpc.CardCatalogStub(channel)
        response = await client.ListAllCards(empty_pb2.Empty())

        return [
            CatalogCardInfo(
                series_name=card.series_name,
                card_number=card.card_number,
                card_name=card.card_name,
                category=card.category,
            )
            for card in response.cards
        ]


async def load_catalog_snapshot(service_address: str) -> CatalogSnapshot:
    """Loads everything stage 3 needs from CatalogService for one analysis - series (for series
    resolution) and per-card data (for card-number resolution within a resolved series)."""
    series = await load_series_catalog(service_address)
    cards = await load_catalog_cards(service_address)
    return CatalogSnapshot(series=tuple(series), cards=tuple(cards))
