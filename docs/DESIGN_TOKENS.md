# Design Tokens & Atoms Reference — Sourcing Ops Platform

Status: Reference — extracted 2026-07-27 from the approved prototype
Source: `Source/Sourcing Ops Platform.dc.html` (DOM structure + inline `style=` values) and the prototype's inline script (`SVC`/`ST` status maps, nav definition)
Implements: TECH_SPEC §5.1, ACTION_PLAN E0-01, E0-07 (closes TECH_SPEC OI-3)

This document is the **single source of truth** for every colour, spacing, radius, shadow, type and glyph value used anywhere in the Angular app. Every net-new screen (Login, Force-password-change, Invoicing, Admin — none of which exist in the approved prototype) must be designed from the atoms and values listed here, not from a freshly invented visual idiom. The values below are carried over 1:1 from the prototype's inline styles — nothing here is invented.

The real, buildable version of these tokens lives in `client/src/app/shared/styles/_tokens.scss` and `client/src/styles.scss` as CSS custom properties. **This file and that stylesheet must agree** — if you change one, change the other.

---

## 1. Colour palette

### Brand / structure

| Token | Value | Used for |
| --- | --- | --- |
| `--color-navy` | `#1a2332` | Sidebar background; primary text colour on white surfaces (`body{color:#1a2332}`) |
| `--color-accent` | `#2d5be3` | Primary buttons, links, active nav item background, focus border, brand dot after "Meridian" |
| `--color-accent-hover` | `#2449b5` | Link hover colour (`a:hover`) |
| `--color-page-bg` | `#f8f9fb` | App background behind cards; table header cell background; hover background for list rows/nav collapse control area |
| `--color-surface` | `#ffffff` | Card/dialog/table/input background |
| `--color-track` | `#f1f3f6` | Progress-bar/funnel-bar track background |

### Text / borders (neutral ramp)

| Token | Value | Used for |
| --- | --- | --- |
| `--color-text` | `#1a2332` | Primary body text (same as navy) |
| `--color-text-muted` | `#6b7280` | Secondary text, placeholders, help text, table row secondary line |
| `--color-text-strong-muted` | `#374151` | `NEW`/`PACKED` status label foreground (darker neutral than `#6b7280`) |
| `--color-border` | `#e5e7eb` | Default 1px borders on cards, inputs, table rules, dividers |
| `--color-border-strong` | `#e5e7eb` | (same value; kept as one token — the prototype never uses a second border weight) |

### Semantic accents

| Token | Value | Used for |
| --- | --- | --- |
| `--color-warning` | `#f57f17` | Freight-only accent, low-stock, "ON-HOLD"/"IN TRANSIT" status fg, warning banner text |
| `--color-warning-bg` | `#fff3e0` | Warning banner / chip background pair for `--color-warning` |
| `--color-warning-border` | `#ffcc80` | Warning banner border (freight-only info banner on intake form) |
| `--color-danger` | `#e53935` | Negative stock, LOST status, in-transit-overdue KPI sub-text |
| `--color-danger-bg` | `#ffebee` | Danger **chip** background |
| `--color-danger-bg-subtle` | `#fff8f8` | Negative-stock **row** tint (inventory table, E7-11). Deliberately distinct from `--color-danger-bg`: the prototype uses both, and `#ffebee` behind a full-width row reads as an error state rather than the quiet flag the approved screen intends |
| `--color-success` | `#2e7d32` | ACTIVE/WON/DELIVERED status fg, positive KPI sub-text |
| `--color-success-bg` | `#e8f5e9` | Success chip background |
| `--color-info` | `#1565c0` | CIF service-type fg, QUALIFIED/DISPATCHED status fg, user-avatar-initials fg |
| `--color-info-bg` | `#e3f2fd` | CIF chip background, user-avatar background |
| `--color-neutral-chip-bg` | `#e5e7eb` | NEW/DORMANT/INACTIVE/PACKED chip background |

### WhatsApp preview bubble (dispatch dialog only)

