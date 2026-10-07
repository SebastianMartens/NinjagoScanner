"""Rarity across the staged pipeline (catalog-owned-rarity): stage 1 hint, stage 2 derivation,
stage 3 scoring, and the catalog client carrying class + rarity."""

import pytest
from google.protobuf import empty_pb2

from picture_service import catalog_client
from picture_service._generated import catalog_pb2
from picture_service.card_analysis_stage_1_and_2 import (
    build_attribute_detection_messages,
    build_derived_attributes_messages,
    compute_derived_attributes,
)
from picture_service.card_analysis_stage_3 import match_catalog, resolve_card
from picture_service.models import AnalysisStatuses, CatalogCardInfo, CatalogSnapshot
from picture_service.prompts import DERIVED_ATTRIBUTES_PROMPT
from tests.test_card_analysis_stage_1_and_2 import FakeModel, make_config, ok_raw_result


def card(series, number, name, card_class="character", card_rarity=None) -> CatalogCardInfo:
    return CatalogCardInfo(
        series_name=series,
        card_number=number,
        card_name=name,
        category="Heroes",
        card_class=card_class,
        card_rarity=card_rarity,
    )


def snapshot(*cards) -> CatalogSnapshot:
    return CatalogSnapshot(cards=tuple(cards))


# --- Stage 1 / stage 2 prompts and derivation ---


def test_stage_1_asks_for_a_rarity_hint_but_does_not_classify():
    messages = build_attribute_detection_messages("card.jpg", b"fake-bytes")
    text = next(block for block in messages[0]["content"] if block["type"] == "text")["text"]

    assert "rarity_hint:" in text
    assert "Do not judge or classify the rarity" in text
    assert '"rarity"' not in text


def test_stage_2_asks_for_common_or_limited_rarity():
    assert '"rarity"' in DERIVED_ATTRIBUTES_PROMPT
    assert "common, limited" in DERIVED_ATTRIBUTES_PROMPT
    assert "legendary" not in DERIVED_ATTRIBUTES_PROMPT

    messages = build_derived_attributes_messages({"rarity_hint": "card number LE6"})
    assert "rarity_hint: card number LE6" in messages[0]["content"]


@pytest.mark.parametrize("rarity", ["common", "limited", "legendary"])
async def test_derived_recognized_rarity_is_kept(rarity):
    result = await compute_derived_attributes(FakeModel([ok_raw_result({"rarity": rarity})]), make_config(), {})

    assert result.attributes["rarity"] == rarity


async def test_derived_unrecognized_rarity_is_dropped():
    result = await compute_derived_attributes(
        FakeModel([ok_raw_result({"rarity": "rare", "card_number": "7"})]), make_config(), {}
    )

    assert "rarity" not in result.attributes
    assert result.attributes["card_number"] == "7"


async def test_derived_without_rarity_stays_without():
    result = await compute_derived_attributes(FakeModel([ok_raw_result({"card_number": "7"})]), make_config(), {})

    assert "rarity" not in result.attributes


# --- Stage 3 scoring ---


def test_rarity_breaks_a_tie_between_otherwise_equal_cards():
    cards = [
        card("Serie 4", "9", "Fire", card_rarity="common"),
        card("Serie 5", "9", "Fire", card_rarity="limited"),
    ]
    derived = {"card_number": "9", "card_name": "Fire", "class": "character"}

    assert match_catalog(derived, snapshot(*cards)).analysis_status == AnalysisStatuses.FAILED

    result = match_catalog({**derived, "rarity": "limited"}, snapshot(*cards))
    assert (result.analysis_status, result.set_name) == (AnalysisStatuses.OK, "Serie 5")

    result = match_catalog({**derived, "rarity": "common"}, snapshot(*cards))
    assert (result.analysis_status, result.set_name) == (AnalysisStatuses.OK, "Serie 4")


