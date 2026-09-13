# Net-New Screen Designs — E0-02 … E0-05

**Status:** Authorised by the business owner on 2026-07-27 to proceed without a separate formal design pass (see `ACTION_PLAN.md` §12 and DR-14).

**Scope.** Four screens have **no precedent in the approved prototype** (`Source/Sourcing Ops Platform.dc.html`). This was re-confirmed by direct grep before this document was written: the prototype contains **zero** occurrences of `login`, `admin` or `password`, and covers only Dashboard, Customers, Vendors, Catalogs, Inventory and Shipments. These designs are therefore genuinely net-new, not ports.

**The single binding constraint.** Every screen below is composed **only** from the E0-01 token/atom reference — `docs/DESIGN_TOKENS.md` and its implementation in `client/src/app/shared/styles/_tokens.scss` / `_atoms.scss`. No new colour, no new spacing step, no new font, no new radius, no new shadow. Where a screen needs a structure the atom set does not yet have (tabs, a one-time secret display), the structure is added **to `_atoms.scss` as a shared atom built from existing tokens** — never as a per-screen invention. This is the entire purpose of E0-01: net-new screens inherit the approved visual language instead of starting a second one.

**Layout inheritance.** E0-04 and E0-05 live inside the ported app shell (E1-15) — navy sidebar, topbar, `--color-page-bg` content area. They follow the layout grammar the six ported screens already established:

```
.page-header   →  h1.page-title  +  right-aligned action buttons
.card.filter-bar  →  .field controls, wrapped, aligned to flex-end
.summary row   →  13px --color-text-muted result count
.table-wrap    →  the list itself
.pager         →  paging controls
```

E0-02 and E0-03 are **outside** the shell — an unauthenticated user has no sidebar to render.

---

## E0-02 — Login

**Route:** `/login` (the unauthenticated landing route; `**` redirects here).
**Layout:** full-viewport centred card on `--color-page-bg`. No sidebar, no topbar.

| Region | Composition |
| --- | --- |
| Card | `.card`, `max-width: 380px`, centred both axes |
| Brand | The M2C logo (`logo-dark.png`, navy ink for the light card), 30px tall — the same lockup the sidebar renders in its white-ink variant, so the first screen a user sees already matches the shell they land in |
| Eyebrow | `SOURCING OPS`, 11px, `--color-text-muted`, `.06em` tracking |
| Title / subtitle | `Sign in` (20px/700) over `Internal tool — staff accounts only.` (13px muted) — states FSD A1's internal-only posture on the one screen where an outsider could arrive |
| Error state | `.banner-warning` with `role="alert"`, above the form |
| Fields | `.field` × 2 — Email (`type=email`, `autocomplete=username`), Password (`type=password`, `autocomplete=current-password`), each with `.field-error` under it |
| Submit | `.btn.btn-primary.btn-block`, label swaps to `Signing in…` while disabled |

**Design decisions.**

- **One generic error message.** A failed sign-in shows the *same* text whether the email is unknown or the password is wrong. This mirrors what E1-07's backend already does (verified: both return 401 with no account-existence hint) — a screen that said "no such user" would leak on the client what the API deliberately does not.
- **No "forgot password" link.** There is no self-service reset in Phase 1 — password recovery is an admin action (E11-02). A link to a flow that does not exist is worse than its absence; the subtitle carries the expectation instead.
- **No "remember me".** Session lifetime is the JWT/refresh pair (TECH_SPEC §4.2); a checkbox implying otherwise would be decorative.

**On `MustChangePassword`:** the component routes to `/force-change-password` rather than the shell (E1-13).

## E0-03 — Force password change

**Route:** `/force-change-password`, behind `authGuard` only.
**Layout:** identical card geometry to E0-02 — deliberately, so the transition from sign-in reads as one continuous flow rather than a different application.

| Region | Composition |
| --- | --- |
| Title / subtitle | `Set a new password` over an explanation that the temporary password must be replaced before the app is reachable |
| Notice | `.banner-warning` stating plainly that no other screen is available until this is done — the user cannot navigate away, so the UI must say why rather than let them hunt for a menu that is not there |
| Fields | Current (temporary) password, New password, Confirm new password — all `autocomplete="new-password"` except the first |
| Rules hint | `.field-hint` listing the actual server-side policy, shown **before** submission, not only as an error afterwards |
| Submit | `.btn.btn-primary.btn-block` |

**Design decisions.**

- **No shell, no nav, no logout-to-dashboard escape.** `mustChangePasswordGuard` already blocks the shell server-authoritatively; the screen must not render an affordance the guard will refuse.
- **Mismatch is a field-level error on Confirm**, not a banner — it is a correctable typo in one specific field, and putting it on that field is where a user looks.
- **Success lands on the dashboard**, not back on login: the user has just proven their identity, so re-authenticating would be theatre.

---

## E0-04 — Admin area

