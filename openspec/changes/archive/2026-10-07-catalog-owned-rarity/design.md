## Context

Rarity today is a free-text `Rarity` on the sidecar (proto `CardEntry.rarity = 6`, `UpdateSidecarRequest.rarity = 7`, DynamoDB attribute `Rarity`). Nothing sets it except the `/collection` edit form. Web reads it in the gallery tag (`CardTagHelper`), the unlock overlay (`UnlockOverlay.razor` colour switch) and gamification (`GamificationService` string compares for `legendary` / `limited edition`). The catalog has no rarity, only a per-category `Class`, one value of which is `limited edition`. See proposal.md for motivation.

Two facts found while reviewing shape the design:

- `CatalogCardEntry` already carries `class` (proto field 6), but `picture_service/catalog_client.py::load_catalog_cards` never copies it (`card_class` stays `None`, justified by a stale "not yet shipped" comment). Stage 3's class scoring and the "number with a different class is inconsistent" rule therefore never fire in production today.
- Stage 2 already derives a `class` that can be `limited edition`, so rarity `limited` is largely redundant with class for matching; it still earns its place as a separate, explicitly stored signal and as the vocabulary Web uses.

## Goals / Non-Goals

**Goals:**
- One definition of rarity (`common` | `limited` | `legendary`), owned by CatalogService and consumed everywhere else.
- Web features (gallery tag, overlay, achievements, review details, collection details) read rarity from the matched catalog card.
- The analysis pipeline detects, derives and scores rarity without ever writing it to the sidecar's Judged section.

**Non-Goals:**
- Assigning `legendary` (or any other non-class-derived rarity) to actual cards; the data model allows it, but no card is marked yet.
- Migrating or deleting stored `Rarity` attributes; they are ignored.
- Restoring manual editing of confidence / reasoning summary / detected text / error message / card name (lost with the collection edit form).

## Decisions

**D1 – Rarity is catalog data, seeded once from class.**
A `"Rarity"` key is added to `cardInfos/*.json`, declared like `"Class"`: on a category object, inherited by everything below it, and overridable on an individual card object (so one rare card inside a common category can be flagged later). A one-time migration script inserts `"Rarity": "limited"` after every `"Class": "limited edition"` line and `"Rarity": "common"` after every other `"Class"` line (text insertion, so the hand-formatted JSON stays untouched otherwise); the script is run once and not kept. After that, the data is the source of truth and rarity no longer follows class. `CatalogRepository` reads it with the same fail-fast rule as `Class` (a card without a rarity fails catalog loading) and exposes it as `CatalogCardEntry.rarity` (next free proto field number). Alternative: derive at load time on every start — rejected because a later "rare card in a common category" could then not be expressed.

**D2 – Rarity is a closed set, validated at both ends.**
The set is `common`, `limited`, `legendary`. CatalogService rejects any other value when loading (fail fast) and today no card is `legendary`. PictureService validates stage 2's `rarity` against the same set (mirrors `CARD_CLASSES`: a `CARD_RARITIES` constant, out-of-set value dropped from the *derived* map's effective use). Web uses constants/enum, no scattered string literals.

**D3 – Stage 1 reports observations, stage 2 decides.**
Stage 1 gets `rarity_hint`: a short free-text string of what is visible (LE prefix on the card number, holographic/gold finish, other markings), empty if none. It builds on the existing `shiny_finish` and the "LE" note in `card_number`; `shiny_finish` stays. Stage 2's prompt gets a `rarity` rule: `limited` when the hints indicate a limited edition (LE prefix, or derived class `limited edition`), else `common`. The prompt only offers `common` and `limited` since `legendary` cannot be recognised from a photo yet; validation accepts the full catalog set. Alternative: a boolean `limited_marking` in stage 1 — rejected for now because the user wants stage 1 to only collect hints; free text keeps stage 1 free of judgment and tolerates other markings (XXL, platinum) being reasoned about in stage 2.

**D4 – Scoring: +10 for equal rarity, no penalty, never decisive alone.**
Current: number 50 / class 20 / name 30 (max 100, threshold 50). Adding rarity as +10 (max 110) leaves every existing combination's score and the 50-point threshold unchanged, so no previously matching photo stops matching. Rarity cannot create a match candidate by itself (a candidate needs ≥50 from number/class/name). It acts as a tie-breaker, which is its real value: common vs. limited variants of otherwise identical numbers/names. No "rarity mismatch voids number" rule (unlike class) because a misjudged rarity is a likelier failure than a misjudged class and the class rule already guards the limited case. Alternative considered: re-split the 100 points (e.g. class 15 / rarity 5) — rejected: "class + exact name" would fall to 45 and stop being a candidate.

