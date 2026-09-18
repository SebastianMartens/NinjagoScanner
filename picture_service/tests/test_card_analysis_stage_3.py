from picture_service.models import AnalysisStatuses, CatalogCardInfo, CatalogSnapshot, VerifiedMatch
from picture_service.card_analysis_stage_3 import match_catalog


def card(series, number, name, card_class=None, category="Heroes") -> CatalogCardInfo:
    return CatalogCardInfo(
        series_name=series, card_number=number, card_name=name, category=category, card_class=card_class
    )


# Card number 1 exists in every series, as in the real catalog - so the number alone rarely
# identifies a series and the name / class have to.
CARDS = [
    card("Serie 1", "1", "Kai", "character"),
    card("Serie 1", "2", "Jay", "character"),
    card("Serie 1", "185", "Golden Weapons", "limited edition", "Limited_Edition_Cards"),
    card("Serie 2", "1", "Kai ZX", "character"),
    card("Serie 2", "2", "Jay ZX", "character"),
    card("Serie 2", "50", "Ninja Car", "vehicle", "Vehicle_Cards"),
    card("Serie 3", "1", "Lord Garmadon", "character"),
    card("Serie 3", "50", "Dragon Rider", "vehicle", "Vehicle_Cards"),
]


def snapshot(cards=CARDS) -> CatalogSnapshot:
    return CatalogSnapshot(cards=tuple(cards))


# --- The card determines the series (picture-service-catalog-matching) ---


def test_number_and_exact_name_pick_the_card_and_its_series():
    result = match_catalog({"card_number": "1", "card_name": "Kai ZX", "class": "character"}, snapshot())

    assert result.analysis_status == AnalysisStatuses.OK
    assert (result.set_name, result.card_number, result.card_name) == ("Serie 2", "1", "Kai ZX")


def test_number_only_matches_when_only_one_series_has_that_number():
    result = match_catalog({"card_number": "185"}, snapshot())

    assert result.analysis_status == AnalysisStatuses.OK
    assert (result.set_name, result.card_number, result.card_name) == ("Serie 1", "185", "Golden Weapons")


def test_number_shared_by_every_series_with_no_name_is_a_tie():
    result = match_catalog({"card_number": "1"}, snapshot())

    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.set_name is None


def test_series_name_in_derived_attributes_is_not_used():
    result = match_catalog({"card_number": "1", "series_name": "Serie 3"}, snapshot())

    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.set_name is None


def test_exact_name_outranks_similar_name_for_the_same_number():
    result = match_catalog({"card_number": "1", "card_name": "Kai"}, snapshot())

    assert (result.set_name, result.card_name) == ("Serie 1", "Kai")


def test_name_is_compared_case_and_punctuation_insensitively():
    result = match_catalog({"card_number": "1", "card_name": "  lord GARMADON! "}, snapshot())

    assert (result.set_name, result.card_name) == ("Serie 3", "Lord Garmadon")


# --- Similar names (wrong language, partially detected) ---


def test_partially_detected_name_still_finds_the_card():
    result = match_catalog({"card_number": "2", "card_name": "Jay Z"}, snapshot())

    assert (result.set_name, result.card_name) == ("Serie 2", "Jay ZX")


def test_name_similar_to_catalog_name_still_finds_the_card():
    result = match_catalog({"card_number": "1", "card_name": "Lord Garmadone"}, snapshot())

    assert (result.set_name, result.card_name) == ("Serie 3", "Lord Garmadon")


def test_unrelated_name_adds_nothing():
    result = match_catalog({"card_number": "185", "card_name": "Totally Different"}, snapshot())

    assert result.card_name == "Golden Weapons"  # matched by number alone


def test_name_without_any_number_needs_a_class_to_be_enough():
    without_class = match_catalog({"card_name": "Jay ZX"}, snapshot())
    with_class = match_catalog({"card_name": "Jay ZX", "class": "character"}, snapshot())

    assert without_class.analysis_status == AnalysisStatuses.FAILED
    assert (with_class.analysis_status, with_class.set_name, with_class.card_number) == (
        AnalysisStatuses.OK,
        "Serie 2",
        "2",
    )


def test_class_with_only_a_partial_name_is_not_enough():
    result = match_catalog({"card_name": "Jay Z", "class": "character"}, snapshot())

    assert result.analysis_status == AnalysisStatuses.FAILED


# --- Card class ---


