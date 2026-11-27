---
id: "6"
title: Accounts and access
status: idea
priority: must
updated: 2026-11-27
---

# Accounts and access

## Why
Anyone can sign up, and each user needs a collection that is theirs alone and stays theirs.

## Capabilities
- [Account deletion](01-account-deletion.md): leave completely, with all data removed.

## Acceptance
All points are *(proposed)* until agreed.

- Anyone can register with a username and password, without giving personal data.
- Only the owner can change a collection.
- Guessing passwords and mass registration are held back.
- A child can sign up and sign in on a phone.
- A user can delete their account with its photos and cards (agreed).

## Open questions
- Should the app collect and verify an email address? Sebastian leans towards it; decision
  deferred. It would allow password recovery, but changes the non-goal of collecting no personal
  data and the "About" page. Required or optional, and whose address for a child, are open too.
- What happens when a user forgets their password? There is no recovery today; depends on the
  email decision.
- [Issue #31](https://github.com/SebastianMartens/NinjagoScanner/issues/31) proposes passkeys and
  mail confirmation. Mail confirmation conflicts with the non-goal of collecting no personal data.

## Realised by
- [web-user-authentication](../../openspec/specs/web-user-authentication/spec.md)
- [web-auth-rate-limiting](../../openspec/specs/web-auth-rate-limiting/spec.md)
- [web-collections](../../openspec/specs/web-collections/spec.md)
- [collection-scoped-picture-access](../../openspec/specs/collection-scoped-picture-access/spec.md)