| Token | Value |
| --- | --- |
| `--color-wa-bubble` | `#dbeafe` |

---

## 2. Status → colour maps (ported verbatim from the prototype's `SVC` / `ST` objects)

These are implemented **once**, in `StatusStyleService`, never copy-pasted into a component. Source: lines ~1233–1250 of the prototype.

### `SVC` — service type

| Code | Label | bg | fg |
| --- | --- | --- | --- |
| `CIF` | `CIF` | `#e3f2fd` | `#1565c0` |
| `Freight-only` | `FREIGHT-ONLY` | `#fff3e0` | `#f57f17` |

### `ST` — generic status (customer lead status + vendor status + shipment status share one map)

| Code | bg | fg |
| --- | --- | --- |
| `NEW` | `#e5e7eb` | `#374151` |
| `QUALIFIED` | `#e3f2fd` | `#1565c0` |
| `ACTIVE` | `#e8f5e9` | `#2e7d32` |
| `WON` | `#e8f5e9` | `#2e7d32` |
| `LOST` | `#ffebee` | `#e53935` |
| `DORMANT` | `#e5e7eb` | `#6b7280` |
| `ON-HOLD` | `#fff3e0` | `#f57f17` |
| `INACTIVE` | `#e5e7eb` | `#6b7280` |
| `PACKED` | `#e5e7eb` | `#374151` |
| `DISPATCHED` | `#e3f2fd` | `#1565c0` |
| `IN TRANSIT` | `#fff3e0` | `#f57f17` |
| `DELIVERED` | `#e8f5e9` | `#2e7d32` |

Fallback: unknown status codes fall back to `ST.NEW`'s colours (prototype: `ST[status] || ST.NEW`).

**Deviation / addition (flagged):** the prototype hard-codes these as JS objects and the FR-ADM-02 requirement makes lead/shipment/invoice status *labels* configurable master data server-side. The colour is a **presentation-layer** concern the FSD never asks to be admin-editable, so `StatusStyleService` keys off the status **code** (stable) not the configurable label, and ships the fixed palette above. Invoice statuses (`DRAFT`/`ISSUED`/`PAID`/`CANCELLED`) have no prototype precedent — assumption recorded in §7 below.

---

## 3. Spacing scale

The prototype does not declare a formal spacing scale variable — it uses literal pixel values directly in `style=`. Reverse-engineered scale (every value that actually recurs):

| Token | Value | Typical use |
| --- | --- | --- |
| `--space-1` | `2px` | Chip vertical padding |
| `--space-2` | `4px` | Tight gaps (icon-to-text in small chips) |
| `--space-3` | `6px` | Gap between icon and label in nav; small margins |
| `--space-4` | `8px` | Standard small gap (button groups, form chip rows) |
| `--space-5` | `9px`/`10px` | Input/button vertical padding, KPI/table row gaps |
| `--space-6` | `12px` | Standard horizontal padding (button, table cell "Ref"), grid gaps |
| `--space-7` | `14px` | Button horizontal padding |
| `--space-8` | `16px` | Card internal padding (dialog sections), most grid `gap` |
| `--space-9` | `20px` | Card padding (stat tile / chart card), page section margins |
| `--space-10` | `24px` | Page content padding, dialog padding |

Layout constants (not a scale step, but fixed and reused):
- Sidebar width expanded: `220px`; collapsed: `64px` (`sbW`, prototype `Component.renderVals`)
- Topbar height: `56px`
- Sidebar top bar height: `56px`

---

## 4. Typography

| Token | Value |
| --- | --- |
| `--font-family-base` | `-apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif` |
| `--font-family-mono` | `ui-monospace, SFMono-Regular, Menlo, monospace` (used once, for a placeholder image caption) |
| `--font-size-base` | `14px` (body default) |
| `--line-height-base` | `1.4` |

