## Context

See proposal.md - Why. All five pages currently declare `@rendermode
InteractiveServer`, which defaults to prerendering enabled. `Program.cs`
wires `AddInteractiveServerComponents()` /
`.AddInteractiveServerRenderMode()` at the app level with no
prerender-disabling override, so each page's own `@rendermode` line is
where this gets decided per page.

Depends on `add-opentelemetry-observability` for measurement — this design
assumes that change has landed and a "before" baseline trace showing the
duplicate fetch pattern already exists (captured in that change's task
5.4).

## Goals / Non-Goals

**Goals:**
- Eliminate the duplicate data fetch on every affected page.
- Prove the fix with trace evidence (before/after), not just code
  inspection.

**Non-Goals:**
- Changing what data any page shows or how it's laid out.
- Addressing the gRPC-channel-per-call cost or any DynamoDB/S3-side
  latency — separate change.
- General Blazor Server perf work (virtualization, pagination) noted
  during exploration but not scoped here.

## Decisions

### Disable prerendering (`prerender: false`) rather than guarding OnInitializedAsync against double invocation
Two ways to stop the duplicate fetch were considered:

1. **Disable prerendering** — `@rendermode @(new InteractiveServerRenderMode(prerender: false))`
   (or the `@attribute` equivalent) on each page. Simplest: removes the
   prerender pass entirely, so `OnInitializedAsync` only ever runs once,
   with no conditional logic needed in any page.
2. **Keep prerendering, guard the fetch** — e.g. skip the data fetch during
   the prerender pass (checking `HttpContext is not null` /
   `RendererInfo.IsInteractive`) and let it run only once the circuit is
   interactive. Preserves prerendering's fast static first paint, at the
   cost of adding a guard condition to every page's `OnInitializedAsync`
   and showing a page shell with no data during the brief prerender
   window.

Chosen: **option 1, disable prerendering**. This app's pages are all
data-driven (card tiles/tables/galleries) — prerendering's usual benefit
(fast static first paint, good for SEO/perceived speed on public
content-heavy sites) doesn't apply here: showing an empty shell before data
arrives isn't meaningfully better than waiting slightly longer for the
single real render, and this is a personal-scale collection tracker with
no SEO concern. Disabling prerendering also removes a whole render pass
from the critical path rather than just making it fetch-free, which should
make time-to-interactive faster, not just backend-call-count lower.
Option 2 is worth revisiting only if a future need for meaningful static
first paint emerges.

### Apply uniformly across all five pages
All five pages share the same render mode declaration and the same
underlying cause, so the same fix applies identically to each — no
per-page special-casing expected. If a page's testing surfaces a reason it
needs prerendering (none currently known), handle that page as an
exception at that point rather than speculatively branching now.

## Risks / Trade-offs

- **[Risk]** Losing prerendering means the very first paint after
  navigation is blank until the SignalR circuit connects and the single
  render completes, instead of showing prerendered static markup
  immediately → **Mitigation**: for this Blazor Server app the circuit
  connects quickly on a warm server, and the change removes the *second*
  render's added latency entirely, which should net faster
  time-to-interactive overall; validate this with the before/after trace
  comparison (see Migration Plan) rather than assuming it.
- **[Risk]** Any page-specific behavior that implicitly relies on running
  twice (e.g. a side effect meant to happen once but written to be
  idempotent, or that quietly depends on the prerender pass having no
  live circuit) could behave differently → **Mitigation**: spot-check each
  page after the change; none of the five are known to have such logic
  today.

## Migration Plan

- Change each page's render mode declaration, one page at a time or all
  together (they're independent, low-risk, single-line changes).
- Before/after comparison: capture a trace for each page's load both
  before and after the fix (using the tracing added by
  `add-opentelemetry-observability`) and confirm the duplicate backend-call
  cluster is gone.
- Rollback: revert the render mode declaration on any page that regresses
  — no data or state to unwind, this is a rendering-behavior-only change.