def test_class_breaks_a_tie_between_series_sharing_a_number():
    result = match_catalog({"card_number": "50", "class": "vehicle"}, snapshot())

    # Serie 2 and Serie 3 both have vehicle #50 - still a tie...
    assert result.analysis_status == AnalysisStatuses.FAILED

    result = match_catalog({"card_number": "50", "card_name": "Dragon Rider", "class": "vehicle"}, snapshot())

    # ...until the name separates them.
    assert (result.set_name, result.card_name) == ("Serie 3", "Dragon Rider")


def test_same_number_but_different_class_is_not_a_match():
    result = match_catalog({"card_number": "185", "class": "character"}, snapshot())

    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.set_name is None


def test_class_mismatch_does_not_let_the_number_win_over_a_card_of_the_right_class():
    cards = [card("Serie 1", "5", "Kai", "vehicle"), card("Serie 2", "9", "Kai", "character")]
    result = match_catalog({"card_number": "5", "card_name": "Kai", "class": "character"}, snapshot(cards))

    # Card 5 is the wrong class so its number earns nothing; the character named Kai wins.
    assert (result.set_name, result.card_number) == ("Serie 2", "9")


def test_unknown_derived_class_gives_no_signal():
    result = match_catalog({"card_number": "185", "class": "not-a-class"}, snapshot())

    assert (result.set_name, result.card_number) == ("Serie 1", "185")


def test_catalog_card_without_class_gives_no_signal():
    cards = [card("Serie 1", "7", "Kai")]
    result = match_catalog({"card_number": "7", "class": "vehicle"}, snapshot(cards))

    assert (result.set_name, result.card_number) == ("Serie 1", "7")


# --- Card number formats ---


def test_numeric_card_number_from_stage_two_is_matched():
    result = match_catalog({"card_number": 185}, snapshot())
    assert (result.set_name, result.card_number) == ("Serie 1", "185")


def test_float_and_padded_card_numbers_are_matched():
    assert match_catalog({"card_number": 185.0}, snapshot()).card_number == "185"
    assert match_catalog({"card_number": "0185"}, snapshot()).card_number == "185"


def test_card_number_zero_means_not_visible():
    result = match_catalog({"card_number": 0}, snapshot())

    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.card_number is None


# --- Same card listed under several categories ---


def test_same_series_and_number_in_two_categories_is_one_card():
    cards = [card("Serie 1", "9", "Kai", "character", "Heroes"), card("Serie 1", "9", "Kai", "character", "Good_Guys")]
    result = match_catalog({"card_number": "9"}, snapshot(cards))

    assert (result.analysis_status, result.set_name, result.card_number) == (AnalysisStatuses.OK, "Serie 1", "9")


# --- Unresolved match preserves the raw guess (picture-service-catalog-matching) ---


def test_unresolved_match_keeps_raw_number_and_name():
    result = match_catalog({"card_number": "999", "card_name": "Mystery", "language": "en"}, snapshot())

    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.set_name is None
    assert result.card_number == "999"
    assert result.card_name == "Mystery"
    assert result.language == "en"
    assert result.error_message


def test_empty_catalog_is_failed():
    result = match_catalog({"card_number": "1", "card_name": "Kai"}, snapshot([]))

    assert result.analysis_status == AnalysisStatuses.FAILED


def test_no_derived_attributes_is_failed():
    result = match_catalog({}, snapshot())

    assert result.analysis_status == AnalysisStatuses.FAILED
    assert result.card_number is None


# --- Verified series and card number survive re-analysis (picture-service-catalog-matching) ---


def test_verified_match_keeps_series_and_card_number_despite_contradicting_attributes():
    result = match_catalog(
        {"card_number": "1", "card_name": "Lord Garmadon", "language": "en"},
        snapshot(),
        VerifiedMatch(set_name="Serie 2", card_number="1"),
    )

    assert result.analysis_status == AnalysisStatuses.OK
    assert result.set_name == "Serie 2"
    assert result.card_number == "1"
    assert result.card_name == "Kai ZX"
    assert result.language == "en"


def test_verified_match_is_ok_even_when_nothing_would_resolve():
    result = match_catalog({}, snapshot(), VerifiedMatch(set_name="Serie 2", card_number="2"))

    assert result.analysis_status == AnalysisStatuses.OK
    assert result.set_name == "Serie 2"
    assert result.card_number == "2"
    assert result.card_name == "Jay ZX"


def test_verified_match_not_in_catalog_falls_back_to_card_name_guess():
    result = match_catalog({"card_name": "Zane"}, snapshot(), VerifiedMatch(set_name="Serie 2", card_number="99"))

    assert result.set_name == "Serie 2"
    assert result.card_number == "99"
    assert result.card_name == "Zane"
