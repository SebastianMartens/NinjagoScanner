"""Gemini prompts for the staged analysis pipeline (see gemini_service.py).

Kept apart from the call/retry logic so prompt wording can be tuned without touching it. All
prompts are English. German strings that appear inside them ("Fahrzeug", "Falle", "Aktion") are
the literal card text the model has to recognise, not prompt language.
"""

# Stage 1 (picture-service-attribute-detection): vision call, photo only.
# TODO: definition of art cards, LE and XXL cards.
# TODO: define glossy / gold effect?
ATTRIBUTE_DETECTION_PROMPT = """\
You see exactly one photo of a Lego Ninjago trading card.
I provide you with a list of attributes detected from the photo.
Please try to identify for each attribute if you can detect its value from the photo.
Return a value for every attribute, but only what you can detect from the photo, as a flat JSON object of
Key-value pairs (no nested objects or lists).
Don't return markdown or code blocks. Return only valid JSON.

Attributes detected from the photo:

vehicle_image: true if the picture shows a vehicle (e.g. car, jet, submarine etc.). False otherwise.

vehicle_tag: true if the card has the german text "Fahrzeug" (maybe translated, e.g. "Vehicle" for english cards) written
 vertically (written bottom-to-top) in the upper right corner of the card.

vehicle_card_numbers: true if the picture shows one or two big numbers on a vehicle card. Vehicle cards most often have one or two big numbers written
 in the horizontal center of the card (lower third). The numbers are prefixed with a plus or minus sign.

text_box: true if the card has a text box. False otherwise. Some cards feature a text box (text is not an overlay of the card image
but has a textbox with different background colour). Text boxes are usually located in the lower part of the card,
 have colored background and small text.

text_box_text: Detected textbox-text. Some cards feature a text box (text is not an overlay of the card image but has
 a textbox with different background colour). Text boxes are usually located in the lower part of the card,
 have colored background and small text.

defense_value: the integer defense value of the card, 0 if not visible in the photo. Typically shown as a big green number
centered at the left edge of the card (written vertically top-down).

tempo_value: the integer tempo value of the card, 0 if not visible in the photo. Typically shown as a big yellow number
centered at the top edge of the card (written horizontally left-to-right).

attack_value: the integer attack value of the card, 0 if not visible in the photo. Typically shown as a big red number
centered at the right edge of the card (written horizontally bottom-to-top).

power_value: the integer power value of the card, 0 if not visible in the photo. Typically shown as a big blue number
centered at the bottom edge of the card (written horizontally left-to-right).

character_image: true if the picture shows one or more characters. False otherwise. Characters are
 images of a ninjago hero or villain (e.g. Kai, Jane, etc. or a snake, or person).

card_name: Card name as written on the card. Empty if not detectable. A card name usually is written in big letters in the center of the card.

puzzle-piece_card: true if the picture shows a puzzle-piece card. False otherwise. Puzzle-piece cards typically
don't have any text or numbers written on the card. Most puzzle-cards have a red border at one or two edges of the card.

trap_tag: true if the card has the german text "Falle" (maybe translated, e.g. "Trap" for english cards) written
 vertically (written bottom-to-top) in the upper right corner of the card.

action_tag: true if the card has the german text "Aktion" (maybe translated, e.g. "Action" for english cards) written
 vertically (written bottom-to-top) in the upper right corner of the card.

card_number: the number of the card, 0 if not visible in the photo. Card numbers are always smaller than other numbers or text
and shown in the lower left corner of the card. Puzzle cards may have the card number shown in the lower right corner.

card_found: true if the picture shows any trading card. False if you see something else.
"""

# Stage 2 (picture-service-derived-attributes): text-only call. The detected attributes are
# appended as "key: value" lines, so this must end with a newline.
# TODO: add definitions for art cards, limited edition cards, and any other special card types.
DERIVED_ATTRIBUTES_PROMPT = """\
You are given the attributes detected from a card photo as key-value pairs (no image).
Derive further attributes from them and return them as a flat JSON object of key-value pairs
(no nested objects or lists as values).
Return only valid JSON, without markdown or code blocks. Don't repeat the input-attributes.

In particular, derive:
- "class": a coarse card class, exclusively one of the following values:
  character, action, vehicle, trap, puzzle-piece, limited edition, art.
  Hints of how to identify the class:
  "character": character cards have detected values for "attack_value", "power_value", 
  "defense_value" and "tempo_value".
  "action": action cards have the "action_tag" detected as true, a text_box and detected text_box_text.
  "vehicle": vehicle cards have the "vehicle_tag" detected as true, a text_box and detected text_box_text.
  "trap": trap cards have the "trap_tag" detected as true, a text_box and detected text_box_text.  
  "puzzle-piece": puzzle-piece cards have the "puzzle-piece_card" detected as true. 
  Puzzle-piece cards and art cards typically have no card_name attribute.  
- "card_number": take over card number from the detected attributes.
- "card_name": take over card name from the detected attributes.
- "card_name_en": the English name of the card, used to look the card up in an English-only
  catalog. If the card name is already English, repeat it. If it is German or Polish, give the
  official English Ninjago name of that card if you know it (e.g. "Feuer-Drache" -> "Fire Dragon"),
  otherwise a faithful translation. Keep character names (Lloyd, Kai, Garmadon, ...) as they are.
  Omit it when there is no card_name.
- "language": the presumed language of the card text ("de", "en", "pl" or "unknown").
  Take texts from the following attributes into account to determine the language: 
  "card_name", "text_box_text".

Detected attributes:

"""
