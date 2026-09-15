from picture_service.models import SeriesInfo
from picture_service.series_catalog_service import GeminiCardPayload, resolve_set_name

SERIE_1 = SeriesInfo(serie="Serie 1", jahr=2016, besonderheiten=("Kein Logo auf den Karten",))
SERIE_2 = SeriesInfo(
    serie="Serie 2",
    jahr=2017,
    besonderheiten=("Logo: Schlange",),
    card_names=("Kai ZX", "Jay ZX"),
)
SERIE_3 = SeriesInfo(serie="Serie 3", jahr=2018, besonderheiten=("Logo: Drache",))

CATALOG = [SERIE_1, SERIE_2, SERIE_3]


def test_exact_series_name_match_case_and_whitespace_insensitive():
    payload = GeminiCardPayload(set_name="  serie 2  ")

    assert resolve_set_name(payload, CATALOG) == "Serie 2"


def test_series_matched_by_symbol_logo_hint():
    payload = GeminiCardPayload(set_name="unbekannt", reasoning_summary="Ich sehe das Symbol Schlange auf der Karte")

    assert resolve_set_name(payload, CATALOG) == "Serie 2"


def test_series_matched_by_known_card_name():
    payload = GeminiCardPayload(set_name=None, card_name="Kai ZX")

    assert resolve_set_name(payload, CATALOG) == "Serie 2"


def test_series_matched_by_year_when_no_stronger_signal():
    payload = GeminiCardPayload(set_name=None, reasoning_summary="Jahr 2018 stand auf der Verpackung")

    assert resolve_set_name(payload, CATALOG) == "Serie 3"


def test_no_symbol_evidence_favors_serie_1():
    payload = GeminiCardPayload(set_name=None, reasoning_summary="Karte zeigt kein Symbol in der Ecke")

    assert resolve_set_name(payload, CATALOG) == "Serie 1"


def test_scoring_tie_yields_no_match():
    tied_catalog = [
        SeriesInfo(serie="Serie 4", jahr=2018),
        SeriesInfo(serie="Serie 5", jahr=2018),
    ]
    payload = GeminiCardPayload(set_name=None, reasoning_summary="Das Jahr 2018 ist zu sehen")

    assert resolve_set_name(payload, tied_catalog) is None


def test_empty_catalog_falls_back_to_raw_guess_trimmed():
    payload = GeminiCardPayload(set_name="  Mystery Set  ")

    assert resolve_set_name(payload, []) == "Mystery Set"


def test_empty_catalog_with_no_set_name_returns_none():
    payload = GeminiCardPayload(set_name=None)

    assert resolve_set_name(payload, []) is None


def test_no_evidence_at_all_returns_none():
    payload = GeminiCardPayload()

    assert resolve_set_name(payload, CATALOG) is None
