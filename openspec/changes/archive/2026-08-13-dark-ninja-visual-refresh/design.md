## Context

See proposal.md - Why. The design was prototyped end-to-end as a set of static HTML/CSS mockups ("Card Vault") in a separate design tool, covering the gallery, table, review, upload, and overview pages plus a reusable card-tile component. This design.md is the bridge from those mockups to `NinjagoScanner.Web`'s actual Blazor/`app.css` implementation. The design reference files are bundled alongside this change (see `design-reference/` in this change's folder) - they are HTML/CSS references to recreate visually in Razor + `app.css`, not code to copy verbatim.

## Goals / Non-Goals

**Goals:**
- Give the Web project one consistent dark visual identity across every existing page.
- Keep every page's data-fetching, routing, and interaction logic untouched - this is a styling/markup change, not a behavior change.
- Solve the Review page's "16 known series, one button each" space problem with a compact popover picker instead of shrinking touch targets or wrapping an ever-growing button row. Each button is still a logo icon but without caption but the outer shell is now a trigger + popover instead of an always-visible row.
- Make the placeholder nature of `Tags` explicit and easy to rip out once a real field exists.

**Non-Goals:**
- Modeling a real `Tags` field server-side (sidecar JSON, `CardListItem`, `CollectionCardDetails`, CatalogService). This change only adds a client-side-derived placeholder for visual layout purposes.
- Adding an `Element` field server-side. The design reference includes an element concept; this change drops it rather than fabricating server data for it (the `Tags` placeholder is used instead, matching current product direction - see proposal.md). No `Element` field or column exists in the app today (confirmed: the only "Element" hit in the Web project is an unrelated Blazor `ElementReference`), so nothing is removed from the spec - the `Tags` display is purely additive.
- Changing review-status semantics, grouping/sorting rules, or any gRPC contract.
- A native mobile app - the responsive bottom-tab-bar shell targets the existing Blazor Web app's mobile browser view only.

## Decisions

**Design tokens live in `app.css` as CSS custom properties, not inline styles or a CSS-in-C# scheme.** Mirrors the existing project convention (`app.css` already holds all page styling); keeps the tokens easy to reference from every `.razor` file's markup via class names.

Recommended token set (from the design reference; adjust hex values only if they clash with real photo thumbnails once dropped in):
- Surfaces: `--cv-bg: oklch(14% 0.015 290)`, `--cv-surface: oklch(19% 0.02 290)`, `--cv-surface-2: oklch(24% 0.02 290)`, `--cv-border: oklch(28% 0.03 290)`.
- Text: `--cv-text: oklch(96% 0.01 290)`, `--cv-text-muted: oklch(58% 0.02 290)`.
- Accents: `--cv-purple: oklch(65% 0.19 300)`, `--cv-green: oklch(75% 0.17 150)`.
- Type: Rajdhani (headings, numbers, labels, nav) + Noto Sans JP (body copy), both via Google Fonts `<link>` in `App.razor`'s `<head>`.

**Series-reassignment control becomes trigger + popover, not a redesigned button.** The existing `web-review-series-logos` capability's per-series logo-icon behavior is preserved unchanged *inside* the popover's grid cells (same icon/caption/fallback rules); only the outer shell (always-visible row → collapsed trigger that opens a 4-column grid) changes. Rationale: an inline row of up to 16 logo buttons either wraps awkwardly or shrinks below a usable tap target on mobile; a popover keeps every option one tap away without permanently consuming vertical space on every photo tile. This is why `web-card-review-flow`'s "one click" reassignment requirement and `web-review-series-logos`' three logo-rendering requirements both carry spec deltas in this change (see `specs/`), while every other touched capability is presentation-only and carries none.

**`Tags` is an explicit, isolated placeholder, not a silent rename of `Rarity`.** Compute it in a single small helper (e.g. `TagsForRarity(string? rarity)` colocated with the other display-formatting helpers already in the affected `.razor` files) rather than scattering the `Rarity → Tags` mapping inline at each call site, so the eventual server-side `Tags` field can replace the helper's call sites with a direct property read in one pass. The gallery tile's tags row and the table view's Tags column both call this one helper.

**Nav shell replaces `NavMenu.razor`'s markup, not its routing.** The existing `<NavLink>` routes/hrefs are kept; only the surrounding markup/CSS classes change to the new top-nav/bottom-tab-bar shell, so active-route highlighting keeps working via Blazor's built-in `NavLink` active-class behavior.

## Risks / Trade-offs

- **Placeholder `Tags` data may look final to users** even though it's derived, not modeled → mitigated by keeping the derivation in one named helper (easy to grep/replace) and calling it out explicitly in this change's proposal and in code comments at the helper.
- **Dropping the `Element` column/dot from the table view** removes information some users may have relied on, even though no real `Element` field exists today (it was mock-only in the design reference) → no real data is lost since `Element` was never a real field or spec'd requirement.
- **New Google Fonts dependency** (Rajdhani, Noto Sans JP) → both are open-source, no licensing risk; adds two font-family network requests on first load (acceptable for a hobby/collector app, not a performance-critical surface).
- **CSS custom properties in `app.css` vs. scoped `.razor.css` files** → project currently mixes both (`MainLayout.razor.css`, `NavMenu.razor.css` exist alongside `app.css`); tokens go in `app.css` specifically so every page can reference them without duplicating `:root` blocks per component.
- **The popover restructure is the one part of this change that is not purely cosmetic** → reassigning a photo's series now takes an extra tap (open popover, then pick) instead of being always one click away; mitigated by making the trigger itself show the photo's current series so its state stays visible without opening it.

## Migration Plan

1. Land the token layer + shared component styles in `app.css` first (additive, no visual change until pages opt in).
2. Restyle one page at a time (`Home` → `Collection`/table → `Review` → upload → overview), each independently verifiable and revertible.
3. Land the Review series-picker popover restructure last, since it's the only change that touches interactive markup structure rather than pure styling.
