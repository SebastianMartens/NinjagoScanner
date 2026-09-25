# web-unlock-feedback Specification

## Purpose

Defines the celebration feedback (card overlay, rank-up overlay, achievement toasts) the Web app shows when a person's action changes their collection's owned cards, XP, or achievements.

## Requirements

### Requirement: The duplicate card overlay states no XP gain
When the card overlay is shown for a photo that matches a catalog card the collection already owned (a duplicate), the overlay SHALL show the card and its owned-copies count ("Du besitzt diese Karte jetzt N×"). It SHALL NOT show an XP gain note, since duplicate copies earn no XP. The "Neue Karte" overlay SHALL continue to show its "+50 XP" note.

#### Scenario: Uploading a duplicate shows the overlay without XP
- **WHEN** a person uploads a photo that is matched to a catalog card the collection already owns one copy of
- **THEN** the "Dublette" overlay is shown with "Du besitzt diese Karte jetzt 2×" and no "+N XP" note

#### Scenario: Uploading a new card still shows its XP
- **WHEN** a person uploads a photo that is matched to a catalog card the collection didn't own before
- **THEN** the "Neue Karte" overlay is shown with a "+50 XP" note

### Requirement: Review corrections that resolve to an already-owned card raise no card overlay
A review correction that changes which catalog card a photo matches SHALL raise the "Neue Karte" overlay only when the photo is now the sole owned copy of that card. When the newly matched card has more than one owned copy after the correction, no card overlay, rank-up overlay, or XP notification SHALL be shown. Achievement toasts for achievements the correction newly unlocked SHALL still be shown, as for any other action with no card overlay.

#### Scenario: Correcting to an already-owned card is silent
- **WHEN** on the review page a person changes a photo's card number so it matches a catalog card the collection already owns another copy of, and no achievement unlocks
- **THEN** no overlay, toast, or other notification is shown

#### Scenario: Correcting to an already-owned card still toasts a newly unlocked achievement
- **WHEN** on the review page a person's correction makes a photo match an already-owned card, and that brings the collection's duplicate copies to 25
- **THEN** no card overlay is shown, and the `dupes-25` achievement toast is shown

#### Scenario: Correcting to a newly owned card still celebrates
- **WHEN** on the review page a person changes a photo's card number so it matches a catalog card the collection didn't own before
- **THEN** the "Neue Karte" overlay is shown with a "+50 XP" note