Font-size steps actually used in the prototype (no separate named scale exists — carried over as literal values per element): `11px` (micro labels, badges, ⌘K hint), `12px` (secondary meta text, table header labels), `13px` (secondary body text, form labels, buttons), `14px` (body text, inputs, primary buttons), `15px` (command palette input), `16px` (topbar title, dialog close glyph), `18px` (app wordmark, dialog titles), `20px` (page `<h1>`), `22px` (upload icon), `28px` (KPI stat value).

Font weights used: `400` (default), `500` (buttons, labels, table primary cell), `600` (nav group label, section headings, chip text), `700` (page `<h1>`, KPI value, app wordmark).

---

## 5. Border radius

| Token | Value | Used for |
| --- | --- | --- |
| `--radius-sm` | `4px` | ⌘K keycap hint, progress-bar track/fill |
| `--radius-md` | `8px` | **Default** — cards, buttons, inputs, selects, nav item, dialogs |
| `--radius-pill` | `12px` | Status/service-type chips |
| `--radius-circle` | `50%` / half-of-size | Avatar circle (`14px` on 28px box), spinner, step-indicator dot (`10px` on 20px box) |

---

## 6. Shadows

| Token | Value | Used for |
| --- | --- | --- |
| `--shadow-card` | `0 1px 4px rgba(0,0,0,.08)` | Cards, stat tiles, table container, filter bar |
| `--shadow-dialog` | `0 8px 32px rgba(0,0,0,.2)` | Modal dialogs (WhatsApp dispatch, upload, vendor edit, command palette) |
| `--overlay-scrim` | `rgba(0,0,0,.4)` | Full-screen dialog backdrop (`position:fixed;inset:0`) |

---

## 7. Emoji nav glyphs

Ported verbatim from the prototype's `navDef` (`Component.renderVals`):

| Route | Icon | Label | Nav group |
| --- | --- | --- | --- |
| `dash` | 📊 | Dashboard | MAIN |
| `customers` | 👥 | Customers | CRM |
| `intake` | 📝 | New Lead Intake | CRM |
| `vendors` | 🏭 | Vendors | SOURCING |
| `catalogs` | 📚 | Catalogs | SOURCING |
| `inventory` | 📦 | Inventory | OPERATIONS |
| `shipments` | 🚚 | Shipments | OPERATIONS |
| `mobile` | 📱 | Mobile Views | REFERENCE (design reference only — not ported as an app route) |

Other glyphs used inline (not nav icons): 🔍/`⌕` search icon, ⌘K hint, ✕ close, ↻ refresh, 📄 document icon, ✓/step-marks in the WhatsApp dispatch dialog.

**Net-new nav entries (not in the prototype, added for this build):** Invoicing and Admin need glyphs of their own since TECH_SPEC §5.3 adds both `invoicing/` and `admin/` feature folders with no prototype precedent. Chosen to stay inside the same "single emoji, business-object metaphor" pattern the prototype uses: 🧾 Invoicing, 🛠️ Admin. Flagged as an assumption (§10) — happy to swap if the business prefers different glyphs at sign-off.

---

## 8. Reusable atoms

Each atom below is implemented once in `client/src/app/shared/` and reused by every screen — never copy-pasted per component (DoD requirement, DR-6).

### Button
- **Primary**: `background:#2d5be3; color:#fff; border:1px solid #2d5be3; border-radius:8px; padding:9px 14px; font-size:14px; font-weight:500; cursor:pointer`
- **Secondary**: `background:#fff; color:#1a2332; border:1px solid #e5e7eb; border-radius:8px; padding:9px 14px; font-size:14px; font-weight:500; cursor:pointer`; hover `background:#f8f9fb`
- **Small/table-row action**: same as secondary but `padding:6px 10px; font-size:13px`

### Card
`background:#fff; border:1px solid #e5e7eb; border-radius:8px; padding:20px; box-shadow:0 1px 4px rgba(0,0,0,.08)` — the universal container for stat tiles, chart panels, filter bars, form sections.