Two screens under an `ADMIN` sidebar group, both `Admin.*`-permission-gated, so the group is invisible to an Associate (E1-12 nav filtering + `permissionGuard`).

### E0-04a — User management (`/admin/users`, `Admin.ManageUsers`)

Backed by the **existing** `AdminUsersController` (M2) — no new endpoint required. Verified surface: `GET users` (`search`/`page`/`pageSize`/`includeInactive`), `POST users`, `POST users/{id}/reset-password`, `DELETE users/{id}`, `POST users/{id}/restore`, `PUT users/{id}/roles`, `GET roles`.

| Region | Composition |
| --- | --- |
| Header | `.page-title` "Users" + `.btn.btn-primary` "+ New User" |
| Filter bar | `.card.filter-bar` — search input + a "Show inactive" checkbox bound to `includeInactive` |
| Summary | `{n} users` |
| List | `.table-wrap` — Name (+ email as `.row-sub`), Roles (`.chip` per role), Status, Created, Actions |
| Status cell | `.chip` — Active on `--color-success-bg`/`--color-success`; Inactive on `--color-neutral-chip-bg`; a separate "Must change password" chip on `--color-warning-bg` |
| Actions | `.btn.btn-sm.btn-secondary` — Roles, Reset password, Deactivate / Restore |
| Empty / loading / error | `.state-panel` + `.spinner` (DoD requirement) |

**The one-time temp password interaction — the load-bearing part of this screen.**

`POST /admin/users` and `POST /reset-password` return `temporaryPassword` **once**, in that response body only; no read endpoint ever echoes it (verified live at M2). The UI must be built around that fact rather than around the convenience of being able to look it up again:

- The password appears in a **modal that cannot be dismissed by backdrop click or Escape** — only by an explicit "I have copied it" button. An accidental click outside is otherwise unrecoverable and forces a second reset.
- It renders in `--font-family-mono` at a size that survives being read aloud over a phone, with a **Copy** button.
- The modal states in words that this is the only time it will be shown.
- The dialog uses the existing `.dialog-backdrop` / `.dialog-panel` atoms.

**Design decisions.**

- **Deactivate is confirmed, not immediate.** It revokes sessions and (since `dd0f8cf`) the already-issued access token. A one-click destructive action in a table row is the classic misclick.
- **Restore is surfaced in the UI.** `POST /restore` exists (D-10) but was API-only; without a button, an accidental deactivation is unrecoverable for anyone who is not curling the API.
- **The last-active-Super-Admin guard (D-11) is surfaced as a message, not a crash.** The API returns 400 for that case; the screen renders the `ProblemDetails` detail verbatim, because the reason ("you would lock everyone out") is the useful part.
- **Roles are a modal, not an inline dropdown.** Changing a role revokes refresh tokens (DR-10, E11-05), so the modal states that the user must sign in again — DR-10's stated mitigation is exactly this sentence appearing in the admin UI.

### E0-04b — Master data configuration (`/admin/master-data`, `Admin.ManageMasterData`)

Backed by the **existing** `MasterDataController` (M2). Verified surface: `GET /master-data?includeRetired=`, `POST /{collection}`, `PUT /{collection}/{id}`, `POST /{collection}/{id}/retire`, `POST /{collection}/{id}/restore`, `PUT /{collection}/reorder`, `DELETE /{collection}/{id}`. Collections: `categories`, `service-types`, `lead-statuses`, `shipment-statuses`, `invoice-statuses`, `vendor-statuses`.

| Region | Composition |
| --- | --- |
| Header | `.page-title` "Master Data" + `.btn.btn-primary` "+ Add" (acts on the selected collection) |
| Collection selector | `.tabs` / `.tab` atom — six tabs, one per collection |
| Toggle | "Show retired" checkbox → `includeRetired` |
| List | `.table-wrap` — Order, Name-or-Code/Label, Status, Actions |
| Actions | Move up / Move down (reorder), Rename, Retire / Restore, Delete |

**Design decisions.**

- **Reorder is up/down buttons, not drag-and-drop.** `PUT /{collection}/reorder` takes an explicit `[{id, sortOrder}]` array, so buttons express the same intent with no dependency; drag-and-drop would mean a library, which collides with constraint C1 and TECH_SPEC §11's no-UI-kit rule. Buttons are also the only version that works on the phone widths E12-01 requires.
- **The `categories` asymmetry is rendered honestly.** Categories carry `name`; every other collection carries `code` + `label` (a real API asymmetry, confirmed in §10.3). The form shows a single "Name" field for categories and separate "Code"/"Label" fields elsewhere, rather than pretending to a uniformity that does not exist.
- **`Code` is shown but not editable after creation.** D-12: `StatusStyleService` keys the ported colour maps by `Code`, so a mutable code would silently break status colours app-wide. The field renders `:disabled` with a hint saying the label is what to rename — an explanation, not a mystery.
- **Delete is offered but expected to fail, and that is the design.** E3-08 returns 409 with "…Retire it instead of deleting." for referenced rows; the screen surfaces that detail verbatim and points at the Retire action. Seeded defaults (N-8) are retire-only, so their Delete is disabled up front with a hint rather than offered and then refused.
- **Retired rows render visibly dimmed with an "Inactive" chip**, never hidden from the admin who ticked "Show retired" — FSD §3.3's promise is that a retired value still resolves on historical records.

