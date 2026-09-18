from picture_service.models import AnalysisStatuses, CatalogCardInfo, CatalogSnapshot, SeriesInfo, VerifiedMatch
from picture_service.series_catalog_service import match_catalog

SERIE_1 = SeriesInfo(serie="Serie 1", jahr=2016, besonderheiten=("Kein Logo auf den Karten",))
SERIE_2 = SeriesInfo(
    serie="Serie 2",
    jahr=2017,
    besonderheiten=("Logo: Schlange",),
    card_names=("Kai ZX", "Jay ZX"),
)
SERIE_3 = SeriesInfo(serie="Serie 3", jahr=2018, besonderheiten=("Logo: Drache",))

CATALOG_SERIES = [SERIE_1, SERIE_2, SERIE_3]

SERIE_2_CARDS = [
    CatalogCardInfo(series_name="Serie 2", card_number="1", card_name="Kai ZX", category="Heroes"),
    CatalogCardInfo(series_name="Serie 2", card_number="2", card_name="Jay ZX", category="Heroes"),
]


def snapshot(series=CATALOG_SERIES, cards=()) -> CatalogSnapshot:
    return CatalogSnapshot(series=tuple(series), cards=tuple(cards))


# --- Series resolution (picture-service-series-name-matching) ---


def test_exact_series_name_match_case_and_whitespace_insensitive():
    result = match_catalog({"series_name": "  serie 2  "}, {}, snapshot())
    assert result.set_name == "Serie 2"


def test_series_matched_by_symbol_logo_hint():
    result = match_catalog({}, {"reasoning": "Ich sehe das Symbol Schlange auf der Karte"}, snapshot())
    assert result.set_name == "Serie 2"


def test_series_matched_by_known_card_name():
    result = match_catalog({"card_name": "Kai ZX"}, {}, snapshot())
    assert result.set_name == "Serie 2"


def test_series_matched_by_year_when_no_stronger_signal():
    result = match_catalog({}, {"reasoning": "Jahr 2018 stand auf der Verpackung"}, snapshot())
    assert result.set_name == "Serie 3"


def test_no_symbol_evidence_favors_serie_1():
    result = match_catalog({}, {"reasoning": "Karte zeigt kein Symbol in der Ecke"}, snapshot())
    assert result.set_name == "Serie 1"


def test_scoring_tie_yields_no_series_match():
    tied_catalog = [SeriesInfo(serie="Serie 4", jahr=2018), SeriesInfo(serie="Serie 5", jahr=2018)]
    result = match_catalog({}, {"reasoning": "Das Jahr 2018 ist zu sehen"}, snapshot(series=tied_catalog))

    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.set_name is None


def test_empty_catalog_falls_back_to_raw_guess_trimmed():
    result = match_catalog({"series_name": "  Mystery Set  "}, {}, snapshot(series=[]))
    assert result.set_name == "Mystery Set"
    assert result.analysis_status == AnalysisStatuses.FAILED


def test_no_evidence_at_all_returns_no_match():
    result = match_catalog({}, {}, snapshot())
    assert result.set_name is None
    assert result.analysis_status == AnalysisStatuses.FAILED


# --- Judged status combination (picture-service-catalog-matching) ---


def test_no_confident_series_match_is_failed_and_skips_card_number():
    result = match_catalog({}, {"series_name": "Mystery Set"}, snapshot(cards=SERIE_2_CARDS))

    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.set_name == "Mystery Set"
    assert result.card_number is None


def test_confident_series_and_card_number_is_ok():
    result = match_catalog(
        {"card_number": "1"}, {"series_name": "Serie 2"}, snapshot(cards=SERIE_2_CARDS)
    )

    assert result.analysis_status == AnalysisStatuses.OK
    assert result.set_name == "Serie 2"
    assert result.card_number == "1"
    assert result.card_name == "Kai ZX"


def test_confident_series_but_no_confident_card_number_is_failed():
    result = match_catalog(
        {"card_number": "99"}, {"series_name": "Serie 2"}, snapshot(cards=SERIE_2_CARDS)
    )

    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.set_name == "Serie 2"
    assert result.card_number == "99"  # raw guess preserved, not dropped


