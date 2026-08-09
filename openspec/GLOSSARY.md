# Ubiquitous Language Glossary

This glossary defines the shared vocabulary (Domain-Driven Design "ubiquitous
language") for NinjagoScanner: the domain of cataloging and scanning Lego
Ninjago collectible cards. Use these terms consistently in conversation,
OpenSpec artifacts, and code/UI naming where practical.

Terms are grouped by area. Cross-references to other terms in this glossary
are written in **bold**.

## Catalog & Series

### Series
A collection identified by a unique Series Name (string), e.g. "Serie 7 Next
Level", combined with a release year. The catalog's top-level grouping for
**Card**s; each card belongs to exactly one series.

### Series Symbol
The small icon/logo printed on a card to identify which **Series** it
belongs to (stored as `Logo` in **Series Metadata**). Series 1 cards carry no
symbol ("Kein Logo"). Used both for human identification and as a visual cue
the scanning pipeline can match against when resolving a card's series.

### Category
A grouping of cards within a **Series** by role or theme (e.g. "Good Guys").
A card belongs to exactly one category within its series; category is an
attribute of a card, not part of its identity.

### Card
A single catalog entry identified uniquely by its **Series** and **Card
Number**. Category is a grouping attribute, not part of identity. Card name
is also an attribute of a card, and may have multiple language-variant
values for the same card.

### Card Number
The identifying number printed on a card within its series (e.g. `42`, or
prefixed forms like `LE1`, `XXL3`). Combined with series, it uniquely
identifies a **Card**. Sorting treats plain numeric values, `LE`-prefixed,
`XXL`-prefixed, and other formats as separate ordered groups.

### Known Card Name
The catalog's recorded name for a **Card** at a given number within a
series (e.g. "Kai", "Ultra Zane"). Used both to display card identity and as
evidence when matching a scanned photo to a series.

### Series Metadata
Descriptive information about a **Series** beyond its structural entry —
year, logo, theme, and highlights — used for richer catalog presentation
(e.g. on the **Collection Overview** detail pane).

## Photo & Scanning Pipeline

### Card Photo
A digital photo of a physical card, taken to document ownership and to be
analyzed by the scanning pipeline. Stored as an image file in the **Card
Photos Directory**.

### Card Photos Directory
The shared folder (`cardFotos`) holding all **Card Photo**s and their
**Sidecar** files, used by **Picture Service**, **Catalog Service**
(indirectly), and the **Web App** as the common storage location for a
person's collection photos.

### Sidecar
A record stored alongside a **Card Photo** holding everything known about
it: its **Analysis Status**, detected card data (name, number, set name,
rarity), **Language**, **Confidence**, **Reasoning Summary**, **Detected
Text**, and its independent **Review Status**. Created automatically on
first scan, or manually when a person edits a card before it's scanned.

### AI Analysis
The batch operation that sends unanalyzed (or explicitly re-requested) card
photos in a directory to the Gemini analysis pipeline, writing a **Sidecar**
for each and reporting a summary of processed/skipped/uncertain/failed
counts.

### Analysis Status
The machine-produced outcome of an **AI Analysis** for a card photo:
`pending` (not yet analyzed), `ok`, `uncertain` (low confidence or
model-reported), or `failed`. Set automatically by the pipeline, never by a
human.

### Confidence
A value between 0 and 1 reported by the **AI Analysis** expressing how sure
the model is about the detected card data. Below a threshold (0.65), the
**Analysis Status** is downgraded to `uncertain` regardless of what the
model itself reported.

### Reasoning Summary
A short explanation from the **AI Analysis** describing why it identified a
card the way it did — useful for a human reviewing an `uncertain` or
`failed` result.

### Detected Text
The raw pieces of text the **AI Analysis** read off a card photo (e.g.
printed name, number, rarity markings), stored on the **Sidecar** as
supporting evidence alongside the interpreted fields.

### Language
The printed language of a scanned card, recorded on its **Sidecar** as `de`
(German), `en` (English), or `unknown`. Detected by **AI Analysis** from the
card's printed text and character names, or set manually by a person.
Purely descriptive: it does not affect a catalog **Card**'s identity or its
**Owned Copies** count. A card photo with no explicit `Language` value —
whether it has never been analyzed or its **Sidecar** predates this field —
defaults to `de` without requiring a re-analysis or a rewrite of the
sidecar file; an explicit `unknown` from a completed analysis is never
overwritten by that default.

### Series Name
The series name as resolved onto a **Sidecar** — either detected by **AI
Analysis** (via **Series Name Matching**) or set manually by a person.
Distinct from the catalog **Series** in that it lives on a photo's sidecar
and may be null/unmatched.

### Series Name Matching
The process of resolving a freeform series guess (from **AI Analysis**) to a
known catalog **Series Name** — first by exact match, then by
evidence-based scoring using the **Series Symbol**, **Known Card Name**s,
and year mentioned in the photo's detected data. Ties result in no match
rather than a guess.

## Human Review

### Review Status
A human-only judgment on a card photo's **Sidecar**, independent of
**Analysis Status** and **Confidence**: `unreviewed` (default), `verified`
(a person confirmed the detected data matches the photo), or `incorrect` (a
person flagged it as wrong or incomplete). Never changed automatically by
**AI Analysis** or other edits.

## Collection & Ownership

### Collection Overview
The view that merges every catalog **Card** with a person's owned **Card
Photo**s, showing which cards are owned, missing, or duplicated, and letting
a person inspect and edit the **Sidecar** behind a selected card's photo.

### Owned Copies
The number of scanned **Card Photo**s whose **Series Name** and **Card
Number** match a given catalog **Card**, after normalization. Zero means the
card is missing; more than one means it's a duplicate.

### Unmapped Photo
A scanned **Card Photo** whose **Series Name** and **Card Number** don't
match any catalog **Card** after normalization — e.g. from a typo, an
unrecognized series, or a card not yet in the catalog.

### Overview
The application's home page ("/"), currently hosting only the **AI
Analysis** trigger button. Not to be confused with the **Collection
Overview** ("/collection"), which merges the catalog with owned photos —
Overview is a lightweight entry point, with room for later at-a-glance
collection status.

## System / Software Components

### Catalog Service
The software component that owns the reference data — all **Series**, their
**Series Metadata**, and every **Card** — and answers lookups from the other
components. It doesn't know about photos or scanning.

### Picture Service
The software component that manages **Card Photo**s and their **Sidecar**s:
receiving uploads, running **AI Analysis**, and applying manual sidecar
edits. It consults the **Catalog Service** for known series when analyzing
or matching photos.

### Web App
The software component a person actually uses: the **Collection Overview**,
gallery and table views, photo upload, and review screens. It talks to both
**Catalog Service** and **Picture Service** on the person's behalf.