---

## E0-05 — Invoicing (design only this pass)

> **Scope statement, deliberate and load-bearing.** There is **no invoicing backend** — no `InvoicesController`, no invoice entity, no migration; epic E8 has not started, and E8-08 is separately blocked on FSD Q9c (numbering format, GSTIN, registered address, bank details). These two screens are therefore built against **realistic mocked data held in the component** so there is a concrete design to sign off. **E8-09/E8-10 — the live, data-wired implementation — remain open and blocked on the full E8 backend epic plus Q9c.** Wiring a screen to a nonexistent API would produce the appearance of progress and none of the substance.

Every mocked screen carries a visible in-app notice saying it is a design preview on sample data, so it cannot be mistaken for a working module during a demo.

### E0-05a — Invoice list (`/invoices`)

| Region | Composition |
| --- | --- |
| Header | `.page-title` "Invoices" + `.btn.btn-primary` "+ Generate Invoice" |
| Notice | `.banner-warning` — design preview, sample data |
| Filter bar | `.card.filter-bar` — search, Customer, Status, Service Type, date-from, date-to (FR-BIL-04's named filters, exactly) |
| Summary | `{n} invoices` + total issued / total outstanding as `.stat-tile`s |
| List | `.table-wrap` — Invoice #, Customer, Date, Service type, Amount (right-aligned), Status, Actions |
| Status | `.chip` via `StatusStyleService` — invoice statuses were flagged net-new at E1-16 and are keyed by the same `Code` values seeded at E3-07 (`DRAFT`/`ISSUED`/`PAID`/`CANCELLED`) |

**Design decisions.**

- **Amounts are right-aligned and tabular.** The `.table-wrap th.align-right` modifier already exists; money that does not align by decimal is unscannable in a column.
- **Status filter reads from the master-data service, not a hard-coded list** (DR-6) — invoice statuses are configurable rows (E3-07), so even the mocked screen resolves them from `MasterDataService`. This is what makes the mock a genuine design rehearsal rather than a picture.

### E0-05b — Invoice generate / detail (`/invoices/new`, `/invoices/:id`)

| Region | Composition |
| --- | --- |
| Header | Invoice number + status `.chip` + actions: Download PDF, Mark Paid, Cancel |
| Notice | `.banner-warning` — design preview, sample data |
| Billing block | Two `.card`s side by side — "From" (company details) and "Bill to" (customer) |
| Lines | `.table-wrap` — description, amount; totals block beneath with subtotal, tax, total |
| Lifecycle | A horizontal status trail Draft → Issued → Paid, with Cancelled as a terminal off-path state |
| Timeline note | States that invoice events land on the customer activity timeline (E8-04, reusing E4-07's feed) |

**Design decisions.**

- **The "From" block renders an explicit "Company billing details not configured" empty state.** `company_settings` exists and is deliberately **empty** — single-row-ness is enforced by a CHECK constraint rather than a seeded row of nulls, precisely so "not configured" stays representable. FSD Q9c is unanswered, so the correct design shows the gap rather than inventing a plausible GSTIN. This is the screen that will make Q9c concrete for the owner.
- **The invoice number is a placeholder with a visible note** that its format is Q9-blocked (E8-08). Picking a format here would quietly become the decision.
- **Mark Paid is a distinct gated action** (`Invoicing.MarkPaid`, FR-BIL-06), not a status dropdown entry — it captures a paid date and an optional reference, and FSD A9 forbids anything resembling reconciliation.
- **Send-via-WhatsApp is drawn but inert**, labelled as depending on E9 (FR-BIL-05) — it reuses the dispatch dialog, which does not exist yet.

---

## Atoms added to `_atoms.scss` by this pass

Added as **shared** atoms from existing tokens only, so no screen invents them locally:

| Atom | Why it was needed | Built from |
| --- | --- | --- |
| `.page-header` / `.page-title` | Promoted from the per-screen copies the six ported screens each carried | existing spacing/type tokens |
| `.tabs` / `.tab` | Master-data collection selector; also the shipments status tabs E7-12 will need | `--color-border`, `--color-accent`, `--space-*` |
| `.secret-value` | One-time temp password display | `--font-family-mono`, `--color-page-bg`, `--radius-md` |
| `.toolbar` | Inline action/row groupings in list screens | `--space-*` |

No new colour, spacing step, font, radius or shadow was introduced. E0-01 remains the only source of visual values.
