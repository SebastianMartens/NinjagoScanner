---
id: "6.1"
title: Account deletion
status: wanted
priority: must
updated: 2026-11-27
---

# Account deletion

## Why
A user must be able to leave and take their photos and cards with them. The app can delete single
photos today, but not an account.

## Features
None written yet.

## Acceptance
- A signed-in user can delete their own account in the app, without asking the operator.
- Before deleting, the app says in simple German what will be lost and asks for the password.
- Deletion is immediate and final; there is no waiting period or restore.
- The other person's trade history keeps the entry, including the deleted user's name.
- The name stays blocked: nobody can register it again.
- Afterwards nobody can sign in to the account or find it.
- Deletion permanently removes the account, its collection with all photos and card data, its
  friendships, and its achievements, experience points and rank. Only the name is kept, as above.
- Open trades are cancelled. Cards already traded away stay with the person who received them.
- Non-functional: an interrupted deletion is completed later and never leaves photos behind.

## Notes
- A username, nickname or real, is kept after deletion on purpose: it does not need strict
  protection (Sebastian). Under EU data protection law it can still count as personal data (not
  legal advice).

## Open questions
- How can an account be deleted if the password is forgotten? Depends on the email decision in
  [Accounts and access](_index.md).
- Should the operator also be able to delete accounts? See
  [issue #32](https://github.com/SebastianMartens/NinjagoScanner/issues/32).

## Realised by
None yet.

## Brief
**Need:** Users, above all children and their parents, must be able to delete an account and its
photos and cards. Only single photos can be deleted today.

**Acceptance:**
- A signed-in user deletes their own account in the app, after a simple German warning and
  entering the password.
- Deletion is immediate and final.
- Others' trade history keeps the entry and the name; the name cannot be registered again.
- Afterwards nobody can sign in to the account or find it.
- Account, collection, photos, card data, friendships, achievements, experience points and rank
  are removed for good; only the name is kept.
- Open trades are cancelled; cards already traded stay with their receiver.
- An interrupted deletion is completed later and leaves no photos behind.

**Constraints:**
- Must work without an email address.
- Not included: password recovery and deletion of accounts by the operator.
- No GitHub issue yet: implementation is paused.
