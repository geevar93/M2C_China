# Human Tasks — things only the user can unblock

Single register for everything that needs a **human**: business inputs, credentials, procurement, licence/commercial decisions, manual testing and sign-off.

**Rules of this file.** Every item has an ID (`H-n`), what is needed, why it is human-blocked, what it blocks, and status. Agents do not close items here — only the user does. Items are never silently dropped; if one stops mattering it is marked **Withdrawn** with a reason. IDs are stable and never reused.

**Status vocabulary:** `Open` · `Asked` (sent to the user/owner, awaiting reply) · `Answered` · `Withdrawn`.

Cross-references point at `docs/ACTION_PLAN.md` (§ sections, `E-` stories, `N-` open items, `D-` deviations, `DR-` risks) and the FSD's open questions (`Q-`).

---

## Summary

| Priority | IDs | Why now |
| --- | --- | --- |
| **Blocking a milestone today** | H-1, H-2 | M6 cannot be honestly called complete without H-1. H-2 blocks estimating M8 at all. |
| **Blocks launch, long lead time** | H-3 | Not needed to finish the codebase (G3), but it is the only item whose lead time is outside our control. |
| **Commercial/legal, cheap now** | H-4, H-5 | Both get expensive to discover late. |
| **Business decisions shaping build** | H-6, H-7, H-8 | Each changes what gets built; none blocks current work. |
| **Sign-off and manual verification** | H-9, H-10, H-11, H-12 | Deferred by standing instruction (no browser passes); listed so the debt stays visible. |
| **Go-live** | H-13 | End of project. |

---

## H-1 — FSD Q9c: company billing values (and the numbering-format decision)

**Status:** Asked (taken out of band by the coordinator)
**Blocks:** M6's §5 exit criterion · story E8-08 · any real invoice
**Why human-blocked:** These are the business's own legal/banking identifiers. Inventing plausible values would silently become the business's decision — see §17.0.

Nine values, each mapping to one field on `PUT /admin/company-settings`:

| Field | Needed |
| --- | --- |
| `legalEntityName` | **Required to issue.** Registered legal name (not the trading name, if they differ). |
| `registeredAddress` | **Required to issue.** Full registered address as it should appear on a tax invoice. |
| `gstin` | 15-character GSTIN. |
| `bankAccountName` | Account holder name per bank records. |
| `bankAccountNumber` | Account number. |
| `bankIfsc` | IFSC code. |
| `bankBranch` | Branch name. |
| `invoiceNumberPrefix` | Prefix for `{PREFIX}-{YYMM}-{NNN}`. |
| `declarationText` | Standard declaration/footer line, if their accountant requires one. |

**Plus one decision, which is the part with teeth:** numbering currently resets to `001` **each month**. Some accountants require a continuous **annual or financial-year** sequence instead. **Changing this after invoices exist is disruptive** — settle before first production use.

**Consequence while open:** not cosmetic. `DRAFT → ISSUED` returns **400**; issuing is also what renders the PDF, and PAID is only reachable from ISSUED, so the whole downstream flow is blocked. See §17.7.

---

## H-2 — Legacy data sample file (FSD Q6 + Q8)

**Status:** Open — never asked
**Blocks:** E12-07 (legacy migration) · E12-02 (performance targets) · **estimating M8 at all**
**Why human-blocked:** Only the business has the spreadsheets.

Q8 is answered — there **is** data to bring over, so E12-07 is explicitly not dropped — but no sample ever arrived. Needed: one representative export, plus a rough sense of total volume.

**Ask for these together.** The legacy volume *is* the initial data volume, so the same file answers Q6, which is still unasked. Until it exists, E12-07 cannot be scoped and E12-02 has no volume assumptions to test against.

---

## H-3 — Production VPS: provisioning, domain and credentials (E2-05)

**Status:** Open
**Blocks:** E2-05, E2-06, E2-08, E2-10 · N-3 (automatic HTTPS untested) · E12-04 (backup restore drill) · H-13
**Why human-blocked:** Requires purchasing/access to a Hostinger box and a real DNS name.