### Stat tile
Card atom, with: label (`font-size:13px;color:#6b7280;text-transform:uppercase;letter-spacing:.05em`), value (`font-size:28px;font-weight:700;margin-top:8px`), sub-text (`font-size:12px;margin-top:4px`, colour driven by semantic state — success/warning/danger/muted).

### `.table`-style list
Wrapper: card atom minus padding, `overflow:hidden`. Header cell: `font-size:12px;font-weight:600;text-transform:uppercase;letter-spacing:.05em;color:#6b7280;background:#f8f9fb;padding:10px 16px;text-align:left;border-bottom:1px solid #e5e7eb` (`text-align:right` for a trailing actions column). Body cell: `font-size:14px;padding:12px 16px;border-bottom:1px solid #e5e7eb`; row hover `background:#f9fafb`.

### Form field
Label: `display:block;font-size:13px;font-weight:500;margin-bottom:6px`. Input/select/textarea: `width:100%;padding:9px 12px;border:1px solid #e5e7eb;border-radius:8px;font-size:14px`; focus `border-color:#2d5be3;outline:none`.

### Chip / badge
`display:inline-block;padding:2px 8px;border-radius:12px;font-size:11px;font-weight:600` with `background`/`color` from the `SVC`/`ST` maps.

### Dialog
Backdrop: `position:fixed;inset:0;background:rgba(0,0,0,.4);display:flex;align-items:flex-start;justify-content:center;padding:64px 24px;overflow-y:auto;z-index:50` (z-index `60` for the command palette, which sits above other dialogs). Panel: `background:#fff;border-radius:8px;padding:24px;width:100%;max-width:720px;box-shadow:0 8px 32px rgba(0,0,0,.2)` (max-width varies: `560px` upload dialog, `720px` WhatsApp/vendor-edit dialogs, `560px` command palette with no padding on the panel itself — the search input and results scroll inside instead).

### Pill tab (added by the M5 screen pass, E7-12)
`border:1px solid #e5e7eb; background:#fff; color:#1a2332; border-radius:8px; padding:8px 14px; font-size:13px; font-weight:500` — active state `border-color:#2d5be3; background:#f5f8ff; color:#2d5be3`. The per-tab count stays `#6b7280` even when active.

**Deliberately a second tab atom, not a variant of the underline `.tab` above.** The prototype uses two unrelated tab treatments — underline tabs on the admin master-data screen, bordered count-carrying pills on the shipments status filter. They share no declarations beyond font size, so a modifier would have had to override almost every property.

### Stock level bar (added by the M5 screen pass, E7-11)
Track `width:40px; height:6px; border-radius:3px; background:#e5e7eb; overflow:hidden`. Fill `height:6px; border-radius:3px` with width and colour bound per row. Width formula is ported verbatim from the prototype's `invRows()`: `clamp(round((qty / (reorder * 2.5)) * 100), 0, 100)`, full width when negative — plus a zero-threshold guard the prototype never needed against its mock data.

### Status stepper (added by the M5 screen pass, E7-13)
Equal-width flex columns; each step draws its own trailing connector line so the markup stays a flat loop with no separator elements. Dot `22px` circle, `border-radius:11px`, `font-size:11px; font-weight:600`; connector `height:2px`; label `13px/600`; "when" line `12px` muted. Dot/line/label colours bind per step — reached steps use `--color-accent`, unreached use `--color-border`/`--color-text-muted`.

### Sidebar nav item
`display:flex;align-items:center;gap:10px;padding:9px 12px;border-radius:8px;cursor:pointer` — active state `background:#2d5be3;color:#fff`, inactive `background:transparent;color:rgba(255,255,255,.75)`, hover (inactive only) `background:#243044`.

---

## 9. App shell layout (E1-15)