# --- Card number resolution (picture-service-catalog-matching) ---


def test_exact_card_number_match_within_resolved_series():
    result = match_catalog({"card_number": "2"}, {"series_name": "Serie 2"}, snapshot(cards=SERIE_2_CARDS))
    assert result.card_number == "2"
    assert result.card_name == "Jay ZX"


def test_card_number_resolved_by_evidence_scoring_when_no_exact_match():
    result = match_catalog(
        {"detected_text": "Ich sehe Jay ZX auf der Karte"}, {"series_name": "Serie 2"}, snapshot(cards=SERIE_2_CARDS)
    )
    assert result.card_number == "2"
    assert result.card_name == "Jay ZX"


def test_card_number_tie_yields_no_match():
    tied_cards = [
        CatalogCardInfo(series_name="Serie 2", card_number="1", card_name="Kai ZX", category="Heroes"),
        CatalogCardInfo(series_name="Serie 2", card_number="2", card_name="Kai ZX", category="Villains"),
    ]
    result = match_catalog(
        {"detected_text": "Kai ZX ist zu sehen"}, {"series_name": "Serie 2"}, snapshot(cards=tied_cards)
    )

    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.card_number is None


def test_class_narrows_ambiguous_card_number_candidates():
    tied_cards = [
        CatalogCardInfo(
            series_name="Serie 2", card_number="1", card_name="Kai ZX", category="Heroes", card_class="character"
        ),
        CatalogCardInfo(
            series_name="Serie 2", card_number="2", card_name="Kai ZX", category="Vehicles", card_class="vehicle"
        ),
    ]
    result = match_catalog(
        {"detected_text": "Kai ZX ist zu sehen"},
        {"series_name": "Serie 2", "class": "vehicle"},
        snapshot(cards=tied_cards),
    )

    assert result.analysis_status == AnalysisStatuses.OK
    assert result.card_number == "2"


def test_no_series_resolved_means_card_number_not_attempted():
    result = match_catalog({"card_number": "1"}, {}, snapshot(series=[], cards=SERIE_2_CARDS))

    assert result.set_name is None
    assert result.card_number is None
    assert result.analysis_status == AnalysisStatuses.FAILED


# --- Unresolved match preserves raw guess (picture-service-catalog-matching) ---


def test_unresolved_series_keeps_raw_guess():
    result = match_catalog({}, {"series_name": "Mystery Set"}, snapshot())

    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.set_name == "Mystery Set"


# --- Verified series and card number survive re-analysis (picture-service-catalog-matching) ---


def test_verified_match_keeps_series_and_card_number_despite_contradicting_evidence():
    result = match_catalog(
        {"card_number": "2"},
        {"series_name": "Serie 3", "rarity": "rare", "language": "en"},
        snapshot(cards=SERIE_2_CARDS),
        VerifiedMatch(set_name="Serie 2", card_number="1"),
    )

    assert result.analysis_status == AnalysisStatuses.OK
    assert result.set_name == "Serie 2"
    assert result.card_number == "1"
    assert result.card_name == "Kai ZX"
    assert result.rarity == "rare"
    assert result.language == "en"


def test_verified_match_is_ok_even_when_nothing_would_resolve():
    result = match_catalog({}, {}, snapshot(cards=SERIE_2_CARDS), VerifiedMatch(set_name="Serie 2", card_number="2"))

    assert result.analysis_status == AnalysisStatuses.OK
    assert result.set_name == "Serie 2"
    assert result.card_number == "2"
    assert result.card_name == "Jay ZX"


def test_verified_match_not_in_catalog_falls_back_to_card_name_guess():
    result = match_catalog(
        {}, {"card_name": "Zane"}, snapshot(cards=SERIE_2_CARDS), VerifiedMatch(set_name="Serie 2", card_number="99")
    )

    assert result.set_name == "Serie 2"
    assert result.card_number == "99"
    assert result.card_name == "Zane"