Needed: a 4 vCPU / 8 GB VPS with SSH access, a domain pointed at it (Caddy provisions TLS via Let's Encrypt automatically), and a container registry credential for the CI push.

**Not scheduled** — by standing instruction the codebase is completed before any infrastructure work. Recorded here because it is the only outstanding item whose **lead time is not under our control**, and because of the risk below.

> **Standing risk, stated once and not re-litigated:** the Definition of Done says "verified against the deployed prod-like stack". No VPS has ever existed, so that has meant local `docker compose` since M1 — eight milestones. E2-06/07/08 are written but never run against real infrastructure. This is deferred deliberately, not forgotten.

---

## H-4 — QuestPDF Community licence: revenue check (N-26)

**Status:** Open
**Blocks:** nothing today; a licensing exposure at launch
**Why human-blocked:** Depends on the business's annual revenue, which we do not know.

QuestPDF Community is free below a stated annual-revenue ceiling; above it a paid licence is required. DR-3 was closed on **technical** merits only (pure-managed, no native binaries, fits the C1 footprint budget) — the commercial half was never checked. One question to the owner; expensive to discover after go-live.

---

## H-5 — Angular / Node upgrade decision (N-1)

**Status:** Open
**Blocks:** clearing open `npm audit` advisories on `@angular/core` / `@angular/compiler`
**Why human-blocked:** A platform decision with upgrade cost, not a task.

The Angular 19.2.x pin and staying on patched Angular are **mutually exclusive** until Node moves past v20.12.2 (Angular 20+ needs Node ≥20.19). Decide: upgrade Node and move to Angular 20, or accept the advisories with a documented rationale.

---

## H-6 — Stock-take correction: does the business do physical counts? (N-19)

**Status:** Open
**Blocks:** an unbuilt capability; visible operational gap today
**Why human-blocked:** A question about how the business actually operates.

Stock moves only via inbound entries and shipment lines (D-42), so there is **no path at all** to correct on-hand quantity when a physical count disagrees. The inventory screen now renders a figure a user can see is wrong and cannot fix. Deliberately not built unasked. If they do count stock, this needs a story.

---

## H-7 — Do invoices need the customer's GSTIN? (N-29)

**Status:** Open
**Blocks:** possible customer-schema + PDF change
**Why human-blocked:** Tax/compliance judgement for their business.

The generated invoice carries the **seller's** GSTIN but customers have **no GSTIN field at all**. For B2B sales to Indian buyers the recipient's GSTIN is normally required on a tax invoice, and without it the buyer generally cannot claim input credit. Not built unasked — FSD §6.8 scopes invoicing as deliberately "lightweight". **Ask alongside H-1**; same conversation, and much cheaper before first production use.

---

## H-8 — Bundle budget: confirm 320 kB, or give a number (N-10)

**Status:** Open — asked twice, unanswered twice
**Blocks:** a build warning on every UI story
**Why human-blocked:** Setting a budget is a call about acceptable payload, not an engineering fact.

The 300 kB initial budget was set at M1 before the app had features and has been exceeded since M4. Production build: **304.87 kB raw / 87.50 kB transfer**, almost entirely framework, with every feature screen correctly lazy-chunked. **Nothing can be cut without removing Angular.** Recommendation: raise to **320 kB** with the rationale recorded. Nothing is broken meanwhile — it is a warning; the error threshold is 500 kB.

---

## H-9 — Manual/browser verification of M4's dispatch dialog (N-16)

**Status:** Open — recorded, **not scheduled**
**Blocks:** confidence, not code
**Why human-blocked:** Standing instruction: no browser passes unless explicitly requested.

Vendors, vendor detail, customer detail, inventory, shipments and the invoicing screens have all been driven at some point. **The WhatsApp dispatch dialog never has** — across four passes — and the prototype calls it one of the two things staff do from a phone. Carried here rather than scheduled.

---

## H-10 — Accept that recent milestones close on suite evidence alone (N-27)

**Status:** Open — recorded, **not scheduled**
**Blocks:** nothing; it is an accepted evidence standard
**Why human-blocked:** It is a deliberate trade the user has made, not a defect to fix.

Worth stating plainly so it is a known trade rather than an assumption: on the last two milestones a live/browser pass each found a real defect the suites **structurally could not** — M5's D-50 (fixtures encoded the same wrong assumption as the code) and M6's paid-date rendering (value stored, sent and parsed correctly; wrong only once rendered in a timezone). Future milestones close without that net. Requesting a targeted pass at any point closes the gap for that surface.

---

## H-11 — Per-screen visual sign-off against the prototype (E12-08)

**Status:** Open (M8)
**Blocks:** E12-08
**Why human-blocked:** Someone has to look and approve.

TECH_SPEC §5.1 explicitly warns not to assume markup identity produces rendering identity. Nine-plus ported screens need diffing against the prototype and signing off.

---

## H-12 — Mobile-responsive sign-off (E12-01)

**Status:** Open (M8)
**Blocks:** E12-01
**Why human-blocked:** Needs a real phone in a real hand.

FSD A6 and the NFRs require intake and dispatch to stay achievable in a few taps on a phone. Checked against the prototype's `mobile` reference screen.

---

## H-13 — Go-live: real accounts, real master data, handover (E12-09)

**Status:** Open (end of project)
**Blocks:** launch
**Depends on:** H-1, H-2, H-3
**Why human-blocked:** Real staff identities, the business's actual categories/statuses, and an owner who has been walked through account management.

Real accounts created with temp passwords, actual master data configured, backups confirmed running, and a short operating guide handed over.

> **Related trap (not a task):** the bootstrap admin password is printed **once** on first startup and diverges from `.env` after the first forced change (N-13). Capture it at first boot of the production stack or the account becomes unrecoverable.

---

## Changelog

- **2026-08-03** — File created (guardrail G2). Seeded H-1…H-13 from `ACTION_PLAN.md` §6, §17.7, §17.8 and the FSD open questions. H-1 already Asked; all others Open.