**D5 – Pass class and rarity through the catalog client (fixes the dormant class bug).**
`load_catalog_cards` sets `card_class=card.class_` (name per protobuf codegen) and `card_rarity=card.rarity`; empty string → `None`. This is a behavior change for class scoring that is already specified but never ran; tests must cover it, and the catalog-matching spec scenario "Class scoring is effective" pins it. Expect some photos to match differently after deploy (generally more correctly); worth a re-analysis spot check on a handful of known photos.

**D6 – Remove sidecar rarity entirely, reserve the proto numbers.**
In `picture_service.proto`: delete `rarity` from `CardEntry` and `UpdateSidecarRequest` and add `reserved 6;` / `reserved 7;` (+ `reserved "rarity";`) so the numbers can't be reused with another meaning against a rolling deploy. PictureService stops writing `Rarity`; `sidecar_table` read ignores an existing attribute. Web and PictureService deploy order: PictureService first is safe (Web sends an unknown field → ignored by protobuf), then Web; either order works because removed proto fields are tolerated by both sides.

**D7 – Web reads rarity from the catalog card, via the existing query service.**
`CollectionQueryService` already joins catalog cards with photos; it exposes the catalog card's rarity on `CardListItem` / `GalleryCardItem` / `CollectionCardDetails` / the review group instead of the photo's. The unlock-celebration path takes rarity from the matched catalog card (`matchedCard`), replacing the `after.Rarity` / `card.Rarity` arguments in `Review.razor` and `Upload.razor`. Gamification builds `LimitedEditionOwnedCards` from the catalog card's rarity; `LegendaryOwnedCards` / `first-legendary` stay but count catalog rarity `legendary` (currently zero cards).

**D8 – Presentation of rarity.**
Gallery tag and overlay chip appear only for non-`common` rarities (German labels "Limited" / "Legendär"); `common` shows nothing, since showing "common" on ~every tile is noise. The collection detail pane and review details ("Seltenheit") show every value ("Normal" / "Limited" / "Legendär"). `CardTagHelper.TagsForRarity` keeps its role but takes the catalog rarity. The overlay colour map keeps the existing purple for `limited` and the existing gold for `legendary`, with a default for `common`.

**D9 – Collection page: link to review, no edit form.**
Remove the sidecar form, draft model, save handler and the `UpdateSidecar` use from `Collection.razor`. Add a button ("Im Review öffnen") that navigates to `/review?series=…&card=…` — the same URL shape the gallery already builds (`Gallery.razor`) — shown only when the card has matching photos (the review page opens only existing groups). The review page already fully covers series, number, language, review status, deletion and re-analysis; the fields it cannot edit (confidence, reasoning summary, detected text, error message, card name) become read-only, which is accepted.

## Risks / Trade-offs

- [Class scoring turns on for the first time] → covered by D5 tests; re-analyse a few known photos before relying on it; the "different class voids number" rule could now unmatch photos whose class stage 2 misjudges. Mitigation: the rule is already specified and this restores intended behavior; verified photos are pinned and unaffected.
- [`first-legendary` cannot unlock until a card is marked `legendary`] → intended; a test fixture with a legendary catalog card keeps the path covered.
- [The one-time migration touches all 17 `cardInfos` files] → text insertion only, verified by a diff that shows only added `Rarity` lines and by catalog tests.
- [Manual editing capability loss in `/collection`] → accepted by the user; review page is the editing surface.
- [Stored `Rarity` attributes linger in DynamoDB] → harmless, ignored; can be cleaned up later with the existing `MigrateSidecars` mechanism if ever desired.
- [Almost every card is `common`, so rarity carries little matching signal] → intentional; it only breaks ties for limited editions.
- [Tests currently use JSON fixtures with `"Rarity": "Common"` / `LEGENDARY`] → remove from fixtures; add catalog-driven fixtures (a `limited`-class card) in `PictureServiceTestHost` and the catalog test host.

## Migration Plan

1. Run the one-time rarity migration on `cardInfos/*.json`, deploy CatalogService (adds `rarity`; additive, backward compatible).
2. Deploy PictureService (new prompts, scoring, catalog client; ignores legacy `Rarity`).
3. Deploy Web (reads catalog rarity; no sidecar rarity).
Rollback: reverse order is safe too because the proto changes are additive for CatalogService and tolerated-removal for PictureService. No data migration; legacy `Rarity` attributes stay untouched.