- Outer flex row, `height:100vh;overflow:hidden;background:#f8f9fb`.
- Sidebar: `width:220px` expanded / `64px` collapsed, `background:#1a2332;color:#fff`, flex column. Top brand bar `56px` tall. Nav scroll area `flex:1;overflow-y:auto;padding:12px 8px`, grouped by label (`MAIN`, `CRM`, `SOURCING`, `OPERATIONS`, `REFERENCE`) with `14px` bottom margin per group. Collapse control fixed at bottom, `12px 20px` padding, top border.
- When collapsed, group labels and nav-item text are hidden (`display:none`) — only the emoji glyph remains, and the row keeps a `title` attribute for the icon-only state (Angular: bind `[title]`, since there is no tooltip component).
- Main column: flex column, topbar `56px` fixed height (title, search-trigger styled as a fake input opening the command palette, refresh action, divider, avatar + name), content area `flex:1;overflow-y:auto;padding:24px`.

---

## 10. Assumptions / open questions recorded (per DoD — not silently absorbed)

1. **Invoice status colours** (`DRAFT`/`ISSUED`/`PAID`/`CANCELLED`) have no prototype precedent (invoicing didn't exist when the prototype was built). Assumption: `DRAFT` reuses `ST.NEW` colours, `ISSUED` reuses `ST.DISPATCHED`, `PAID` reuses `ST.DELIVERED`/`ACTIVE` (success green), `CANCELLED` reuses `ST.LOST` (danger red). Recorded in `StatusStyleService` with a comment pointing back here; revisit at E0-05/E0-06 sign-off.
2. **Emoji glyphs for the two net-new nav groups** (Invoicing 🧾, Admin 🛠️) are not in the prototype and are a judgment call pending business sign-off (OI-4/E0-06).
3. **Login / Force-password-change** have no prototype screen. They are built from the atoms above (card, form field, primary button) with the sidebar/topbar shell omitted entirely (there is no authenticated user yet to show a topbar for) — see the Login/Force-change screens themselves for the concrete layout. Flagged for E0-06 sign-off per ACTION_PLAN.
4. The prototype's `.table` atom has no dedicated CSS class — it's a literal `<table>` with inline styles. The Angular port introduces a `shared` SCSS partial (`_table.scss`) that reproduces the exact same declarations under a reusable class, per TECH_SPEC §5.1's translation rule ("same declarations, moved into the component's scoped CSS file").
5. `--color-border-strong` is listed as a distinct token pointing at the same value as `--color-border` because the prototype never actually uses a second, heavier border weight — kept as a separate token only so a future design pass has a named place to put one without a find/replace across the codebase.

---

## 11. OI-3 — `_ds/industry-…` bundle (closes E0-07)

Confirmed by inspection of `Source/_ds/industry-325ec96d-4bae-4174-ba24-d2d3665480ba/` (`readme.md`, `styles.css`, `theme.json`, `_ds_bundle.js`, `_ds_manifest.json`):

- It is a **generic, unbranded prototyping-tool design-system bundle** named "Industry" — a steel-blue wireframe/blueprint theme: light ground `#f2f2f3`, single accent `#5980a6`, square-cornered "blueprint" cards and buttons with corner registration marks, Barlow Condensed headings over Barlow body text, Lucide icons at stroke-width 1.5.
- This is **categorically different** from the approved prototype's visual language: navy `#1a2332` + blue accent `#2d5be3`, rounded `8px` corners everywhere, system font stack (`-apple-system, BlinkMacSystemFont, 'Segoe UI'`), emoji icons, soft drop shadows instead of hairline "blueprint" framing.
- `Sourcing Ops Platform.dc.html` does **not** load, `@import`, or reference `_ds/industry-…/styles.css` or `_ds_bundle.js` anywhere — confirmed by inspecting the prototype's `<head>`/`<helmet>` and its only external reference (`support.js`, the dc-runtime itself).
- Conclusion: this is unused scaffolding generated by the prototyping tool for a *different* candidate theme that was never applied to the approved screens. **It is excluded from the Angular port.** No file under `_ds/` was read into or copied by any deliverable in this pass.

This closes TECH_SPEC OI-3 and ACTION_PLAN E0-07 as: confirmed unused, safe to ignore, excluded from the port.
