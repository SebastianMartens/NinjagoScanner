---
id: "1"
title: Safe for children
status: idea
priority: must
updated: 2026-11-27
---

# Safe for children

## Why
Children aged about 7–12 are the main audience, and anyone can register. A child must be able to
use the app without being exposed to strangers, costs or misuse of their data, and parents must be
able to trust what the app says about itself.

## Capabilities
None written yet.

## Acceptance
All points are *(proposed)* until agreed.

- Every outside service that receives photos or other data from users allows, under its current
  terms, an app mainly used by children.
- A stranger cannot find a child or contact them through the app.
- What the app says about data, third parties and costs matches what it does, in German a child
  or parent can understand.
- There is no chat, no public profile and no way to spend money.
- A user can delete their photos, and deleted photos are gone.
- A user can delete their account with its photos and cards (agreed); see
  [Account deletion](../06-accounts-and-access/01-account-deletion.md).

## Open questions

### Card recognition (urgent)
Left open on purpose, tracked in
[issue #40](https://github.com/SebastianMartens/NinjagoScanner/issues/40).

The [Gemini API terms](https://aistudio.google.com/docs/terms) (effective 2026-03-23) forbid use
in an app that is directed towards, or likely to be accessed by, people under 18. Card recognition
uses this API today. The same terms allow only paid use for apps offered to users in the EEA,
Switzerland or the UK.

- Which service replaces it, or is recognition switched off until then? Another AI provider is an
  alternative to look into; so is recognition without an outside provider (feasibility unknown).
- Is billing enabled for the current Gemini key?

Providers checked 2026-11-27 (a reading of their terms, not legal advice):

| Provider | Finding |
|---|---|
| [Google Cloud / Vertex AI](https://cloud.google.com/terms/service-terms) | Same ban as the Gemini API. |
| [AWS Bedrock](https://aws.amazon.com/service-terms/) | No age rule found in AWS's terms; terms of the individual models not yet checked. Some models keep inputs up to 30 days. |
| [Mistral](https://legal.mistral.ai/terms/commercial-terms-of-service) | Minors need parental consent; no personal data of children under 13 or the age of digital consent. |
| [OpenAI](https://openai.com/policies/services-agreement/) | Minors need parental consent, plus [safeguards](https://developers.openai.com/api/docs/guides/safety-checks/under-18-api-guidance), such as zero data retention before processing personal data of under-13s. |
| [Anthropic](https://support.claude.com/en/articles/9307344-responsible-use-of-anthropic-s-models-guidelines-for-organizations-serving-minors) | Allowed with safeguards, including age verification, filtering, monitoring and AI disclosure. |

Conditions *(proposed)* for any replacement: its terms allow the app; it recognises series and
card number at least as well as today; photos are not used to train models; the running cost stays
affordable for a free hobby project.

### Parental consent
- Is asking for a parent's consent at sign-up acceptable? It adds friction and probably means
  collecting data such as a parent's email address, against the non-goal of collecting no personal
  data.
- Do data protection rules require consent regardless of the recognition provider? Not checked.

### Friend contact
- Who may find a child and send a friend request? Today any registered user can, by searching for
  part of a username. Options: invite code or link only *(recommended)*, exact username, or search
  as today.
- The app does not ask for age, so it cannot tell who is a child. Should contact rules apply to
  every user *(recommended)*, or should the app start asking for age?

### "About" page wording
- The "About" page says no data is passed to third parties, but card photos are analysed by Google
  Gemini. Which changes: the statement or the way photos are analysed?
- Not a question, still to change: the page says advertising helps cover hosting costs. That
  sentence must go (see the non-goal of no monetisation); the
  [web-about-page](../../openspec/specs/web-about-page/spec.md) spec fixes this wording.

## Realised by
These cover parts of the goal only.

- [web-collection-sharing](../../openspec/specs/web-collection-sharing/spec.md): only friends see a
  collection; the owner can make it private.
- [web-friends](../../openspec/specs/web-friends/spec.md): friendship needs acceptance; also
  defines today's username search.
- [web-about-page](../../openspec/specs/web-about-page/spec.md): fixes the exact wording of the
  safety and privacy statements.
- [picture-service-photo-deletion](../../openspec/specs/picture-service-photo-deletion/spec.md):
  deleting photos.