def test_all_four_signals_score_the_maximum_of_110():
    # Exposed through resolve_card's scoring only indirectly: a card that matches everything
    # beats one that misses just the rarity.
    best = card("Serie 1", "1", "Kai", card_rarity="limited")
    other = card("Serie 2", "1", "Kai", card_rarity="common")

    winner = resolve_card("1", "Kai", "character", [other, best], None, "limited")

    assert winner is best


def test_rarity_alone_is_not_a_match():
    cards = [card("Serie 1", str(n), f"Card {n}", card_rarity="limited") for n in range(1, 6)]

    result = match_catalog({"rarity": "limited"}, snapshot(*cards))

    assert result.analysis_status == AnalysisStatuses.FAILED


def test_rarity_does_not_lift_a_partial_class_match_over_the_threshold():
    # class (20) + a partial name (<30) stays below 50; rarity (10) must not push it over.
    cards = [card("Serie 1", "1", "Jay ZX", card_rarity="common")]

    result = match_catalog({"card_name": "Jay Z", "class": "character", "rarity": "common"}, snapshot(*cards))

    assert result.analysis_status == AnalysisStatuses.FAILED


def test_rarity_mismatch_keeps_the_number_points():
    cards = [card("Serie 1", "185", "Golden Weapons", card_rarity="common")]

    result = match_catalog(
        {"card_number": "185", "card_name": "Golden Weapons", "class": "character", "rarity": "limited"},
        snapshot(*cards),
    )

    assert (result.analysis_status, result.set_name) == (AnalysisStatuses.OK, "Serie 1")


def test_unknown_derived_rarity_or_catalog_rarity_gives_no_signal():
    cards = [
        card("Serie 4", "9", "Fire", card_rarity=None),
        card("Serie 5", "9", "Fire", card_rarity="limited"),
    ]
    derived = {"card_number": "9", "card_name": "Fire", "class": "character"}

    # A derived rarity outside the fixed set is ignored, so the tie stays a tie.
    assert match_catalog({**derived, "rarity": "rare"}, snapshot(*cards)).analysis_status == AnalysisStatuses.FAILED


# --- Class scoring is effective (catalog client carries class + rarity) ---


class _FakeChannel:
    async def __aenter__(self):
        return self

    async def __aexit__(self, *exc):
        return False


class _FakeStub:
    def __init__(self, channel):
        pass

    async def ListAllCards(self, request):
        assert isinstance(request, empty_pb2.Empty)
        response = catalog_pb2.ListAllCardsResponse()
        response.cards.add(
            series_name="Serie 1", category="Special", card_number="LE1", card_name="Sensei Wu", **{"class": "limited edition"}, rarity="limited"
        )
        response.cards.add(series_name="Serie 1", category="Heroes", card_number="1", card_name="Kai")
        return response


async def test_catalog_client_carries_class_and_rarity(monkeypatch):
    monkeypatch.setattr(catalog_client.grpc.aio, "insecure_channel", lambda address: _FakeChannel())
    monkeypatch.setattr(catalog_client.catalog_pb2_grpc, "CardCatalogStub", _FakeStub)

    cards = await catalog_client.load_catalog_cards("http://localhost:5073")

    limited, plain = cards
    assert (limited.card_class, limited.card_rarity) == ("limited edition", "limited")
    assert (plain.card_class, plain.card_rarity) == (None, None)


def test_class_scoring_is_effective_with_catalog_classes():
    cards = [
        card("Serie 1", "50", "Ninja Car", card_class="vehicle"),
        card("Serie 1", "51", "Ninja Hero", card_class="character"),
    ]

    # The number matches a card of another class: no number points, so no match.
    assert match_catalog({"card_number": "50", "class": "character"}, snapshot(*cards)).analysis_status == (
        AnalysisStatuses.FAILED
    )
    result = match_catalog({"card_number": "50", "class": "vehicle"}, snapshot(*cards))
    assert result.card_name == "Ninja Car"
