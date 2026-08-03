# Action Plan & Delivery Backlog — Sourcing Ops Platform (Phase 1)

Status: Draft for review
Source documents (authoritative — this plan invents no scope beyond them):

- `C:\work\DevZone\Hermes\China_M2C\Functional_Spec.docx` — Functional Specification (FSD) v1.0, Phase 1 baseline, **including the Section 6.8 Lightweight Invoicing module (FR-BIL-01…07)**.
- `C:\work\DevZone\Hermes\China_M2C\docs\TECH_SPEC.md` — Technical Specification implementing that FSD.

> **Source-of-truth note.** The readable mirror at `C:\work\DevZone\Hermes\China_M2C\Source\uploads\spec.txt` (and `spec_document.xml`) were found stale during this planning pass — they predated the invoicing addition. Both have since been regenerated from the current `.docx` and now include §6.8/FR-BIL-*/A9/Q9. Re-run the same regeneration after any future edit to the `.docx` so the mirrors don't drift again (see DR-13).

> **Delivery status is tracked in §9–§15.** This document is now a living tracker, not only a plan. Each close-out section records per-story status with what was actually verified and how: §9 M1, §10 M2, §11 M3, §12 the E0 design pass, §13 the M4 pass, **§14 the M4 close-out (supersedes §13's partial status)** §15 the M5 backend pass (a mid-milestone checkpoint at 10 of 13), and **§16 the M5 close-out — M5 is COMPLETE, 13 of 13 (supersedes §15's status)**. Statuses there are set from executed commands, never from intent — and where a story is closed on test evidence alone rather than live verification, that is stated on the story rather than left for the reader to infer.

## 1. Scope framing

This plan covers **FSD Phase 1 only**: an internal-only operations tool for the business owner and her staff, with no customer or vendor logins (FSD A1). In scope: structured lead intake and CRM, vendor roster management, vendor catalog sections with versioned PDF documents, inventory and outbound shipment tracking, **lightweight document-based invoicing** (FSD §6.8 — generate, issue, track status, mark paid; no GST engine, no payment gateway, no reconciliation, per A9), WhatsApp **click-to-chat** catalog/invoice dispatch (deep link only, not the Business API — A2), operational dashboards, and RBAC with configurable master data.

Explicitly **not** planned here, per FSD §3.2 and TECH_SPEC §11: customer/vendor portals, RFQ/quotations, orders, payment processing, landed-cost engine, WhatsApp Business API, accounting/GST/customs integrations, native mobile apps, multi-tenancy.

Technical shape is fixed by TECH_SPEC: .NET 10 Web API in four projects (Api/Application/Domain/Infrastructure, no generic repository, no MediatR/AutoMapper), PostgreSQL + EF Core with lookup-table master data, permission/policy RBAC over two seeded roles (SuperAdmin, Associate), JWT with temp-password account management, `ICacheService` defaulting to in-memory, Angular standalone-component SPA porting the approved prototype 1:1, and a three-container Docker deployment (Caddy + api + db) on one Hostinger VPS.

## 2. Assumptions made by this plan

These are **planning** assumptions, not requirements — neither source document covers them. Each needs a yes/no before the sequence hardens.

| # | Assumption | Why it is needed | If wrong |
| --- | --- | --- | --- |
| PA-1 | **Team size and velocity are unknown.** No story points, no calendar dates, no sprint counts are assigned anywhere in this document. Milestones are ordered by dependency, not duration. | The plan must be sequenceable before staffing is known. | Re-slice milestones into sprint-sized batches once velocity is observed; nothing else changes. |
| PA-2 | A **single delivery team** works the backlog front-to-back. Where two epics are marked parallel-safe, that is an option, not a requirement. | Determines whether the parallel tracks in §4 are usable. | With one developer, collapse parallel tracks into the stated dependency order. |
| PA-3 | "Demoable in a prod-like environment" means the **local `docker-compose` stack** (Caddy + api + db) from M1 onward, plus the real VPS as a separate deploy target. There is **no dedicated staging VPS** — TECH_SPEC §7 describes exactly one production box. | Milestone exit criteria depend on where demos happen. | If the business wants a true staging environment, add a second small VPS (cost decision) — see DR-9. |
| PA-4 | MoSCoW **Should**/**Could** requirements are in the backlog but sequenced after all **Must** items in their epic, and are the first candidates to drop if the release needs trimming. FSD §6 says S/C "may slip to a later phase without blocking launch". | Gives an explicit descope lever. | None — this is the FSD's own stated intent. |
| PA-5 | Frontend and backend for a given feature are built by the **same slice of work**, not handed between separate teams. | Justifies pairing screen-port stories inside feature epics rather than a separate "frontend phase". | If frontend is a separate team, pull all screen-port stories into a parallel track gated on the API contract of their epic. |

## 3. Epic map

| Epic | Title | FSD coverage | TECH_SPEC coverage |
| --- | --- | --- | --- |
| E0 | Design Pass — Un-prototyped Screens | FR-ADM-01, FR-ADM-03, FR-BIL-01…04 (UI only) | §5.1, §5.2 (Gap), OI-4 |
| E1 | Foundation, Infrastructure & Auth | FR-ADM-03; NFR Security | §4.1, §4.2, §4.5, §4.6, §4.8, §5.3, §5.4, §6 |
| E2 | Deployment & Ops Skeleton | NFR Reliability/Availability | §2, §7.1–§7.5 |
| E3 | Master Data & RBAC Scaffold | FSD §3.3, §4 (personas), FR-ADM-02, FR-ADM-04 | §4.3, §4.5, §6 |
| E4 | Customer Intake & CRM | UC-01, UC-02, FR-CRM-01…11 | §4.7 `CustomersController`, §5.2, §6 |
| E5 | Vendor Management | UC-03, FR-VEN-01…07 | §4.7 `VendorsController`, §5.2, §6 |
| E6 | Catalog & Document Management | UC-04, FR-CAT-01…08 | §4.6, §4.7 Catalog controllers, §5.2, §6 |
| E7 | Inventory & Shipment Tracking | UC-06, UC-07, FR-INV-01…09 | §4.7 `InventoryController`/`ShipmentsController`, §5.2, §6 |
| E8 | Lightweight Invoicing | FR-BIL-01…07 (FSD §6.8) | §4.7 `InvoicesController`, §6, OI-4 |
| E9 | WhatsApp Dispatch | UC-05, FR-WA-01…07, FR-CAT-08 | §4.7 `DispatchController`, §5.2, §6 |
| E10 | Analytics & Dashboards | UC-08, FR-AN-01…08 | §4.7 `AnalyticsController`, §4.5, §5.2 |
| E11 | Admin & Account Management | UC-09, FR-ADM-01…05 | §4.4, §4.7 `AdminUsersController`/`MasterDataController`/`AuditController`, §5.3 |
| E12 | Hardening, NFR & Launch Readiness | FSD §8 (all NFRs) | §7.3, §7.4, §8, §9 |

---

## 4. Backlog by epic

Story ID convention: `<epic>-<nn>`. Each story states acceptance criteria in 1–2 lines plus its traceability. `[M]`/`[S]`/`[C]` mirrors the FSD's MoSCoW priority where the story implements a prioritised FR; enabling/technical stories carry no MoSCoW marker and are implicitly must-have.

### E0 — Design Pass: Un-prototyped Screens (TECH_SPEC OI-4)

Four screens have no approved design. This epic exists so their design is settled **before** the milestone that builds them, without blocking the corresponding backend work.

| ID | Story | Acceptance criteria | Refs |
| --- | --- | --- | --- |
| E0-01 | Extract the prototype's design system into a written token/atom reference | A short reference lists the exact colour values, spacing, font stack, emoji nav glyphs, and the status→colour maps (`SVC`, `ST`) taken from the approved prototype, plus the reusable atoms (button, card, `.table` list, form field, dialog, stat tile). Every net-new screen is designed from this reference only. | TECH_SPEC §5.1, §5.3 |
| E0-02 | Design the Login screen | A screen design exists using only E0-01 atoms: email + password, error state, and a route the unauthenticated user always lands on. | OI-4, FR-ADM-03 |
| E0-03 | Design the Force-password-change screen | A screen design exists for the `MustChangePassword` state: new password + confirm, no navigation to the rest of the app until submitted. | OI-4, TECH_SPEC §4.2 |
| E0-04 | Design the Admin area (User management + Master data configuration) | Designs exist for: a user list (incl. inactive), create-user with the **temp password shown once**, reset password, deactivate, role assignment; plus a master-data configuration view covering categories, service types, lead/shipment/invoice statuses with add/rename/retire and ordering. | OI-4, FR-ADM-01, FR-ADM-02, TECH_SPEC §4.4 |
| E0-05 | Design the Invoicing screens (list + generate/detail) | Designs exist for an invoice list with filters (customer, status, service type, date range) and an invoice generate/detail view showing number, date, line description, amount, tax, status lifecycle, PDF download, and mark-paid. | OI-4, FR-BIL-01…04, FR-BIL-06 |
| E0-06 | Business sign-off on the four net-new screens | Owner has reviewed and approved E0-02…E0-05 as belonging to the same visual language as the approved prototype. This sign-off is a hard gate for E1-13/E1-14, E8-09/E8-10 and E11-07/E11-08. | OI-4 |
| E0-07 | Close TECH_SPEC OI-3 (`_ds/industry-…` bundle) | Written confirmation that the unused design-system bundle in `Source/` is prototyping-tool scaffolding and is excluded from the port. | OI-3, TECH_SPEC §5.1 |

### E1 — Foundation, Infrastructure & Auth

| ID | Story | Acceptance criteria | Refs |
| --- | --- | --- | --- |
| E1-01 | Scaffold the four-project solution and pin versions | `SourcingOps.sln` builds with Api/Application/Domain/Infrastructure and the two test projects; `Domain` has no framework dependencies; exact .NET and Angular versions are recorded, closing OI-7. | TECH_SPEC §4.1, OI-7 |
| E1-02 | EF Core `DbContext`, `IAppDbContext` and the initial migration | A single initial migration creates every table in TECH_SPEC §6 with the stated FK relationships; `Application` depends only on the narrow `IAppDbContext` interface; no generic repository is introduced. | TECH_SPEC §4.1, §6 |
| E1-03 | Seed migration: permissions, roles, role→permission map, default lookups | Startup seeding creates the full permission catalog, the `SuperAdmin` and `Associate` roles with the mapping from TECH_SPEC §4.3, the six FSD categories, both service types, and the default lead/shipment/invoice status sets. Re-running is idempotent. | TECH_SPEC §4.3, §6; FSD A4 |
| E1-04 | `ICacheService` with in-memory default and Redis switch | `MemoryCacheService` is registered by default; setting `Caching:Provider=Redis` swaps to `RedisCacheService` with no code change; no application code references `IMemoryCache`/`IDistributedCache` directly. | TECH_SPEC §4.5, C2 |
| E1-05 | `IFileStorage` with local-disk implementation and PDF validation | Files save/read/delete against the configured root path using the §4.6 path convention; uploads are rejected unless content-type **and** `%PDF` magic bytes match and size is under the configured cap. | TECH_SPEC §4.6; FR-CAT-06 [M] |
| E1-06 | Cross-cutting middleware | Unhandled errors return RFC 7807 `ProblemDetails`; structured request logs go to stdout; CORS is restricted to the single frontend origin; `/auth/login` is rate-limited via built-in middleware. | TECH_SPEC §4.8, §8 |
| E1-07 | Login, JWT issuance, refresh-token rotation, logout | Valid credentials return an access token carrying `sub`, `role`, and the resolved `permissions[]`, plus a refresh token stored hashed with expiry; refresh rotates the token; logout revokes it; invalid credentials give no account-existence hint. | TECH_SPEC §4.2; FR-ADM-03 [M] |
| E1-08 | `MustChangePassword` enforcement and change-password endpoint | A user with the flag set can authenticate but every endpoint except change-password returns 403 via `RequirePasswordChangeFilter`; a successful change clears the flag and issues a full-scope token. | TECH_SPEC §4.2 |
| E1-09 | `IAuditLogger` and the audit write path | A single call site records user id, action, entity type, entity id, timestamp and JSON detail into `audit_logs`; auth events are covered too. Available for every mutating service before feature CRUD begins. | TECH_SPEC §4.3; FR-ADM-04 [S] |
| E1-10 | Angular app scaffold with ported base styles and shared atoms | `provideRouter` with lazy `loadComponent` per feature; no NgModules; `shared/` holds the ported base stylesheet and the button/card/table/dialog/form-field atoms from E0-01; production build succeeds with bundle budgets configured. | TECH_SPEC §5.3, §5.4, C6 |
| E1-11 | `ApiService`, JWT interceptor and 401 handling | All HTTP calls go through a thin `ApiService`; the interceptor attaches the bearer token and redirects to `/login` on 401; token and refresh handling live in one place. | TECH_SPEC §5.3 |
| E1-12 | Route guards from JWT permission claims | `canActivate` guards decode the permission set from the token and block routes the user lacks; nav items for those routes are hidden. Server remains the authority (defence in depth). | TECH_SPEC §5.3, §4.3 |
| E1-13 | Login screen (Angular) | Implements the E0-02 design; on `MustChangePassword` it routes to the force-change screen instead of the shell. **Gated on E0-06.** | FR-ADM-03 [M], OI-4 |
| E1-14 | Force-password-change screen (Angular) | Implements the E0-03 design; no shell navigation is reachable until the password is changed; success lands the user on the dashboard. **Gated on E0-06.** | TECH_SPEC §4.2, OI-4 |
| E1-15 | App shell: sidebar, topbar, permission-filtered nav | The navy sidebar, topbar and emoji nav icons match the prototype 1:1; nav entries render only for permissions present in the token. | TECH_SPEC §5.1, §5.3 |
| E1-16 | `StatusStyleService` / pipe | The prototype's per-status colour maps are defined once and consumed by every list and detail screen — never copy-pasted per screen. | TECH_SPEC §5.1 |
| E1-17 | Health endpoint | `GET /api/v1/health` returns API and database reachability, usable by the deploy smoke check in E2-10. | TECH_SPEC §7.2 |

### E2 — Deployment & Ops Skeleton

Deliberately early: from M1 onward every subsequent story is demoable in a prod-like stack rather than only on a developer machine.

| ID | Story | Acceptance criteria | Refs |
| --- | --- | --- | --- |
| E2-01 | Multi-stage Dockerfile for the API | Produces a runtime-only image (no SDK layer) that starts against the compose Postgres and honours env-var configuration. | TECH_SPEC §7.1, C1 |
| E2-02 | `docker-compose.yml` + `docker-compose.override.yml` for local dev | `docker compose up` starts `api` + `db` with the `uploads` and `pgdata` volumes; Angular runs via `ng serve` with a dev proxy; `--profile redis` is the only way `redis` starts. | TECH_SPEC §7.1, C2, C3 |
| E2-03 | Angular production build baked into the Caddy image | `ng build` output is copied into the `caddy` static root at image build time; no Node runtime exists in the production image. | TECH_SPEC §5.5, §7.2 |
| E2-04 | `Caddyfile` with TLS, static serving and `/api/*` proxy | Automatic HTTPS on the real domain; SPA deep links fall back to `index.html`; `encode gzip zstd` is on; `/api/*` reverse-proxies to the api container. | TECH_SPEC §7.2 |
| E2-05 | Provision the VPS and deploy the walking skeleton | The Hostinger box runs `caddy` + `api` + `db` over HTTPS; login works end-to-end against seeded data; `redis` is absent from the running container list. | TECH_SPEC §2, §7.2 |
| E2-06 | CI: build, test, image push | Pipeline restores, builds, runs unit **and** integration tests, builds both images and pushes to the registry. Confirms the runner can run Testcontainers Postgres (see DR-11). | TECH_SPEC §4.1, §7.5 |
| E2-07 | Deploy job with an explicit migration step | Deploy pulls images and restarts the stack, then runs `dotnet ef database update` as a **separate, explicit** step — never auto-migrating on container start in production. | TECH_SPEC §6, §7.5 |
| E2-08 | Nightly backups of Postgres **and** the uploads volume | A scheduled job writes a `pg_dump` and a snapshot of the `uploads` volume off-box on the same schedule; both are verified present. Retention window is set once OI-5 is answered. | TECH_SPEC §7.4, OI-5 |
| E2-09 | Secrets and environment configuration | JWT signing key, DB password and storage root come from environment/secret store; `.env` files are git-ignored; no secret appears in the repository or in image layers. | TECH_SPEC §8 |
| E2-10 | Resource limits and post-deploy smoke check | Container memory limits from TECH_SPEC §7.2 are applied and the box's usage is inside the §7.3 budget; the deploy job fails if the health endpoint does not return healthy. | TECH_SPEC §7.2, §7.3 |

### E3 — Master Data & RBAC Scaffold

Must land before any feature CRUD, because every feature screen resolves its dropdowns from these lookups and every feature endpoint is permission-gated.

| ID | Story | Acceptance criteria | Refs |
| --- | --- | --- | --- |
| E3-01 | Permission requirement, handler and policy registration | Policies are registered by iterating the seeded permission catalog at startup; `[Authorize(Policy = "Customers.Edit")]`-style attributes enforce correctly; adding a permission requires one catalog entry, not new plumbing. | TECH_SPEC §4.3, C4 |
| E3-02 | Additive role resolution | A user holding multiple roles receives the **union** of their permissions in the token; the model supports this even though Phase 1 seeds one role per user. | TECH_SPEC §4.3; FSD §4, A5 |
| E3-03 | Categories master data CRUD | Categories can be added, renamed, reordered and retired via `MasterDataController`; retiring hides a category from new selections without breaking existing references. | FR-ADM-02 [M]; FSD §3.3, A4 |
| E3-04 | Service types lookup | `CIF` and `FREIGHT_ONLY` exist as lookup rows with editable labels; every service-type field is an FK to this table, never an enum or free text. | FR-ADM-02 [M], FR-CRM-02 [M]; TECH_SPEC §6 |
| E3-05 | Lead/customer status pipeline lookup | Pipeline stages (New → Qualified → Active → Won → Lost/Dormant by default) are configurable rows with sort order. | FR-CRM-05 [M], FR-ADM-02 [M] |
| E3-06 | Shipment status lookup | Shipment lifecycle stages (Packed → Dispatched → In Transit → Delivered by default) are configurable rows with sort order. | FR-INV-05 [M], FR-ADM-02 [M] |
| E3-07 | Invoice status lookup | Invoice lifecycle (Draft → Issued → Paid → Cancelled by default) is a configurable lookup. | FR-BIL-02 [M], FR-ADM-02 [M] |
| E3-08 | Retire/reorder semantics and referential safety | Attempting to delete a lookup row still referenced by records is prevented and offers retire instead; retired rows still render correctly on historical records. | FSD §3.3; TECH_SPEC §6 |
| E3-09 | Cache master-data reads and invalidate on write | Lookup reads are served through `ICacheService`; any master-data mutation invalidates the relevant keys so a Super Admin edit is visible immediately. | TECH_SPEC §4.5 |
| E3-10 | Frontend master-data service | A single Angular service loads all lookups once and feeds every dropdown in the app; **no feature component hard-codes a category, status or service-type list**. | FSD §3.3; TECH_SPEC §5.3 |

### E4 — Customer Intake & CRM (UC-01, UC-02)

| ID | Story | Acceptance criteria | Refs |
| --- | --- | --- | --- |
| E4-01 | Customer entity and CRUD endpoints | Create/read/update a customer with name, business name, phone/WhatsApp, optional email, city/region, source channel and notes; permission-gated on `Customers.View`/`.Edit`; mutations audit-logged. | FR-CRM-01 [M]; TECH_SPEC §4.7 |
| E4-02 | Mandatory service type on the customer | A customer cannot be saved without a service type FK; the value drives conditional fields downstream. | FR-CRM-02 [M] |
| E4-03 | Category interest (many-to-many) | Zero or more categories can be attached to a customer from the configurable category master via `customer_categories`. | FR-CRM-03 [M] |
| E4-04 | Validated international phone storage | Phone is stored with country code in a normalised international format and is directly usable to build a `wa.me` link without further transformation. | FR-CRM-04 [M]; FSD NFR Localisation |
| E4-05 | Lead status pipeline on the customer | Status is set from the configurable lead-status lookup; each change is recorded on the activity timeline with user and timestamp. | FR-CRM-05 [M], FR-CRM-07 [M] |
| E4-06 | List, search and filter customers | List endpoint supports `search`, `page`, `pageSize` and filters by status, service type, category, region and owner, returning results within the NFR performance envelope. | FR-CRM-06 [M]; TECH_SPEC §4.7 |
| E4-07 | Interactions/notes and the activity timeline | `POST /{id}/interactions` records a note with author and timestamp; the timeline endpoint returns interactions, status changes, catalog dispatches and invoices in one chronological feed. | FR-CRM-07 [M], FR-CRM-10 [S], FR-BIL-03 [M] |
| E4-08 | Follow-up reminders on interactions | An interaction can carry a follow-up date and surface as due. **CONFIRMED IN SCOPE (FSD Q2 answered 2026-07-27)** — the business wants active reminders, i.e. a "due follow-ups" view, not a passive activity log. No longer conditional. Needs **no migration**: `interactions.follow_up_date` already exists from E1-02, so this is surfacing only. | FR-CRM-10 [S]; FSD Q2 |
| E4-09 | Customer owner assignment and reassignment | A customer can be assigned to a staff user and reassigned; the change is audit-logged and appears on the timeline. | FR-CRM-08 [S] |
| E4-10 | Duplicate phone detection | Creating a customer with an existing phone number surfaces the existing record and requires explicit confirmation to proceed. | FR-CRM-09 [S] |
| E4-11 | Customer tags | Free-form tags can be added/removed and are filterable in the list view. | FR-CRM-11 [C] |
| E4-12 | External-purchase reference for freight-only customers | For freight-only customers the intake form captures the external purchase reference shown in the prototype. **Scope of the fields is blocked on FSD Q1** — build the minimum the prototype shows until answered. | FSD Q1; TECH_SPEC §5.2 (`intake`) |
| E4-13 | Port the New Lead Intake screen | The `intake` screen matches the prototype 1:1 in structure and styling; all dropdowns come from the master-data service; conditional freight-only fields behave per E4-12. | UC-01; TECH_SPEC §5.2 |
| E4-14 | Port the Customers list screen | The `customers` screen matches the prototype 1:1; search and the service type / status / category filters call the E4-06 endpoint. | UC-02, FR-CRM-06 [M]; TECH_SPEC §5.2 |
| E4-15 | Port the Customer detail screen | The `custdetail` screen matches the prototype 1:1 and renders the profile, activity timeline, note entry and the WhatsApp dispatch launch point (wired in E9). | UC-02, FR-CRM-07 [M]; TECH_SPEC §5.2 |

### E5 — Vendor Management (UC-03)

| ID | Story | Acceptance criteria | Refs |
| --- | --- | --- | --- |
| E5-01 | Vendor entity and CRUD endpoints | Create/read/update a vendor with name, contact person, phone/WhatsApp, email, country/city and notes; permission-gated and audit-logged. | FR-VEN-01 [M] |
| E5-02 | Vendor→category mapping | A vendor can be mapped to one or more categories from the category master via `vendor_categories`. | FR-VEN-02 [M] |
| E5-03 | Vendor status | Active / inactive / on-hold is maintained on the vendor and shown in every listing. | FR-VEN-03 [M] |
| E5-04 | List, search and filter vendors | List endpoint filters by name, category, region and status with paging. | FR-VEN-04 [M] |
| E5-05 | Vendor detail shows catalog sections and PDFs | The vendor detail response includes its catalog sections and their documents. Depends on E6. | FR-VEN-05 [M] |
| E5-06 | Commercial metadata | MOQ, lead time, payment terms and reliability rating are optional fields on the vendor, editable and displayed on detail. | FR-VEN-06 [S] |
| E5-07 | Vendor-level documents (licence, quality certs) | Non-catalog documents can be attached to a vendor and downloaded through the authenticated endpoint. **Requires a schema addition** — TECH_SPEC §6 has no vendor-documents table (see DR-4). | FR-VEN-07 [C]; TECH_SPEC §6 |
| E5-08 | Port the Vendors list screen | The `vendors` screen matches the prototype 1:1 with category / region / status filters wired to E5-04. | TECH_SPEC §5.2 |
| E5-09 | Port the Vendor detail screen | The `vendordetail` screen matches the prototype 1:1: profile, catalog sections, MOQ/lead time/rating, and edit. | TECH_SPEC §5.2 |
| E5-10 | Vendor document upload UI | The vendor detail screen can attach and download a **non-catalog** compliance document (business licence, quality cert, test report), with a document-type dropdown **filtered to `Vendor` scope** — `MasterDataService.documentTypeOptions('Vendor')` already exists for it. Distinct from the catalog upload dialog, which files catalog PDFs into sections and has no type selector. **Backend is already built and tested** (`POST\|GET /vendors/{id}/documents`, `/vendor-documents/{id}/download\|DELETE`, E5-07/D-25); this is the frontend half, which was never written. Raised as **N-22** during the M5 screen pass, when N-20 (c) assumed two scope-filtered dropdowns and found one. Confirm against FSD Q5 before building: compliance documents are filed **for reference with no enforcement**, so this is an upload/list/download screen and explicitly **not** a blocking-rules feature. | FR-VEN-07 [C]; §16.6 N-22 |

### E6 — Catalog & Document Management (UC-04)

| ID | Story | Acceptance criteria | Refs |
| --- | --- | --- | --- |
| E6-01 | Catalog section CRUD under a vendor | A section is created against a vendor with title, category and optional tags; sections are listable per vendor. | FR-CAT-01 [M] |
| E6-02 | Upload PDF documents to a catalog section | Upload stores the file via `IFileStorage` and records original filename, size, uploader and upload date; multiple documents per section are supported. | FR-CAT-02 [M]; TECH_SPEC §4.6 |
| E6-03 | Catalog document versioning | Uploading a new version marks it `is_latest` and demotes the previous one; older versions remain listable and downloadable. | FR-CAT-03 [M] |
| E6-04 | Authenticated preview/download | Documents are served only through a permission-checked download endpoint — never from a public static path — and can be previewed before sending. | FR-CAT-04 [M]; TECH_SPEC §8 |
| E6-05 | Cross-vendor catalog browse and search | Sections are searchable across all vendors by category, vendor and title with paging. | FR-CAT-05 [M] |
| E6-06 | Enforce upload validation on the catalog endpoint | Non-PDF content, magic-byte mismatches and oversized files are rejected with a clear `ProblemDetails` message. Uses E1-05. | FR-CAT-06 [M] |
| E6-07 | Catalog section tags | Sections can be tagged (e.g. "new arrivals") and filtered by tag. | FR-CAT-07 [C] |
| E6-08 | Port the Catalogs screen and upload dialog | The `catalogs` screen matches the prototype 1:1 including the upload dialog and category/vendor/title browse. | TECH_SPEC §5.2 |

### E7 — Inventory & Shipment Tracking (UC-06, UC-07)

| ID | Story | Acceptance criteria | Refs |
| --- | --- | --- | --- |
| E7-01 | Inventory item CRUD | Items carry name/description, category, source vendor, unit and on-hand quantity; permission-gated and audit-logged. **FSD Q3 is answered (TECH_SPEC OI-8): inventory is tracked per SKU, with SKU optional** — the schema's nullable `sku` column already matches, so the blocker this row previously carried is gone (unblocked 2026-07-29, see §14.6). | FR-INV-01 [M]; FSD Q3 |
| E7-02 | Record inbound stock | An inbound entry with date and reference increases on-hand quantity atomically and is audit-logged. | FR-INV-02 [M] |
| E7-03 | List and filter inventory | Inventory list filters by category, vendor and stock level with paging. | FR-INV-06 [M] |
| E7-04 | Low-stock indicator | A configurable reorder threshold per item drives a low-stock flag returned by the API and rendered as the prototype's visual bar. | FR-INV-07 [S] |
| E7-05 | Shipment and shipment-line CRUD | An outbound shipment records customer, destination, dispatch date, service type, status and one or more lines referencing inventory items. | FR-INV-03 [M] |
| E7-06 | Decrement stock on shipment, guard negatives | Recording a shipment decrements on-hand quantity in the same transaction; a shipment that would drive stock negative is rejected or explicitly flagged per the FSD's "prevent or flag" wording. | FR-INV-04 [M] |
| E7-07 | Shipment status transitions | Status moves through the configurable shipment-status lookup; each transition is audit-logged and timestamped. | FR-INV-05 [M]; FSD NFR Auditability |
| E7-08 | List and filter shipments | Shipments filter by status, customer and date range, backing the prototype's status tabs. | FR-INV-06 [M] |
| E7-09 | Shipment reference documents | Packing list / AWB / BL documents attach to a shipment and download through the authenticated endpoint; AWB/BL number is stored on the shipment. | FR-INV-08 [S] |
| E7-10 | Freight-only shipments without stock movement | A freight-only shipment can be recorded with no inventory lines and performs **no** stock decrement, per FSD A8. | FR-INV-09 [S]; FSD A8 |
| E7-11 | Port the Inventory screen | The `inventory` screen matches the prototype 1:1 including search/filter and the low-stock visual bar. | TECH_SPEC §5.2 |
| E7-12 | Port the Shipments list screen | The `shipments` screen matches the prototype 1:1 including the status tabs. | TECH_SPEC §5.2 |
| E7-13 | Port the Shipment detail screen | The `shipdetail` screen matches the prototype 1:1 including line items and the reference-documents section (its mock data already contains an invoice doc slot — link it to the real invoice in E8). | TECH_SPEC §5.2 |

### E8 — Lightweight Invoicing (FSD §6.8)

No prototype exists for this module — UI stories are gated on E0-05/E0-06.

| ID | Story | Acceptance criteria | Refs |
| --- | --- | --- | --- |
| E8-01 | Generate an invoice against a customer | An invoice is created with invoice number, date, line description, amount, tax amount and currency, optionally referencing a shipment (CIF) or standing alone (freight-only service). | FR-BIL-01 [M] |
| E8-02 | Invoice status lifecycle | Status is driven by the configurable invoice-status lookup (Draft → Issued → Paid → Cancelled by default), changed manually by staff, and each transition is audit-logged. | FR-BIL-02 [M] |
| E8-03 | Render, store and download the invoice PDF | Issuing an invoice produces a PDF stored via `IFileStorage` at the §4.6 invoice path and downloadable through the authenticated endpoint. **Requires choosing a PDF generation approach — TECH_SPEC's stack table names none (see DR-3).** | FR-BIL-03 [M]; TECH_SPEC §4.6 |
| E8-04 | Invoices on the customer activity timeline | Invoice creation and status changes appear in the customer's timeline feed from E4-07. | FR-BIL-03 [M], FR-CRM-07 [M] |
| E8-05 | List, search and filter invoices | Invoices filter by customer, status, service type and date range with paging. | FR-BIL-04 [M] |
| E8-06 | Send an invoice PDF over the WhatsApp dispatch flow | The invoice detail view launches the same click-to-chat dispatch dialog used for catalogs, and the send is recorded in the dispatch log. Depends on E9. | FR-BIL-05 [S] |
| E8-07 | Mark an invoice paid | A manual paid marker records date and an optional reference (e.g. bank transfer note), gated behind the `Invoicing.MarkPaid` permission. No gateway or reconciliation. | FR-BIL-06 [S]; TECH_SPEC §4.3; FSD A9 |
| E8-08 | Invoice numbering sequence and company billing details | A configurable numbering sequence and default billing/company details are applied to generated invoices. **Blocked on FSD Q9** (numbering format, GSTIN, registered address, bank details). | FR-BIL-07 [C]; FSD Q9 |
| E8-09 | Build the Invoicing list screen | Implements the E0-05 list design against E8-05, using only E0-01 atoms. **Gated on E0-06.** | FR-BIL-04 [M], OI-4 |
| E8-10 | Build the Invoice generate/detail screen | Implements the E0-05 detail design: generate, status transitions, PDF download, mark paid. **Gated on E0-06.** | FR-BIL-01…03, FR-BIL-06, OI-4 |

### E9 — WhatsApp Dispatch (UC-05)

| ID | Story | Acceptance criteria | Refs |
| --- | --- | --- | --- |
| E9-01 | `wa.me` deep-link builder | An Infrastructure component builds a click-to-chat URL from a stored international phone number plus pre-filled text, correctly URL-encoded. No Business API dependency. | FR-WA-02 [M]; FSD A2; TECH_SPEC §4.1 |
| E9-02 | Dispatch log endpoint | `POST /dispatch-log` records customer, catalog document (or invoice), staff user, message and timestamp, gated on `Dispatch.Send`. | FR-WA-04 [M]; TECH_SPEC §4.7 |
| E9-03 | "Send via WhatsApp" on the customer record | The customer detail screen exposes the dispatch action, pre-addressed to that customer's stored number. | FR-WA-01 [M] |
| E9-04 | Port the 3-step dispatch dialog | The prototype's **Download PDF → Open WhatsApp → Attach & send in chat** dialog is ported 1:1, with **Log Dispatch as a separate, ungated action** rather than a third step; step 1 uses the authenticated download so the file is ready to attach in WhatsApp with minimal steps. **Corrected 2026-07-29** — this row previously read "open chat → download PDF → mark sent", which contradicts the prototype it instructs porting and was already overridden during implementation. See §14.4 for the full rationale, including why Log Dispatch is deliberately not gated on completing the three steps. | FR-WA-02 [M], FR-WA-03 [M]; TECH_SPEC §5.2, ACTION_PLAN §14.4 |
| E9-05 | Launch dispatch from a catalog section | From a catalog section a staff member can pick a customer and enter the same dispatch flow in one go. | FR-WA-06 [S] |
| E9-06 | Configurable message template | A template with customer-name and catalog-name placeholders is stored as configuration and pre-fills the dispatch message; the text remains editable before sending. | FR-WA-05 [S], FR-ADM-05 [S] |
| E9-07 | "Sent to" history on a catalog document | A catalog document shows which customers received it and when, derived from the dispatch log. | FR-CAT-08 [S] |
| E9-08 | Confirm and apply dispatch permission scope | `Dispatch.Send` is granted per the business's answer to **FSD Q4** (any staff vs specific roles); the seeded role mapping matches that answer. | FR-WA-01 [M]; FSD Q4; TECH_SPEC §4.3 |
| E9-09 | Keep the Phase 2 API upgrade seam | Dispatch logic sits behind an interface so a Business-API sender can replace the deep-link builder later without changing the dispatch log or the calling screens. | FR-WA-07 [S] |

### E10 — Analytics & Dashboards (UC-08)

| ID | Story | Acceptance criteria | Refs |
| --- | --- | --- | --- |
| E10-01 | Leads & conversion aggregate | Endpoint returns new leads over time, split by source channel and status, plus conversion rate through the pipeline. | FR-AN-01 [M] |
| E10-02 | Service-type split aggregate | Endpoint returns the CIF vs freight-only split across customers. | FR-AN-02 [M] |
| E10-03 | Category mix aggregate | Endpoint returns customer interest and activity by category, sourced from the configurable category master. | FR-AN-03 [M] |
| E10-04 | Vendor overview aggregate | Endpoint returns active vendors by category and catalog counts. | FR-AN-04 [M] |
| E10-05 | Inventory & shipments aggregate | Endpoint returns on-hand quantity/value by category and shipment counts by status. | FR-AN-05 [M] |
| E10-06 | Dispatch activity aggregate | Endpoint returns catalogs sent over time and by staff member. | FR-AN-06 [S] |
| E10-07 | Global date-range / category / service-type filters | All aggregate endpoints accept the same filter parameters and the dashboard applies them consistently across every tile. | FR-AN-07 [S] |
| E10-08 | CSV/Excel export | Any dashboard view or its underlying list can be exported. | FR-AN-08 [S] |
| E10-09 | Port the Dashboard screen | The `dash` screen matches the prototype 1:1: stat tiles, lead funnel, category mix, service split and shipment status, using the prototype's hand-rolled CSS bars rather than introducing a chart library. | UC-08; TECH_SPEC §5.2, §5.4 |
| E10-10 | Cache dashboard aggregates | Aggregate responses are served through `ICacheService` with a short TTL; nothing user-write-adjacent (live stock, shipment status) is cached. | TECH_SPEC §4.5 |

### E11 — Admin & Account Management (UC-09)

Backend stories can start once E3 lands; UI stories are gated on E0-04/E0-06.

| ID | Story | Acceptance criteria | Refs |
| --- | --- | --- | --- |
| E11-01 | Create a user with a generated temp password | `POST /admin/users` creates the account with `MustChangePassword=true` and returns the 12-character temp password **once** in the response, generated per TECH_SPEC §4.4 rules (crypto RNG, no ambiguous characters). Confirms OI-1. | FR-ADM-01 [M]; TECH_SPEC §4.4, OI-1 |
| E11-02 | Reset a user's password | Reset issues a new temp password, re-sets `MustChangePassword`, and revokes all existing refresh tokens for that user. | TECH_SPEC §4.4 |
| E11-03 | Deactivate a user (soft delete) | `DELETE /admin/users/{id}` sets `IsActive=false`, blocks login and revokes sessions while preserving ownership and audit history. Confirms OI-2 with the business first. | TECH_SPEC §4.4, OI-2 |
| E11-04 | List and search users including inactive | Super Admin can list, search and see active/inactive state for all accounts. | FR-ADM-01 [M] |
| E11-05 | Assign and change roles on a user | One or more roles can be assigned; the effective permission set is the union; changing roles revokes refresh tokens so the change takes effect promptly (see DR-10). | FR-ADM-01 [M]; TECH_SPEC §4.3 |
| E11-06 | Audit trail query and viewer | Read-only `AuditController` supports filtering by user, entity type and date range; a Super-Admin-only screen renders it. | FR-ADM-04 [S]; TECH_SPEC §4.7 |
| E11-07 | Build the User management screen | Implements the E0-04 user design including the show-temp-password-once interaction. **Gated on E0-06.** | FR-ADM-01 [M], OI-4 |
| E11-08 | Build the Master data configuration screen | Implements the E0-04 master-data design: add/rename/retire/reorder categories, service types and lead/shipment/invoice statuses, all Super-Admin-only. **Gated on E0-06.** | FR-ADM-02 [M], UC-09, OI-4 |
| E11-09 | WhatsApp template configuration screen | The dispatch message template(s) and default sender behaviour are editable in the admin area. | FR-ADM-05 [S] |

### E12 — Hardening, NFR & Launch Readiness

| ID | Story | Acceptance criteria | Refs |
| --- | --- | --- | --- |
| E12-01 | Mobile-responsive pass | Every screen is usable on a phone, checked against the prototype's `mobile` reference screen; intake and dispatch remain achievable in a few taps. | FSD NFR Usability, A6; TECH_SPEC §5.2 |
| E12-02 | Performance pass | FK and filter columns are indexed; every list and dashboard view loads within the 2–3 second NFR under representative data volumes. Data volume assumptions come from **FSD Q6**. | FSD NFR Performance, Q6; TECH_SPEC §9 |
| E12-03 | Security review | CORS origin, login rate limiting, upload path/magic-byte validation, authenticated-only document serving, TLS, unpublished Postgres port and secret handling are all verified against TECH_SPEC §8. | FSD NFR Security; TECH_SPEC §8 |
| E12-04 | Backup restore drill | A full restore from the nightly `pg_dump` **plus** the uploads snapshot is performed into a clean environment and verified — documents included, not just rows. Sets the RPO/RTO answer for OI-5. | FSD NFR Reliability; TECH_SPEC §7.4, OI-5 |
| E12-05 | Wire the global command palette across all entities | The ⌘K palette searches customers, vendors, catalogs, shipments and screens, matching the prototype's behaviour. Sequenced last because it depends on every feature's search endpoint. | TECH_SPEC §5.2 |
| E12-06 | Bundle budget verification | Production build passes the configured per-chunk budgets; no UI kit, icon font or chart library has crept in. | TECH_SPEC §5.4, C6 |
| E12-07 | Legacy data migration | Existing spreadsheets/records are imported at launch. **CONFIRMED IN SCOPE, SCOPE TBD (FSD Q8 answered 2026-07-27)** — there *is* data to bring over, so this story is explicitly **not** dropped. Cannot be estimated until a sample file arrives (what it contains, roughly how much). That same sample should also answer **Q6**, which is still unasked — the legacy volume *is* the initial data volume, so do not request them separately. | FSD Q8, Q6 |
| E12-08 | Per-screen visual sign-off against the prototype | Each ported screen is diffed against the prototype and signed off, per TECH_SPEC §5.1's explicit recommendation not to assume markup identity produces rendering identity. | TECH_SPEC §5.1, C5 |
| E12-09 | Go-live: real accounts, real master data, handover | Real staff accounts are created with temp passwords, the business's actual categories and statuses are configured, backups are confirmed running, and the owner has a short operating guide for account management. | UC-09; FSD §9 Phase 1 |

---

## 5. Recommended build sequence

Milestones are **dependency-ordered, not time-boxed** (PA-1). Each has an exit criterion that can be demonstrated in the prod-like stack (PA-3).

### M0 — Unblock: decisions and design
**Content:** E0 (all), plus written answers to TECH_SPEC **OI-1, OI-2, OI-3, OI-4, OI-7** and FSD **Q1, Q3, Q4, Q9** (the four questions that directly change story shape: freight-only external-purchase fields, inventory granularity, who may dispatch, invoice numbering/company details). Choose the invoicing PDF approach (DR-3).
**Why first:** E0 is the only work with a business-availability dependency rather than an engineering one, and it gates six later UI stories. Starting it now means the design pass runs *in parallel* with M1–M3 engineering and is finished long before M6/M8 need it.
**Exit:** E0-06 signed off; the listed open items answered in writing.

### M1 — Walking skeleton (deployable, authenticated, empty)
**Content:** E1-01…E1-12, E1-15…E1-17, and **all of E2**.
**Why here:** The FSD's configurable-master-data and RBAC principles are structural — retrofitting permission checks and audit logging across finished controllers is far more expensive than having them available from the first feature story (DR-8). Deploy comes with it, not after it, so that from this point on every story is demoable in the real topology rather than only on a laptop.
**Exit:** A user logs in over HTTPS on the VPS, is forced through password change, sees the empty ported shell, and CI builds/tests/deploys the stack with an explicit migration step. `redis` is not running.
**Note:** E1-13/E1-14 (login and force-change *screens*) need E0-06; if the design pass is still running, ship M1 with functional-but-unstyled auth pages and restyle them the moment E0-06 lands.

### M2 — Master data & RBAC live
**Content:** E3 (all), E11-01…E11-05 (account-management backend).
**Why here:** Every feature epic from M3 onward resolves dropdowns from these lookups and gates endpoints on these policies.
**Exit:** A Super Admin can create an Associate account (temp password shown once) and add/rename/retire a category; a feature endpoint correctly returns 403 for a missing permission.

### M3 — CRM vertical slice (first business value)
**Content:** E4 (Must stories E4-01…E4-07, E4-13…E4-15 first; then S/C E4-08…E4-12).
**Why here:** UC-01/UC-02 sit at the top of the funnel and are the FSD's stated primary pain (lead leakage). It is also the first end-to-end proof that the port-the-prototype approach works on a real, data-driven screen.
**Exit:** Staff can capture a lead from intake, find it in the list, open the detail and add a note, on the deployed environment.

### M4 — Sourcing side: vendors, catalogs, dispatch
**Content:** E5, E6, then E9.
**Why here:** E9 dispatch needs both a customer (M3) and a catalog document (E6) to exist, so it must follow them. E5 and E6 are parallel-safe with each other only up to E5-05, which needs catalog sections.
**Exit:** UC-03, UC-04 and UC-05 are demonstrable end-to-end: onboard a vendor, upload a versioned catalog PDF, open WhatsApp pre-addressed to a customer, and see the dispatch logged on the customer timeline.

### M5 — Inventory & shipments
**Content:** E7 (Must stories first, then E7-04, E7-09, E7-10).
**Why here:** Independent of M4 and parallel-safe with it if two workstreams exist (PA-2); it must precede M6 because CIF invoices reference a shipment.
**Exit:** UC-06 and UC-07 are demonstrable: inbound stock raises on-hand, an outbound shipment lowers it and cannot drive it negative, and a freight-only shipment moves no stock.

### M6 — Invoicing
**Content:** E8 (all), including E8-06 which reuses the M4 dispatch flow.
**Why here:** It is the newest scope, depends on both customers (M3) and shipments (M5), and its screens are net-new — by this point the E0-05 design has had the longest possible runway.
**Exit:** An invoice is generated against a CIF shipment and against a freight-only service, moves Draft → Issued → Paid, downloads as a PDF, and appears on the customer timeline.

### M7 — Analytics
**Content:** E10 (all).
**Why last among features:** Every aggregate reads from schema that M3–M6 finish shaping. Building dashboards before the underlying modules stabilise guarantees rework (DR-2).
**Exit:** UC-08 is demonstrable — the dashboard renders real data across all five Must aggregates with filters applied.

### M8 — Admin UI, hardening and launch
**Content:** E11-06…E11-09, E12 (all).
**Exit:** All FSD NFRs verified, a restore drill completed including uploaded documents, every ported screen visually signed off, real accounts and master data created, and handover done.

### Dependency summary

```
M0 (design + decisions) ──────────────┐  (runs parallel to M1–M3)
                                      ▼
M1 Foundation + Deploy ─► M2 Master data + RBAC ─► M3 CRM ─┬─► M4 Vendors/Catalogs/Dispatch ─┐
                                                            └─► M5 Inventory/Shipments ──────┤
                                                                                             ▼
                                                                                    M6 Invoicing
                                                                                             │
                                                                                             ▼
                                                                                    M7 Analytics
                                                                                             │
                                                                                             ▼
                                                                              M8 Admin UI + Launch
```

Hard gates worth restating:
- **E3 before any feature CRUD.** Permission policies, audit logging and lookup tables must exist before the first controller, not after.
- **E2 before M3.** The deploy skeleton is an early story precisely so nothing is demoed only on a laptop.
- **E0-06 before E8-09/E8-10, E11-07/E11-08, E1-13/E1-14.** Backend for those modules is *not* gated on the design — only the screens are.
- **E6 before E9; E4 + E7 before E8; everything before E10.**

---

## 6. Delivery risk register

Distinct from TECH_SPEC's OI-1…OI-7 (unresolved *technical decisions*) — these are risks to *how the work is sequenced and delivered*. Referenced, not duplicated.

| # | Risk | Impact | Mitigation |
| --- | --- | --- | --- |
| DR-1 | **Four screens have no approved design (OI-4).** Invoicing, Login, Force-password-change and Admin are the only modules where frontend and backend cannot be built from a shared visual contract, so the two can drift and be discovered late. | Rework on E8 and E11; a launch-blocking module discovered to be unbuildable at M6. | E0 runs in M0, in parallel with engineering, and E0-06 is a hard gate on those UI stories only — their backends proceed regardless. E0-01 forces net-new designs to reuse existing atoms, narrowing the drift surface. |
| DR-2 | **Analytics is sequenced last but reads from every module's schema.** Late schema churn in M3–M6 silently breaks M7 aggregates. | Rework at the end of the plan, when there is least slack. | Freeze the §6 schema at M1 (E1-02) and treat later schema changes as explicit, reviewed migrations. Write each aggregate's integration test against seeded data so a schema change fails CI immediately, not at M7. |
| DR-3 | ~~**No PDF generation library is chosen.** TECH_SPEC §3's stack table is silent on it, yet FR-BIL-03 requires storing invoices as PDFs. The obvious candidates vary hugely in footprint and licensing, which collides with constraint C1.~~ **CLOSED 2026-08-03 — QuestPDF 2026.7.2, Community licence, accepted once in Infrastructure DI. See §17.0.** | Either a blocked M6, or a heavy/licence-encumbered dependency chosen under time pressure. | ~~Decide in M0 alongside OI-7 version pinning; evaluate against the C1 footprint budget and licence terms before M6 starts.~~ Footprint was evaluated (pure-managed, no native binaries). **The licence half was closed on technical merit only — the revenue threshold has never been checked against this business, carried as N-26.** |
| DR-4 | **FR-VEN-07 (vendor documents) has no table in TECH_SPEC §6.** The requirement is Could-have, so the schema gap is currently invisible. | If FR-VEN-07 is pulled into scope late it needs a migration plus a new upload path, mid-flight. | Keep it explicitly last in E5 and treat any decision to build it as a schema-change decision, not a UI story. Otherwise drop it per PA-4. |
| DR-5 | **"Port the prototype 1:1" is a subjective acceptance criterion.** TECH_SPEC §5.1 itself warns that identical markup does not guarantee identical rendering. | Repeated review/rework loops on every screen-port story; disputes about what "done" means. | Define 1:1 concretely as: same DOM structure, same computed style values from E0-01's token reference, same status→colour maps. Per-screen visual diff sign-off (E12-08) is in the DoD for every UI story, done at the time — not batched into M8. |
| DR-6 | **Configurable master data is easy to violate quietly.** Any feature component that hard-codes a category or status list to move faster breaks FSD §3.3 without failing a test. | The business's core "change it without a deploy" promise is silently false in some screens. | E3-10 provides the single service; the DoD carries an explicit "no hard-coded lookup lists" check, enforced at code review. |
| DR-7 | **Four open FSD questions change story shape, not just detail.** Q1 (freight-only external-purchase fields), Q3 (inventory per-SKU vs product line), Q4 (who may dispatch), Q9 (invoice numbering and company details) each land inside a story already in this backlog. | Building E4-12, E7-01, E9-08 or E8-08 on a guess means rework in the module where the guess was wrong. | All four are in the M0 decision list. Each affected story names its blocking question. If an answer is late, build the minimum the prototype already shows and flag the field set as provisional. |
| DR-8 | **Cross-cutting concerns are cheap upfront and expensive retrofitted.** Permission attributes and `IAuditLogger` calls touch every mutating endpoint. | Adding them after M3–M6 means revisiting every controller and every integration test. | E1-09 and E3-01 are hard gates before feature CRUD; permission-checked and audit-logged are DoD line items, not optional polish. |
| DR-9 | **There is one VPS and no staging environment** (TECH_SPEC §7 describes exactly one box). "Demo in a prod-like environment" and "don't disturb live data" pull against each other once the business starts using the tool. | Demos on the live box risk polluting real data; demos only on laptops defeat the purpose of the early E2. | PA-3 assumes local compose is the demo target and the VPS is the deploy target. Revisit at M8: once real users are on the box, either add a second small VPS or agree a maintenance-window demo protocol. |
| DR-10 | **Permission changes take effect only at next login/refresh** — an accepted trade-off in TECH_SPEC §4.3, but a support-load risk once the owner starts managing accounts. | "I changed their role and nothing happened" tickets; the owner loses trust in the admin screens. | E11-05 revokes refresh tokens on role change; the admin UI states plainly that the user must sign in again. |
| DR-11 | **Integration tests need Docker in CI.** TECH_SPEC §4.1 specifies Testcontainers Postgres; if the CI runner cannot run containers, the integration half of the DoD quietly degrades to unit tests only. | Endpoint-level regressions ship undetected while the DoD appears satisfied. | E2-06 explicitly verifies a Testcontainers-based test runs green in CI before M3 depends on it. |
| DR-12 | **Uploaded documents are the only durable state outside Postgres.** A backup routine or restore drill that covers only `pg_dump` loses every catalog and invoice PDF while appearing to succeed. | Catastrophic, and only discovered during a real recovery. | E2-08 backs up both; E12-04's restore drill is not complete until a restored document opens. |
| DR-14 | **E0-06 was resolved by authorisation, not by a formal design review** (2026-07-27, see §12). The four net-new screens were designed and built in one pass against the E0-01 token reference, so the design and its implementation were reviewed together rather than the design being approved first. | If the owner dislikes a design decision, the cost is reworking a built screen, not a draft — which is precisely the cost the original E0-06 gate existed to avoid. | Accepted deliberately: the gate had been open since M0 and its cost was *growing*, since every milestone added surface waiting on it. Narrowed by (a) building only from E0-01 atoms, so a rejected screen is restyled and not rebuilt, (b) writing every design decision down in `docs/SCREEN_DESIGNS.md` so the owner reviews reasoning rather than pixels, and (c) holding Invoicing at design-only, so the screen with the most unresolved business input (Q9c) has the least code behind it. |
| DR-13 | **The `spec.txt`/`spec_document.xml` mirrors can silently drift stale** — they were found out of sync during this planning pass and have been regenerated, but nothing currently re-generates them automatically on the next `.docx` edit. | An entire module missed, or contradictory requirements between team members, if someone edits the `.docx` again and forgets the mirrors. | Treat the `.docx` as the single source of truth; regenerate both mirrors as a checklist step whenever it changes, or retire the mirrors entirely once nothing depends on them. |

---

## 7. Definition of Done

Applies to **every** story unless a line is explicitly not applicable (e.g. no UI in a backend-only story).

**All stories**
- [ ] Acceptance criteria in the story are demonstrably met, verified against the deployed prod-like stack (PA-3), not only locally.
- [ ] Traceability holds: the implementation matches the FR-ID(s) and TECH_SPEC section cited on the story — no scope added that neither document states.
- [ ] No new dependency added without checking it against constraint C1 (footprint) and TECH_SPEC §11 (explicit non-goals: no MediatR/AutoMapper/generic repository/NgRx/UI kit/message queue).
- [ ] Any assumption made while building is recorded as a new open item, not silently absorbed.

**Backend stories**
- [ ] Unit tests cover the Application service's behaviour, including the failure/validation paths, not just the happy path.
- [ ] At least one integration test exercises the endpoint through `WebApplicationFactory` against a Testcontainers Postgres instance (TECH_SPEC §4.1).
- [ ] The endpoint is protected by the correct `[Authorize(Policy = "…")]` permission from the TECH_SPEC §4.3 catalog, **and** a test asserts that a caller lacking that permission receives 403.
- [ ] Every create/update/delete writes through `IAuditLogger` with user, action, entity type, entity id and timestamp (FR-ADM-04, FSD Auditability NFR).
- [ ] Any status, category or service-type value is an FK to its lookup table — never an enum or free-text column (FSD §3.3).
- [ ] Schema changes ship as a reviewed EF Core migration; nothing relies on auto-migration in production (TECH_SPEC §6, §7.5).
- [ ] List endpoints support `search`, `page`, `pageSize` and the filters the FSD names for that module; filter/FK columns are indexed.
- [ ] Errors surface as RFC 7807 `ProblemDetails`; no stack traces or internal identifiers leak to the client.
- [ ] File-handling stories go through `IFileStorage` and the magic-byte/size validation; documents are served only via the authenticated download endpoint, never a public static path (TECH_SPEC §8).

**Frontend stories**
- [ ] The screen matches the approved prototype 1:1 in DOM structure and computed styling — same colours, spacing, font stack and emoji glyphs as the E0-01 token reference. For net-new screens, matches the E0-approved design instead.
- [ ] A per-screen visual diff against the prototype has been reviewed and signed off (TECH_SPEC §5.1).
- [ ] All dropdown/lookup values come from the master-data service; **no hard-coded category, status or service-type lists** (DR-6).
- [ ] Status colours come from the shared `StatusStyleService`/pipe, not per-component copies.
- [ ] The route is lazy-loaded and guarded by the matching permission; nav entries for unavailable routes are hidden (TECH_SPEC §5.3).
- [ ] The screen is usable at phone width, checked against the prototype's `mobile` reference (FSD NFR Usability, A6).
- [ ] Loading, empty and error states are handled — no indefinite spinner and no blank screen on API failure.
- [ ] The production build stays within its configured bundle budget (TECH_SPEC §5.4).

**Before a milestone is called complete**
- [ ] All Must-have (M) stories in the milestone's epics are done; any deferred S/C story is explicitly recorded as deferred, with the descope decision noted (PA-4).
- [ ] CI is green: build, unit tests, integration tests.
- [ ] The milestone's exit criterion has been demonstrated to the business owner.
- [ ] Any open item this milestone was meant to close (OI-* or FSD Q*) is answered in writing, or re-flagged with its new blocking date.

---

## 8. Requirement coverage check

Every Phase 1 FR from the FSD maps to at least one story. Nothing in this backlog exists without a source requirement or a TECH_SPEC section.

| FR group | Requirements | Covered by |
| --- | --- | --- |
| FR-CRM-01…11 | 11 | E4-01…E4-12 (E4-08 conditional on Q2) |
| FR-VEN-01…07 | 7 | E5-01…E5-07 (E5-07 needs schema addition, DR-4) |
| FR-CAT-01…08 | 8 | E6-01…E6-08, FR-CAT-08 via E9-07 |
| FR-INV-01…09 | 9 | E7-01…E7-10 |
| FR-WA-01…07 | 7 | E9-01…E9-09 |
| FR-AN-01…08 | 8 | E10-01…E10-09 |
| FR-ADM-01…05 | 5 | E11-01…E11-09, E1-07 (FR-ADM-03), E1-09 (FR-ADM-04), E3-03…E3-08 (FR-ADM-02) |
| FR-BIL-01…07 | 7 | E8-01…E8-10 |
| UC-01…UC-09 | 9 | UC-01/02 → E4; UC-03 → E5; UC-04 → E6; UC-05 → E9; UC-06/07 → E7; UC-08 → E10; UC-09 → E3 + E11 |
| FSD §8 NFRs | 9 areas | E12-01…E12-04, E12-06, plus DoD line items and TECH_SPEC §9 traceability |

Requirements deliberately **not** built in Phase 1 are listed in FSD §3.2 and TECH_SPEC §11 and are absent from this backlog by design.

---

## 9. Delivery status tracker

**Legend** — `Done` = acceptance criteria met **and** verified by an executed command. `Draft` = written but not executable in this environment. `Partial` = built, with a named residual gap. `Not started` = untouched.

**Last updated:** 2026-07-27, end of the M1 implementation pass.

### M1 pass result: Done, with three carried gaps

Verified on this machine: `dotnet build` (0 warnings, 0 errors), `dotnet test` (**75 passing** — 53 unit + 22 integration against a real Testcontainers Postgres), `NODE_OPTIONS= npx ng build` (production, within budgets, output at `client/dist/browser`), and a `docker compose --profile prod up` smoke test of the combined `caddy`+`api`+`db` stack serving the real Angular bundle, with a full login → forced password change → refresh-rotation cycle exercised over HTTP through Caddy.

### E0 — Design pass

| ID | Status | Verification / note |
| --- | --- | --- |
| E0-01 | **Done** | Tokens extracted from the approved prototype into `docs/DESIGN_TOKENS.md` and implemented as `client/src/app/shared/styles/_tokens.scss`. Spot-checked: every documented hex occurs in the prototype, and the `SVC`/`ST` maps are byte-identical to prototype lines 1233-1250. |
| E0-02…E0-05 | **Superseded — see §12** | Was "Not started, needs business input" at M1. The business owner authorised proceeding on 2026-07-27; all four designs now exist in `docs/SCREEN_DESIGNS.md`. Current status is tracked in §12.1. |
| E0-06 | **Superseded — see §12** | Was an open gate. Resolved on 2026-07-27 by the owner directing the work to proceed against the E0-01 reference rather than waiting on a separate formal design pass (DR-14). |
| E0-07 | **Done** | Closes TECH_SPEC OI-3 in `DESIGN_TOKENS.md` §11, from direct inspection of `Source/_ds/industry-…` — an unrelated steel-blue/Barlow-Condensed theme, referenced by nothing in the approved prototype, excluded from the port. |

### E1 — Foundation, Infrastructure & Auth

| ID | Status | Verification / note |
| --- | --- | --- |
| E1-01 | **Done** | Four projects + two test projects build clean. `Domain` confirmed framework-free: zero `PackageReference`, no `Microsoft.*` usings. |
| E1-02 | **Done** | One `InitialCreate` migration; 27 tables live in Postgres (26 entity + `__EFMigrationsHistory`). `Application` depends only on `IAppDbContext`; no generic repository. **Addition:** `vendor_statuses` lookup — see §9.1. |
| E1-03 | **Done** | Verified in-database: 17 permissions, 2 roles, 6 categories, 2 service types, 6 lead / 4 shipment / 4 invoice / 3 vendor statuses. Values match the prototype verbatim (`Jewellery`, `IN TRANSIT` with a space). Idempotency unit-tested. **Addition:** bootstrap Super Admin — see §9.1. |
| E1-04 | **Done** | Default `MemoryCacheService`; `Caching:Provider=Redis` verified to swap implementation with no code change, API healthy on the Redis path and Redis showing a live connection. Nothing outside the two implementations touches `IMemoryCache`/`IDistributedCache`. No cache consumers exist yet (first are E3-09, E10-10). |
| E1-05 | **Done** | Content-type + `%PDF` magic-byte + size-cap validation, plus a path-traversal guard. 11 unit tests including a spoofed-content-type case. |
| E1-06 | **Done** | `ProblemDetails` on unhandled errors; stdout structured logs that deliberately exclude headers so bearer tokens cannot leak; CORS locked to one origin; login rate limiting verified live — 9th rapid attempt returns 429. |
| E1-07 | **Done** | Verified live through Caddy: JWT carries `sub`/`email`/`name`/`role`/`permissions`/`must_change_password`; `permissions` is a real JSON array of 17. Refresh rotates and the replayed old token returns 401. Unknown email and wrong password are both 401 — no account-existence hint. |
| E1-08 | **Partial** | Flag enforcement, the change-password endpoint, flag clearing and full-scope reissue all verified live. **Gap:** the 403 path is unit-tested only — no permission-gated feature endpoint exists yet to exercise it over HTTP (a live attempt returns 404, since `/admin/users` is M2). Re-verify at E3-01/E11. |
| E1-09 | **Done** | `audit_logs` verified in Postgres with real rows for `LoginSucceeded`/`LoginFailed`/`RefreshSucceeded`/`RefreshFailed`/`PasswordChanged`, tracing the exact smoke-test sequence. |
| E1-10 | **Done** | Angular 19.2.x (CLI 19.2.27, core 19.2.25) on Node v20.12.2; standalone throughout, zero `NgModule`; production build clean. Budgets set now, pulling E12-06 forward — see §9.1 for the threshold deviation. |
| E1-11 | **Done** | `ApiService` + interceptor with 401 → `/login`; `ProblemDetails` surfaced via title/detail. Defensive claim parsing tolerates both array and scalar. |
| E1-12 | **Done** | `canActivate` guards read `permissions` from the token; claim names match the live JWT confirmed above. Server remains the authority. |
| E1-13 | **Done** (unsigned-off) | Built from the E0-01 reference under the §5 M1 fallback. **Needs E0-06.** |
| E1-14 | **Done** (unsigned-off) | Same as E1-13. **Needs E0-06.** |
| E1-15 | **Done** | Navy sidebar/topbar ported 1:1; permission-filtered nav; lazy stub routes for the six M3+ features. Nav uses `<a routerLink>` rather than the prototype's `<div onClick>` for accessibility — same visuals. |
| E1-16 | **Done** | `SVC`/`ST` defined once in `StatusStyleService`, byte-identical to the prototype. Invoice statuses are net-new and flagged for E0-06. |
| E1-17 | **Done** | `GET /api/v1/health` returns `{"status":"Healthy","checks":{"api":"Healthy","database":"Healthy"}}` with a real `CanConnectAsync`, verified both direct and proxied through Caddy. |

### E2 — Deployment & Ops Skeleton

| ID | Status | Verification / note |
| --- | --- | --- |
| E2-01 | **Done** | Multi-stage build, `aspnet:10.0-alpine` runtime, 191 MB, no `/usr/share/dotnet/sdk`, runs as non-root uid 1654. |
| E2-02 | **Done** | Verified from a clean state: plain `docker compose up` creates **only** `api` + `db`; `redis` requires `--profile redis`. `uploads`/`pgdata` volumes present. |
| E2-03 | **Done** | Caddy serves the real Angular production bundle (verified `<title>` and hashed `main-*.js` from `client/dist/browser`), not a placeholder root. No Node runtime in the serving image. |
| E2-04 | **Done** (HTTP only) | SPA deep-link fallback returns 200 for `/login` and for nested routes; `encode` confirmed via `Content-Encoding: gzip`; `/api/*` proxies correctly. **Automatic HTTPS is unverifiable here** — no public DNS name for a certificate; verified over `:80`. |
| E2-05 | **Not started** | **Out of scope** — needs real VPS access, domain and credentials. |
| E2-06 | **Draft** | Written, never executed. No CI runner or registry in this environment. |
| E2-07 | **Draft** | Written, never executed. Uses an EF migrations bundle because the runtime image has no SDK — the least-validated choice here. |
| E2-08 | **Draft** | Written, never executed. Covers both `pg_dump` and the `uploads` volume per DR-12. No off-box target exists here. |
| E2-09 | **Done** | `.env.example` is placeholders only; `.env` confirmed git-ignored. No secret in the repo or in an image layer. |
| E2-10 | **Partial** | Limits confirmed applied via `docker inspect` (api 400 MiB, db 1200 MiB); live idle usage ~131 MB across all three containers, inside the §7.3 budget. **Cannot verify against the real 4 vCPU/8 GB box, and idle usage with no representative data is weak evidence** — recheck under E12-02 volumes. |

### E3-E12

All **Not started** — M2 onward, as sequenced in §5.

### 9.1 Deviations and additions from this pass

Recorded per the §7 DoD requirement that assumptions be surfaced, not absorbed.

| # | Deviation | Rationale |
| --- | --- | --- |
| D-1 | **Bootstrap Super Admin seeded** with a generated password logged once. | M1's exit criterion is "a user logs in", but account creation (E11-01) is M2 — without this M1 is unverifiable by its own standard. Not in the written E1-03. No password is committed; config-supplied values are honoured. |
| D-2 | **`vendor_statuses` lookup table added** (schema is 26 tables, not TECH_SPEC §6's 25). | §6 lists `vendors.status` as a plain column, contradicting both its own FK rule and FSD §3.3's configurable-master-data requirement. **TECH_SPEC §6 should be corrected**; this is a spec inconsistency, not a build decision. |
| D-3 | **Bundle budgets are 150 kB warn / 250 kB error per chunk**, not §5.4's suggested 120/200. | Angular's own framework chunk is ~131 kB with zero app code, so 120 kB warns on day one for a reason the budget is not meant to catch. Every real feature chunk is under 9 kB, so 150 kB still catches a UI-kit/chart-library regression. §5.4 words these as examples. |
| D-4 | **`mem_limit` used instead of `deploy.resources.limits`.** | `deploy.resources` is a Swarm construct and is not applied by plain `docker compose up`, which is exactly what E2-10 asks to verify. Confirmed applied via `docker inspect`. |
| D-5 | **Docker build context is the repo root**, not `./src/SourcingOps.Api`. | A four-project solution needs sibling project folders inside the build context. |
| D-6 | **`caddy` sits behind a `prod` profile.** | Lets one compose file serve both environments per §7's "single definition reused with overrides": local dev runs Angular via `ng serve` and never starts Caddy. |
| D-7 | **Extra packages beyond §3's named stack**: `EFCore.NamingConventions`, some `Microsoft.Extensions.*`/`Microsoft.IdentityModel.*`. | Needed for snake_case mapping, DI/config in Application, and JWT issuance. All small and justified against C1; none is on §11's prohibited list. |
| D-8 | **Sidebar nav uses `<a routerLink>`** rather than the prototype's `<div onClick>`. | Keyboard and screen-reader access; computed styling unchanged. Supports E12-01. |
| D-9 | **Two compose wiring bugs found and fixed during the combined-stack smoke test.** | (1) `caddy` never received `CADDY_DOMAIN`, so `{$CADDY_DOMAIN}` expanded to empty and Caddy read the leading `{` as a global options block, failing with "unrecognized global option: encode". (2) `docker-compose.override.yml` hard-coded `Caching__Provider=InMemory`, and being auto-merged on every local `up` it beat the base file's variable, making the Redis switch untestable locally — contradicting E2-02 and constraint C2. Both were invisible to component-level testing and only surfaced when the stack ran together. |

### 9.2 Open items this pass raised or could not close

| # | Item |
| --- | --- |
| N-1 | **The Angular pin and staying on patched Angular are currently mutually exclusive.** `npm audit` reports moderate/high advisories in `@angular/core`/`@angular/compiler` <=19.2.25, fixed only in 22.x, which Node v20.12.2 cannot run. Not bumped, per the OI-7 pin. **TECH_SPEC OI-7 should be reopened** to record this consequence: it is a Node-upgrade decision, not an Angular one. Low immediate risk (internal-only tool behind auth), but it should not be discovered at launch. |
| N-2 | **E1-08's 403 path is not yet verifiable end-to-end** — no permission-gated endpoint exists. Carry into E3-01. |
| N-3 | **Automatic HTTPS is untested** — no public DNS name locally. First real exercise is E2-05. |
| N-4 | **`redis` was never exercised as a real cache**, only as a reachable provider, because M1 has no cache consumers. First genuine test is E3-09. |
| N-5 | **Changing `DB_PASSWORD` after first `docker compose up` silently breaks auth**, because Postgres only applies `POSTGRES_PASSWORD` when initialising an empty data directory; an existing `pgdata` volume keeps the old credential. Cost real debugging time this pass. Worth a line in the E12-09 operating guide. |
| N-6 | E0-06 remains unsigned, and E1-13/E1-14 shipped under the §5 fallback. Unchanged gate for E8-09/E8-10 and E11-07/E11-08. |

---

## 10. M2 close-out — Master data & RBAC live

**Last updated:** 2026-07-27, end of the M2 implementation pass. Same evidentiary standard as §9: `Done` means an executed command backs it.

**Exit criterion met.** A Super Admin creates an Associate account with the temp password shown once, adds/renames/retires a category, and a feature endpoint returns 403 for a missing permission — all verified over real HTTP through Caddy, not only in tests.

Verified on this machine: `dotnet build` (0 warnings), `dotnet test` (**151 passing** — 99 unit + 52 integration on Testcontainers Postgres), plus a full `docker compose --profile prod` smoke test of the combined stack.

### E3 — Master Data & RBAC Scaffold

| ID | Status | Verification |
| --- | --- | --- |
| E3-01 | **Done** | `PermissionRequirement` + handler backing `[Authorize(Policy=…)]`. Verified live: Associate read 200, Associate write 403, Associate `/admin/users` 403, SuperAdmin write 201. **Closes N-2.** |
| E3-02 | **Done** | Additive union proven end-to-end — a live Associate token carries exactly 15 permissions (17-code catalog minus the two `Admin.*`). |
| E3-03 | **Done** | Categories create/rename/reorder/retire via `MasterDataController`. |
| E3-04 | **Done** | `CIF` / `FREIGHT_ONLY` as lookup rows with editable labels; verified live as FK-backed, never enum or free text. |
| E3-05 | **Done** | 6 lead statuses seeded and configurable. |
| E3-06 | **Done** | 4 shipment statuses; `IN TRANSIT` confirmed live with a space, matching the prototype's ported colour map key. |
| E3-07 | **Done** | 4 invoice statuses. |
| E3-08 | **Done** | Verified live: `DELETE` on a **referenced** row → `409` + `application/problem+json`, detail "…Retire it instead of deleting."; unreferenced → `204`. Retire/restore flip `IsActive` without touching any FK. |
| E3-09 | **Done** | First real `ICacheService` consumer. Verified a retire is visible in the immediately following cache-backed read (no stale window), under **both** in-memory and Redis. |
| E3-10 | **Done** | `MasterDataService` — one HTTP call shared by all consumers; 10/10 Karma tests. Active-vs-retired split enforced by separate accessors. |

### E11-01…E11-05 — Account management backend

| ID | Status | Verification |
| --- | --- | --- |
| E11-01 | **Done** | Temp password returned once; verified live it is **never** echoed by `GET /admin/users`. |
| E11-02 | **Done** | Reset issues a new temp password and revokes refresh tokens. |
| E11-03 | **Done** | Soft delete per the settled OI-2 reasoning. Login and refresh both 401 afterwards. **But see N-7** — already-issued access tokens survive. |
| E11-04 | **Done** | List/search including inactive, paged. |
| E11-05 | **Done** | Role reassignment revokes refresh tokens (DR-10). |

### 10.1 Deviations and additions from this pass

| # | Deviation | Rationale |
| --- | --- | --- |
| D-10 | **`POST /admin/users/{id}/restore` added** | Not in E11-03. Without it an accidental deactivation is unrecoverable through the API. |
| D-11 | **Last-active-SuperAdmin guard added** — blocks deactivating *or* role-reassigning-away-from the final active Super Admin | Real lockout safety: no hard delete, `restore` itself needs `Admin.ManageUsers`, and the bootstrap seed is idempotent so it will not re-create an existing admin. A zero-SuperAdmin state would be permanently unadministrable. Verified live on both paths (400, account untouched). **Not described in TECH_SPEC §4.4 — worth adding there.** |
| D-12 | **Lookup `Code` is immutable after creation**; `PUT` edits label/name only | The frontend's `StatusStyleService` keys its ported colour maps by `Code`, so a mutable `Code` would silently break status colours app-wide. Renaming still works via the display field. |
| D-13 | **Login rate limit made configurable** (production default unchanged) | It was hard-coded, and under `WebApplicationFactory` all requests share one synthetic IP, so the suite self-tripped the real 10/min cap. Would have failed **intermittently in CI** (E2-06) by concurrency. Fixed at the root, not with retries; a test still asserts the real default so a disabled limiter cannot ship unnoticed. |
| D-14 | **A failing test's premise was fixed, not its assertion** | `Deactivate_TheLastActiveSuperAdmin` asserted a *global* DB condition while sharing one Postgres with siblings that create users; a sibling left a second active admin behind. The product guard was never broken (confirmed by unit test). Had the test ever passed, it would have deactivated the shared fixture admin and poisoned every later test in the class. |

### 10.2 Open items after M2

| # | Item |
| --- | --- |
| **N-7** | **CLOSED — commit `dd0f8cf`**, extended to the credential-change paths in `5ffbfef`. Option (c) was taken, as recommended: an `ICacheService` deny-list keyed by user id with a TTL equal to the token lifetime, checked in `TokenRevocationMiddleware`. No per-request DB hit, so TECH_SPEC §4.3's deliberate design holds. The original finding is kept below as the rationale record. <br><br> *Original finding:* **A deactivated user keeps full API access for up to 8 hours.** Verified live: after `DELETE /admin/users/{id}`, login and refresh both correctly return 401, but the user's **already-issued access token still returns 200**. The access token lifetime is 8h (TECH_SPEC §4.2), so that is the exposure window. E11-03's own wording is "blocks login and **revokes sessions**" — refresh tokens *are* revoked, so the gap is specifically the bearer token. TECH_SPEC §4.3 knowingly accepts this for *permission* changes, but deactivation is a different risk class: it is the "remove access now" operation, used when someone leaves or is compromised. Genuinely low risk at Q7's stated 2–5 internal staff, but it should be an **explicit accepted decision, not an unnoticed gap**. Three options: (a) shorten the access-token lifetime and lean on refresh rotation — simplest; (b) check `IsActive` per request — a DB hit §4.3 deliberately avoided; (c) a revocation deny-list in `ICacheService` keyed by user id with TTL equal to the token lifetime — no DB hit, bounded size, and the cache now has a proven consumer. **Recommend (c), or (a) if simplicity wins.** |
| N-8 | **CLOSED — commit `ea7c440`.** Seeded default rows are now retire-only: they can be deactivated but not deleted, so an early mistake is recoverable and the idempotent seeder is never asked to restore something it cannot. Non-seeded rows are unaffected and still follow E3-08 exactly. The original finding is kept below as the rationale record. <br><br> *Original finding:* **Seeded default lookups are deletable while unreferenced.** Observed during verification: `DELETE` removed the seeded `ACTIVE` vendor status because no vendor referenced it yet. This is *correct* per E3-08 and consistent with FSD §3.3 configurability, but it means an early mistake can remove a default the seeder will not restore (seeding is idempotent and only fills gaps on an empty set). Consider whether seeded defaults should be retire-only. |
| N-1 | **Unchanged.** The Angular 19 pin and staying on patched Angular remain mutually exclusive until Node is upgraded. TECH_SPEC OI-7 reopened. |
| N-3, N-4 | **N-3 unchanged** (automatic HTTPS still untested — no public DNS name; first real exercise is E2-05). **N-4 now closed** — Redis was exercised as a real cache under E3-09, not merely as a reachable provider. |
| N-5 | **Unchanged and re-encountered.** Changing `DB_PASSWORD` against an existing `pgdata` volume still breaks auth confusingly. Belongs in the E12-09 operating guide. |
| N-6 | **Unchanged.** E0-06 still unsigned; E1-13/E1-14 shipped under the §5 fallback. Still gates E8-09/E8-10 and E11-07/E11-08. |

### 10.3 Cross-track integration check (the seam M1 proved matters)

M1's two real defects were both wiring between components that each passed their own tests, so E3-10 was diffed against a **live** API response rather than trusted:

- All six collections present, no extras, **keys match the TypeScript interfaces exactly**, `sortOrder` int / `isActive` bool / `id` string.
- The deliberate asymmetry holds on both sides — `categories` returns `name`; every other collection returns `code` + `label`.
- `includeRetired` is genuinely honoured, **not silently ignored as an unknown query param**: a retired row is absent at `false` and present with `isActive: false` at `true`. This is exactly what E3-10's design depends on, since it loads with `true` so `*ById` resolves retired rows while `*Options` filters them out.
- Seeded values still match the prototype verbatim (`IN TRANSIT` with a space; `CIF` / `FREIGHT_ONLY` with the prototype's labels), so the ported colour maps resolve.

### 10.4 Next

**M3 — CRM vertical slice (E4).** Now unblocked: FSD Q1 is answered, and Q2 confirmed E4-08 in scope needing no migration.

**Do first, before any E4 story:** the single reviewed **pre-M3 migration** carrying the three deferred schema corrections — the six `external_*` columns on `customers` (Q1), `shipments.reference`, and `company_settings` (shape only; Q9c values still outstanding). Deliberately held out of M2 so the M1 freeze (DR-2) breaks once, with review.

---

## 11. Pre-M3 hardening pass and M3 close-out — CRM vertical slice

**Last updated:** 2026-07-27. Same evidentiary standard as §9 and §10: `Done` means an executed command backs it.

**Verified on this machine, this pass:** `dotnet test` — **246 passing, 0 failed** (168 unit + 78 integration on Testcontainers Postgres), up from the 151 at M2 close. `ng test` (Karma, ChromeHeadless) — **38 passing, 0 failed**, up from 10. Migrations are exercised for real by the integration suite, so `AddCustomerListFilterIndexes` is known to apply, not assumed to.

**Not yet done: live HTTP verification.** M1 and M2 were both closed against real requests through Caddy, and two of M1's defects were invisible to tests. M3 has **not** had that treatment — it is closed on test evidence only. See §11.4.

### 11.1 Pre-M3 hardening pass (committed before any E4 story)

| Commit | Change |
| --- | --- |
| `3321ad2` | Docs: corrected a false claim about `customers.external_purchase_reference`. |
| `e8c9444` | The single reviewed pre-M3 migration — the three deferred schema corrections, exactly as §10.4 required. |
| `dd0f8cf` | **N-7 closed.** Option (c): an `ICacheService` deny-list keyed by user id, TTL equal to the token lifetime, checked in `TokenRevocationMiddleware`. No per-request DB hit, so TECH_SPEC §4.3's deliberate design survives. |
| `ea7c440` | **N-8 closed.** Seeded default lookup rows are retire-only. Non-seeded rows still follow E3-08 unchanged. |
| `1481eb4` | Tests for the above: 171 passing, no regressions. |
| `5ffbfef` | **N-7 extended to the two credential-change paths.** `dd0f8cf` covered deactivation only, but an admin-forced reset and a self-service change carry the same "cut existing access now" intent. Also fixed a genuine intermittent: `/auth/login` is `[AllowAnonymous]`, which skips *authorization* but not *authentication*, so a stale `Authorization` header on a shared `HttpClient` was rejected by `TokenRevocationMiddleware` with 401 "Token revoked" before reaching the controller — reproducing only when the two actions landed in different wall-clock seconds. |

### 11.2 E4 — Customer Intake & CRM

| ID | Status | Verification |
| --- | --- | --- |
| E4-01 | **Done** | `CustomersController` — full CRUD, every endpoint `[Authorize]`-gated on `Customers.View`/`.Edit`, mutations audit-logged. |
| E4-02 | **Done** | Service type FK required on save. |
| E4-03 | **Done** | Category interest many-to-many via `categoryIds`. |
| E4-04 | **Done** | `PhoneNumberNormalizer` normalises at the boundary, so the stored value builds a `wa.me` link with no further transformation — the invariant holds for E9, which does not exist yet. Default country code is configurable (`Customers:DefaultCountryPhoneCode`) rather than a hard-coded `+91`. |
| E4-05 | **Done** | Status from the configurable lookup; each change lands on the timeline with user and timestamp. |
| E4-06 | **Done** | List with `search`/`page`/`pageSize` and all five filters. Needed a migration: `ServiceTypeId`/`StatusId`/`OwnerUserId` already had FK-derived indexes, but `Region` and `Tags` did not. `Tags` gets **GIN** because the filter is array containment, which a btree index would not serve. |
| E4-07 | **Done** | `POST /{id}/interactions` plus the merged chronological timeline feed. |
| E4-08 | **Done** | `GET /customers/follow-ups/due` returns interactions earliest-due first. The reminder screen and its route now exist, so FSD Q2's "active reminders, not a passive log" is actually satisfied rather than merely supported. Overdue vs. due-today is computed on **local calendar days, not raw milliseconds**, so a follow-up due earlier today reads "Due today" rather than "Overdue by 0 days". Reachable from a button on the customers list, not only by URL. Route is declared **before** `customers/:id`, or the `:id` route would swallow it and try to load a customer with the id "follow-ups". |
| E4-09 | **Done** | `PUT /{id}/owner`; change is audit-logged and appears on the timeline. |
| E4-10 | **Done** | Duplicate phone returns 409 with the existing customer in the ProblemDetails body, requiring explicit confirmation. Deliberately **not** a hard block: a shared family or office line is real, so the collision is made visible rather than the second customer made unrepresentable. |
| E4-11 | **Done** | `?tag=` filter, GIN index, and `Tags` accepted on create/update with trim / drop-empty / case-insensitive de-dup normalisation. The client interface was silently missing `tags` on the create request even though the API accepted it; added. Tag editor on intake **mirrors the backend normalisation client-side**, so the chips a user sees are exactly what gets stored rather than a set the server will quietly alter on save. Tags render on the list (click-to-filter) and on the detail profile. |
| E4-12 | **Done** | Minimum the prototype shows, per FSD Q1 as answered. |
| E4-13 | **Done** | Intake screen; all dropdowns resolve through `MasterDataService`, not local constants. |
| E4-14 | **Done** | Customers list screen against the E4-06 endpoint. |
| E4-15 | **Done** | Customer detail screen — profile, timeline, note entry. WhatsApp dispatch launch point is E9. |

Committed as `3acdbac` (backend) and `ed04a76` (frontend), split so the migration and API surface are reviewable independently of the screens. E4-08 and E4-11's UI followed in a third commit once the two gaps were closed.

**E4 is now complete — all 15 stories.** Frontend suite 38 → **57 passing**, `ng build` clean.

One open interaction question, not a blocker: tags are free text rather than master data, so the list screen offers **both** a text filter and click-to-filter chips. If only one is wanted, say which — the other is a small deletion.

### 11.3 Open items after M3

| # | Item |
| --- | --- |
| N-9 | **M3 has no live HTTP verification.** M1 and M2 were both closed against real requests through Caddy, and M1's two real defects were wiring between components that each passed their own tests. M3 breaks that precedent: it is closed on test evidence alone. The seam most worth exercising is the same one §10.3 flagged — the API response diffed against the TypeScript interfaces, since the CRM DTOs are far wider than the master-data ones. |
| N-1, N-3, N-5, N-6 | **All unchanged.** N-6 in particular: E0-06 is still unsigned and now gates more surface than before. |

### 11.4 Decisions required from the business owner — none of these have been asked yet

**These are drafted asks, not sent ones.** Nothing below has been put to the owner; no sign-off has been obtained or implied. Each needs a human to actually make the request.

| # | Ask | Why it matters now |
| --- | --- | --- |
| ~~**E0-06**~~ | ~~Review and sign off the four net-new screen designs.~~ **RESOLVED 2026-07-27 — see §12.** The owner directed the work to proceed against the E0-01 reference rather than wait on a separate formal design pass. It was asked and answered; this row is kept for the record, not as an outstanding ask. | The gate no longer blocks E1-13/E1-14 or E11-07/E11-08. **E8-09/E8-10 are still blocked — but on the missing E8 backend and Q9c, not on design (§12.3).** The residual risk of resolving by authorisation rather than review is recorded as DR-14. |
| **FSD Q9c** | The actual billing block values: legal entity name, GSTIN, registered address, bank details. | Real data, not a decision. `company_settings` is built and deliberately **empty**, with single-row-ness enforced by a CHECK constraint rather than a seeded row of nulls, so "not configured" stays representable. **No invoice can be issued until these values exist — this gates M6.** |
| **FSD Q6** | Expected data volumes: customers, vendors and catalogs per month. | **Never actually asked** — it was omitted from the batch that produced the Q1–Q9a answers, so it is unanswered rather than pending. Sets the representative volumes E12-02's performance pass tests against, and gates the E2-10 memory-budget recheck (M1 measured only idle usage, which is weak evidence). |
| **FSD Q8** | A sample of the legacy data to be migrated — what it contains, roughly how much. | Confirmed in scope; E12-07 cannot be estimated without it. **Ask this together with Q6:** the legacy data *is* the initial data volume, so one request answers both. Do not request them separately. |
| **Deployment track** | Provision the VPS now, or accept local `docker compose` verification through M8? | Not an engineering call. M3 closed without live HTTP verification (N-9) partly because there is no persistent environment to verify against. The longer this runs, the more milestones accumulate that were only ever proven locally — and N-3 (automatic HTTPS, untested for want of a public DNS name) cannot be closed at all until a real environment exists. |

---

## 12. E0 design pass — the four net-new screens

**Last updated:** 2026-07-27, opening this pass. Same evidentiary standard as §9–§11: `Done` means an executed command backs it.

### 12.1 What the business owner authorised, and what it changes

On **2026-07-27** the business owner directed that the E0 design pass **proceed now** rather than continue waiting on a separate formal design review. Recorded precisely, because it changes a hard gate:

- **E0-06 is resolved by authorisation, not by review.** The owner's instruction to proceed *is* the disposition of the gate. The four designs (E0-02…E0-05) were produced and implemented in the same pass, using the **E0-01 token/atom reference as the consistency baseline** — which is what E0-01 was extracted for. See **DR-14** for the risk this accepts and how it is narrowed.
- **These four screens are genuinely net-new, not ports.** Re-confirmed by direct grep of the approved prototype (`Source/Sourcing Ops Platform.dc.html`) before any design work: **zero** occurrences of `login`, `admin` or `password`. The prototype covers only Dashboard, Customers, Vendors, Catalogs, Inventory and Shipments. No design was lost or overlooked — there was never one to port.
- **The design artifact is `docs/SCREEN_DESIGNS.md`**, and it is the thing to review. It records each screen's composition *and the reasoning behind every non-obvious decision*, because with the gate resolved by authorisation the owner is reviewing decisions rather than approving mockups.
- **The E0-01 constraint held.** No new colour, spacing step, font, radius or shadow was introduced. Four structures the atom set lacked (`.page-header`/`.page-title`, `.tabs`/`.tab`, `.secret-value`, `.toolbar`) were added **to `_atoms.scss` as shared atoms built from existing tokens**, not invented per screen.

**What this does *not* resolve.** E0-06 no longer blocks E1-13/E1-14 or E11-07/E11-08. It also no longer blocks E8-09/E8-10 — but **those two remain open for an entirely different reason**, stated plainly in §12.3: there is no invoicing backend at all.

### 12.2 Story status

| ID | Status | Verification / note |
| --- | --- | --- |
| E0-02 | **Done** | Login design in `SCREEN_DESIGNS.md`; implemented. |
| E0-03 | **Done** | Force-password-change design in `SCREEN_DESIGNS.md`; implemented. |
| E0-04 | **Done** | User management + master-data configuration designs; both implemented against the existing M2 controllers. |
| E0-05 | **Done (design only — by design)** | Invoice list and generate/detail designs, implemented on **mocked data**. See §12.3. |
| E0-06 | **Resolved by owner authorisation** (2026-07-27) | Not a review sign-off. See §12.1 and DR-14. |
| E1-13 | **Done** | Restyled to the E0-02 design. The §5 M1-fallback caveat and its "(unsigned-off)" marker are retired. |
| E1-14 | **Done** | Restyled to the E0-03 design. Same. |
| E11-07 | **Done** | User management screen, incl. the show-temp-password-once interaction, against the existing `AdminUsersController`. |
| E11-08 | **Done** | Master-data configuration screen against the existing `MasterDataController`. |
| E8-09 | **Open — blocked** | **Not blocked on design any more; blocked on the backend.** See §12.3. |
| E8-10 | **Open — blocked** | Same. |

### 12.3 Invoicing is design-only, deliberately — E8-09/E8-10 stay open

This is the single most important thing not to misread in this pass.

**What exists:** two invoicing screens, matching the token system, rendering **realistic mocked data held in the component**, each carrying a visible in-app notice that it is a design preview on sample data.

**What does not exist:** any invoicing backend whatsoever. Verified by direct inspection this pass — the API has exactly five controllers (`AdminUsers`, `Auth`, `Customers`, `Health`, `MasterData`). There is **no `InvoicesController`, no invoice entity, no invoice migration**; epic **E8 has not been started**. The only invoicing artefacts in the system are the `invoice_statuses` lookup rows (E3-07) and the empty `company_settings` table.

**Therefore E8-09 and E8-10 remain open and blocked on:**

1. **The full E8 backend epic** (E8-01…E8-07) — nothing to wire a screen to.
2. **FSD Q9c**, still unanswered — the legal entity name, GSTIN, registered address and bank details. `company_settings` is deliberately empty, with single-row-ness enforced by a CHECK constraint rather than a seeded row of nulls, so "not configured" stays representable. **No invoice can be issued until these values exist.**
3. **DR-3**, still open — no PDF generation library has been chosen (FR-BIL-03 requires storing invoices as PDFs, and TECH_SPEC §3's stack table names none).

Building a live Invoicing UI this pass would have produced a screen with no real data behind it: the appearance of progress and none of the substance. The design deliberately makes the Q9c gap *visible* — the invoice "From" block renders an explicit "Company billing details not configured" empty state rather than inventing a plausible GSTIN, so the screen itself is now the clearest way to put Q9c to the owner.

### 12.4 Verified on this machine, this pass

| Check | Result |
| --- | --- |
| `dotnet test` | **246 passing, 0 failed** (168 unit + 78 integration on Testcontainers Postgres) — identical to the M3 close. This pass touched no backend code, and the number confirms it. |
| `NODE_OPTIONS= npx ng test` | **136 passing, 0 failed**, up from 57 at the M3 close (+79). |
| `NODE_OPTIONS= npx ng build` | Succeeds. All four new screens confirmed as their own lazy chunks: `admin-users-component` 17.46 kB, `invoice-detail-component` 16.95 kB, `admin-master-data-component` 13.71 kB, `invoices-component` 9.64 kB. **One budget warning — see D-15/N-10.** |

### 12.5 Deviations and additions from this pass

| # | Deviation | Rationale |
| --- | --- | --- |
| D-15 | **The initial bundle crossed its 300 kB warning budget: 288.38 kB → 303.01 kB (+14.63 kB, 3.01 kB over).** | **Measured, not assumed** — the pre-pass baseline was rebuilt from `HEAD` to confirm the breach is caused by this pass and was not pre-existing. Cause is *not* a leaked heavy dependency: the four new components import only `DatePipe`, `LowerCasePipe` and `RouterLink`, and all four are correctly lazy-chunked. The growth is shared-code hoisting — four more lazy routes pull more common code into the shared initial chunk. No new npm dependency was added. Still far below the 500 kB **error** threshold. Not silently accepted — carried as **N-10**. |
| D-16 | **Four shared atoms added to `_atoms.scss`** (`.page-header`/`.page-title`, `.tabs`/`.tab`, `.secret-value`, `.toolbar`, plus `.row-inactive`). | Built from existing tokens only — no new colour, spacing step, font, radius or shadow. Added as *shared* atoms rather than per-screen inventions, which is the whole point of E0-01. `.page-title` duplicates the value each ported screen already had locally; component-scoped copies win on specificity, so rendering is unchanged. |
| D-17 | **Login required no changes at all.** | The M1-fallback screens were recorded as "functional-but-unstyled". That was inaccurate: both already consumed the E0-01 tokens via a shared `_auth-page.scss`. Login already met the E0-02 design in full, including the generic-error rule — verified against `AuthService.LoginAsync`, which returns the identical message for unknown email, wrong password and inactive account. Only the force-change screen needed work. |
| D-18 | **The force-change password hint states the real policy, which is weaker than expected.** | `AuthService.ChangePasswordAsync` enforces **only** `length >= 8` — no complexity or character-class rule exists. The hint says exactly that rather than a plausible-sounding stronger policy. **Worth a business decision:** if the owner expects complexity rules, that is a backend change (E12-03 hardening), not a UI one. |

### 12.6 Open items after the E0 pass

| # | Item |
| --- | --- |
| **N-10** | **Initial bundle is 3.01 kB over its 300 kB warning budget** (D-15). Not a build failure and nowhere near the 500 kB error threshold, but the §7 DoD says the production build stays within budget, so this is a real breach and is recorded rather than absorbed. Three options, all cheap: (a) raise the initial warning budget with a written justification, exactly as D-3 did for the per-chunk budget — defensible, since ~253 kB of the 303 kB is the Angular framework itself and the budget was set when the app had six routes rather than twelve; (b) investigate the shared-chunk hoisting for a genuine win; (c) leave it and let E12-06 handle it. **Recommend (a)**, since the budget exists to catch a UI-kit/chart-library regression and it is still doing that job — but this should be an explicit decision, not a warning everyone learns to ignore. |
| **N-11** | **This pass has no live HTTP verification**, the same gap M3 carried as N-9. The admin screens in particular are wired to real M2 endpoints and have only been exercised against mocked HTTP in Karma. The seam most worth checking is the one §10.3 flagged and M1 proved twice: the live API response diffed against the TypeScript interfaces. Two known asymmetries make this more than routine — `AdminUserDto.roles` returns role **names** while assignment takes role **ids**, and `categories` uses `name` where every other collection uses `code`+`label`. Both are handled in code; neither has been confirmed against a running API. |
| N-1, N-3, N-5 | **Unchanged.** |
| N-6 | **CLOSED.** E0-06 is resolved (§12.1) and E1-13/E1-14 are no longer "unsigned-off". The design gate no longer blocks any UI story. E8-09/E8-10 remain blocked, but on the missing E8 backend and FSD Q9c — a different cause entirely (§12.3). |
| N-9 | **Unchanged and now compounding** — M3 still has no live HTTP verification, and this pass adds more unverified surface on top. See N-11. |

### 12.7 Next

**Still open, and still needing the business owner rather than an engineer:**

- **FSD Q9c** — legal entity name, GSTIN, registered address, bank details. Now has a screen attached to it: the invoice detail's "From" block renders the not-configured empty state, which is the concrete way to ask. **Gates M6 entirely.**
- **FSD Q6 + Q8** — expected data volumes and a legacy-data sample. Ask together; the legacy data *is* the initial volume.
- **Deployment track** — provision the VPS, or accept local `docker compose` verification through M8? N-11 and N-9 both trace back to there being no persistent environment to verify against.
- **N-10** — the bundle budget decision above.
- **D-18** — whether an 8-character minimum with no complexity rule is the intended password policy.

**Engineering-side, whenever M6 is picked up:** E8 is untouched. E8-01…E8-07 (entity, migration, controller, PDF, lifecycle) all remain to be built before E8-09/E8-10 can wire the now-designed screens to anything real. **DR-3** (no PDF library chosen) is still open and should be settled before M6 starts, not during it.

---

## 13. M4 pass — Sourcing side: vendors, catalogs, dispatch

**Last updated:** 2026-07-29, backend complete and live-verified; screens partially landed. Same evidentiary standard as §9–§12: `Done` means an executed command backs it.

**This pass breaks the N-9/N-11 pattern deliberately.** M3 and the E0 pass were both closed on test evidence alone. The M4 **backend** was verified live over HTTP against the `docker-compose` `api`+`db` stack on a **clean volume**, so the migration is known to apply from scratch rather than assumed to. What has *not* had that treatment is called out explicitly in §13.5 rather than left implicit.

### 13.1 Story status

| ID | Status | Verification |
| --- | --- | --- |
| E5-01 | **Done** | `VendorsController` full CRUD, `Vendors.View`/`.Edit` gated, mutations audit-logged. Vendor created live with every field persisting. |
| E5-02 | **Done** | `vendor_categories` many-to-many via `categoryIds`; verified live returning `Jewellery`. |
| E5-03 | **Done** | FK to `vendor_statuses`, never an enum; `ACTIVE` resolved live as `{id, code, label}`. |
| E5-04 | **Done** | List with `search`/`page`/`pageSize` + `categoryId`/`region`/`statusId`. |
| E5-05 | **Done** | Vendor detail embeds `catalogSections[]` with their `documents[]` — one call, no second round trip. |
| E5-06 | **Done** | MOQ, lead time, **payment terms** and reliability rating. Needed a schema addition — see D-19. |
| E5-07 | **Not started — held for a scope decision** | **Not a silent drop.** See §13.5; this is the one genuinely open question from this pass. |
| E5-08 | **Done** | Vendors list screen ported; category/region/status filters wired to E5-04. |
| E5-09 | **Done** | Vendor detail screen ported: profile, catalog sections, commercial metadata, edit dialog. |
| E6-01 | **Done** | Catalog section CRUD under a vendor. |
| E6-02 | **Done** | Upload via `IFileStorage` recording filename, size, uploader, upload date. Verified live: 201. |
| E6-03 | **Done** | Verified live — uploading v2 set `isLatest=true` and demoted v1 to `false`; v1 remains listable and downloadable. |
| E6-04 | **Done** | Verified live: authenticated download 200 `application/pdf`; **anonymous 401**; static-path probe **404**. Never a public path (TECH_SPEC §8). |
| E6-05 | **Done** | Cross-vendor browse/search by `search`/`categoryId`/`vendorId`/`tag`, paged. |
| E6-06 | **Done** | Reuses the E1-05 validator. Verified live against a **real spoofing attempt** — a file declaring `application/pdf` whose bytes were `GIF89a` was rejected `400` with `ProblemDetails`. Extension checking alone would have passed it. |
| E6-07 | **Done** | Tags normalised exactly as `CustomerService` does. Verified live: `['NEW ARRIVALS','new arrivals',' ']` collapsed to `['NEW ARRIVALS']`. |
| E6-08 | **Done** | Catalogs screen + upload dialog ported. Cover images — see D-21. |
| E9-01 | **Done** | `WhatsAppDeepLinkSender` behind `IDispatchMessageSender`. Verified live: a stored `+91…` number produced `https://wa.me/91…?text=…`, digits only, correctly URL-encoded. No Business API dependency (FSD A2). |
| E9-02 | **Done** | `POST /dispatch-log`, gated on `Dispatch.Send`, audit-logged. Staff user comes from the token — the request DTO has **no** staff field, so a caller cannot attribute a dispatch to someone else. |
| E9-03 | **Not started** | Frontend. In flight at the time of writing — see §13.6. |
| E9-04 | **Not started** | Frontend. Same. |
| E9-05 | **Not started** | Frontend. Same. |
| E9-06 | **Done** | `DispatchOptions` bound from config, `{CustomerName}`/`{CatalogName}` placeholders, exposed via `GET /dispatch-log/compose` so the text stays user-editable. Verified live: both placeholders resolved. |
| E9-07 | **Done (backend)** | `GET /catalog-documents/{id}/dispatches`, newest first. Chosen as an additive sub-resource rather than widening `CatalogDocumentDto`, which a concurrent frontend track was consuming. UI half is outstanding. |
| E9-08 | **Done — confirmed, not assumed** | FSD Q4 **is answered** (TECH_SPEC OI-8): any staff may dispatch. Verified in seed code **and live** — an Associate-only token carries exactly 15 permissions *including* `Dispatch.Send`. See N-12 for the regression trap this exposed. |
| E9-09 | **Done** | `IDispatchMessageSender` is the sole seam; `DispatchService` never builds a URL itself, so a Business-API sender can replace the deep-link builder without touching the dispatch log or the calling screens. |

### 13.2 E4-07 correction

**E4-07 was closed slightly overstated.** Its acceptance criteria say the timeline returns "interactions, status changes, **catalog dispatches** and invoices in one chronological feed", and it was marked **Done** in §11.2 — but dispatches could not exist until this pass. It was *Done for what existed at the time*. `TimelineEventKinds.CatalogDispatched` is now genuinely populated, sourced by **reading** the `dispatches` log rather than by writing a second `interactions` row, so there remains exactly one source of truth. `ShipmentRecorded` / `InvoiceCreated` / `InvoiceStatusChanged` are still unproduced placeholders pending E7/E8 (M5/M6) — the same caveat now applies to them and should not be forgotten a second time.

The merge ordering was fixed rather than papered over: both sources are fetched **unsorted** and a single sort is applied **after** the union. Sorting either source first is correct only when a dispatch happens to land at one end of the feed. Verified live — a dispatch at 11:30:53 sorted strictly between interactions at 11:30:51 and 11:30:55.

### 13.3 Verified on this machine, this pass

| Check | Result |
| --- | --- |
| `dotnet build` | 0 warnings, 0 errors. |
| `dotnet test` | **345 passing, 0 failed** (219 unit + 126 integration on Testcontainers Postgres), up from 246 at the E0 close. |
| `NODE_OPTIONS= npx ng test` | **176 passing, 0 failed**, up from 136. |
| `NODE_OPTIONS= npx ng build` | Succeeds. Initial bundle 303.36 kB vs. the 303.01 kB N-10 baseline — **+0.35 kB**, no new dependency. |
| Live `docker compose` (api+db, clean volume) | Migration applied from scratch (`payment_terms` and `ix_vendors_region` both present); login to forced password change to full-scope token; vendor, section, upload, versioning, download, dispatch and timeline all exercised over real HTTP; Associate-only permission boundary confirmed at 15 permissions with admin endpoints 403. Stack and volumes torn down afterwards. |

### 13.4 Deviations and additions from this pass

| # | Deviation | Rationale |
| --- | --- | --- |
| D-19 | **`vendors.payment_terms` added.** | FR-VEN-06/E5-06 names "MOQ, lead time, **payment terms** and reliability rating", but TECH_SPEC §6's `vendors` row omits it. **TECH_SPEC §6 should be corrected**, exactly as D-2 corrected it for `vendor_statuses`. This is a spec inconsistency, not a build decision. |
| D-20 | **One migration, `AddVendorPaymentTermsAndM4Indexes`.** | Keeps the M1 schema freeze (DR-2) breaking once, with review, as the pre-M3 migration did. Adds `ix_vendors_region`, a **GIN** index on `catalog_sections.tags` (array containment cannot use btree), and a composite `(catalog_section_id, is_latest)` replacing two weaker indexes. `Up`/`Down` reviewed by hand and confirmed reversible. |
| D-21 | **Catalog cards render a token-built placeholder, not a cover image.** | The prototype's cards show `k.coverImg` photos, but **no cover-image field exists** anywhere in the schema or API. Rather than invent one or request a schema change for decoration, the card renders a neutral placeholder at the same box size. If the business wants real cover images that is a new story with a migration, not a styling tweak. |
| D-22 | **Catalog sections require a vendor at creation and it is immutable thereafter.** | The prototype's upload dialog labels the vendor field "optional — editable later", but `UpdateCatalogSectionRequest` has no `VendorId` at all. The **API is the binding contract**; the dialog now requires a vendor on create and shows it read-only on edit. Caught by diffing assumed DTOs against the real backend — the same seam that produced M1's only two real defects. |
| D-23 | **New routes beyond the one TECH_SPEC §4.7 names.** | §4.7 names only `POST /dispatch-log`. Added `GET /dispatch-log/compose` (E9-06's template plus E9-01's link, both needed before a send) and `GET /catalog-documents/{id}/dispatches` (E9-07). Both additive; neither changes an existing contract. |
| D-24 | **Namespaces are `Dispatching`, not `Dispatch`.** | `Dispatch` collides with the `Domain.Entities.Dispatch` class and breaks unqualified references project-wide, since C# sibling-namespace lookup outranks `using` resolution. |

### 13.5 Open items after M4

| # | Item |
| --- | --- |
| **N-12** | **The seed test for role permissions could not catch the regression it existed to catch.** `SeedAsync_GrantsSuperAdminEveryPermission_AndAssociateEverythingExceptAdminOnly` derived its expected set **from `AdminOnly` itself**, so moving `Dispatch.Send` into `AdminOnly` would have silently kept it green while reversing an answered business decision (FSD Q4). Now covered by a test that hardcodes the permission code, plus a live JWT-claim assertion. **Worth auditing whether other tests derive their expectations from the same constant they are meant to protect** — this is a test-design smell, not a one-off. |
| **N-13** | **The bootstrap admin's password silently diverges from `.env`.** After the first forced password change the seeded admin no longer matches `BOOTSTRAP_ADMIN_PASSWORD`, and because seeding is idempotent it never reconverges — a later `docker compose up` against an existing `pgdata` volume then fails login with no explanation. Cost real debugging time this pass. Closely related to N-5 but distinct, and belongs in the same E12-09 operating-guide note. |
| **N-14** | **List search is not served by its indexes.** `ix_vendors_name` and `ix_catalog_sections_title` exist, but the query is `LOWER(col) LIKE '%term%'`, which no btree index can serve. **Pre-existing, not a regression** — `customers` search has the identical shape — but it means the DoD's "filter columns are indexed" is satisfied in letter and not in effect for substring search. Belongs to E12-02; if it matters at real volumes the answer is a trigram (`pg_trgm`) index, which is a dependency decision under C1. |
| **E5-07** | **Held for a business scope decision, deliberately not silently dropped.** The PA-4 default is to drop Could-haves — but **FSD Q5's recorded answer names it explicitly**: compliance documents are filed for reference only, and "scope is vendor-level (FR-VEN-07/E5-07, needs the `vendor_documents` table DR-4 already flags)". The owner's answered question and the descope default therefore point opposite ways. Building it now is materially cheaper than later, since E6's validated upload/download machinery is fresh and it would still be one reviewed migration rather than the mid-flight retrofit DR-4 exists to warn about. **Needs a yes/no.** |
| N-10 | **Unchanged and still undecided.** Initial bundle 303.36 kB against a 300 kB warning budget. This pass added +0.35 kB, so it is not the cause. The §12.6 recommendation (raise the budget with a written justification) still stands. |
| N-1, N-3, N-5 | **Unchanged.** |
| N-9, N-11 | **Partially addressed.** The M4 **backend** now has live HTTP verification, breaking the two-pass pattern. But the M3 and E0 surface those items originally described is **still** unverified live, and the M4 *screens* have not been exercised in a browser — only under Karma. **Do not read this pass as closing N-9/N-11.** |

### 13.6 Next

**Engineering, to finish M4:** E9-03, E9-04 and E9-05 — the "Send via WhatsApp" launch point, the 3-step dispatch dialog, and launching dispatch from a catalog section — plus the UI half of E9-07. Note that the ACTION_PLAN's own E9-04 text ("open chat → download PDF → mark sent") **does not match the prototype**, whose actual step order is **Download PDF → Open WhatsApp → Attach & send in chat**, with a separate "Log Dispatch" action. The prototype is the port source and wins; that story text should be corrected rather than followed.

**M4 cannot be called complete** until those screens land and the full exit criterion — onboard a vendor, upload a versioned catalog PDF, open WhatsApp pre-addressed to a customer, and see the dispatch on the customer timeline — is demonstrated end-to-end through the UI. The backend half of that chain is already proven live.

**Still needing the business owner, unchanged from §12.7:** FSD Q9c (gates M6), FSD Q6 and Q8 (ask together), the deployment track, N-10, and D-18 — plus **E5-07** above.

---

## 14. M4 close-out — all 26 stories done

**Last updated:** 2026-07-29, end of the M4 implementation pass. Supersedes §13's "screens partially landed" status. Same evidentiary standard as §9–§13.

**M4 is complete: 26 of 26 stories Done**, including E5-07, which §13 had held open for a scope decision. The owner confirmed on 2026-07-29 that FSD Q5 puts it in scope and that building it now beat the mid-flight retrofit DR-4 warns about. **DR-4 is now closed** — the `vendor_documents` schema gap it tracked since planning no longer exists.

**Exit criterion met.** Onboard a vendor, upload a versioned catalog PDF, open WhatsApp pre-addressed to a customer, and see the dispatch on the customer timeline — the backend chain is proven live end to end (§14.3). The one residual gap is that the *screens* have not been driven in a browser; see N-16.

### 14.1 Stories closed since §13

| ID | Status | Verification |
| --- | --- | --- |
| E5-07 | **Done** | `vendor_documents` + `document_types` lookup, migration `AddVendorDocuments`. Four endpoints, all permission-gated with a 403 test each, all audit-logged. Verified live: upload 201; a file declaring `application/pdf` whose bytes were `GIF89a` rejected 400; download 200 authenticated / **401 anonymous**; `DELETE` removed the stored file **from disk**, not only the row. `filePath` never leaves the service boundary. |
| E9-03 | **Done** | Customer detail's previously-inert "Send Catalog via WhatsApp" button now opens the shared dialog, gated on `Dispatch.Send`, and reloads the timeline on log. No parallel dispatch list added — the timeline is the single surface, per that component's existing decision. |
| E9-04 | **Done** | 3-step dialog ported from the prototype's **actual** order (Download PDF → Open WhatsApp → Attach & send) with **Log Dispatch as a separate action**. See §14.4 on the story text being wrong. |
| E9-05 | **Done** | Dispatch launches from a catalog card and from any document row on vendor detail — the same component, not a fork. |
| E9-07 | **Done** | Backend sub-resource from §13, now surfaced twice: the catalogs card's "Sent to N customers · last DATE" footer (restoring a prototype element dropped in the E6-08 pass for want of a data source) and an expandable per-row history panel on vendor detail. |

### 14.2 Deviations and additions from this pass

| # | Deviation | Rationale |
| --- | --- | --- |
| D-25 | **`doc_type` is a seeded `document_types` lookup with an FK, not a column.** | The §7 DoD requires category-like values to be FKs to a lookup table, never enums or free text (FSD §3.3), and **D-2** set the precedent by adding `vendor_statuses` for exactly this reason. `MasterDataService` and `DbSeeder.SeedLookupAsync<TEntity>` are already generic over `ILookupEntity`, so this was one mechanical case per switch rather than special-casing. Seeded: Business Licence, Quality Certificate, Test Report, Other — all `isSystemDefault`, therefore retire-only under N-8. |
| D-26 | **`document_types` added to the admin master-data screen — a small edit to the closed story E11-08.** | The frontend hand-enumerates its collections in **four** places (`MasterDataAggregate`, `COLLECTION_KEYS`, `COLLECTION_SEGMENTS`, `COLLECTION_LABELS`), so a new lookup ships seeded and API-editable but **invisible in the UI** — meaning the business could not add a document type without a deploy. That is precisely the quiet FSD §3.3 violation **DR-6** exists to catch ("breaks FSD §3.3 without failing a test"), so it was closed rather than deferred. TypeScript caught the one stale test fixture immediately, which is the interface doing its job. |
| D-27 | **No version history on vendor documents.** | `catalog_documents` has `is_latest` because FR-CAT-03 requires it. E5-07's criteria say only "attached and downloaded", and FSD Q5 says filed for reference only. Deliberately not built rather than assumed — if a renewed licence needs supersession semantics later, that is a new story. |
| D-28 | **One shared `DispatchDialogComponent` with `customerLock`/`documentLock` inputs.** | Entering from a customer you pick a document; entering from a document you pick a customer. Whichever side is known renders as a locked panel. The prototype's desktop dialog only ever needed the one direction, but its own **`mobile` reference screen** already shows the locked "To"/"Catalog" shape, so the styling is carried over rather than invented. |
| D-29 | **`DELETE` removes the stored file before the DB row**, matching `CatalogService`. | A storage failure then leaves a recoverable orphaned row rather than a row whose only access path is already gone. Minor cosmetic note: the per-vendor directory is left behind empty. |

### 14.3 Verified on this machine, this pass

| Check | Result |
| --- | --- |
| `dotnet build` | 0 warnings, 0 errors. |
| `dotnet test` | **375 passing, 0 failed** (235 unit + 140 integration on Testcontainers Postgres), up from 345 at §13. |
| `NODE_OPTIONS= npx ng test` | **198 passing, 0 failed**, up from 176. |
| `NODE_OPTIONS= npx ng build` | Succeeds. Initial bundle **303.36 kB — unchanged**, so the dispatch dialog is correctly lazy-chunked and N-10 is untouched. |
| Live `docker compose` (api+db, clean volume) | Both migrations applied from scratch; `document_types` seeded with 4 system defaults; vendor-document upload/spoof-rejection/download/anonymous-401/delete-removes-file all as above; an Associate-only token carries **15** permissions, downloads successfully via `Vendors.View`, and is **403** creating a document type; and the full dispatch chain still lands `CatalogDispatched` on the customer timeline with "sent to" history resolving. Stack and volumes torn down afterwards. |

### 14.4 A story text that is wrong, left recorded

**E9-04's acceptance criteria contradict the prototype they instruct porting.** The text says "open chat → download PDF → mark sent". The approved prototype does **Download PDF → Open WhatsApp → Attach & send in chat**, with **Log Dispatch as a separate action** rather than a third step. The prototype is the port source and won. Both the E6-08 and E9 passes independently hit this. **The story text should be corrected in §4**, not carried forward — the next reader should not have to rediscover it.

Relatedly, **Log Dispatch is deliberately not gated on completing the three steps.** The prototype's own handler never checks step state, and its footer wording ("N of 3 steps done") reads as informational. Gating it would invent a constraint the approved design does not have.

### 14.5 Open items after M4

| # | Item |
| --- | --- |
| **N-15** | **A latent permission mismatch on vendor detail.** The route is gated on `Vendors.View`, but the catalog-document endpoints it calls (`/download`, `/dispatches`) are gated server-side on `Catalogs.View`. A role holding `Vendors.View` **without** `Catalogs.View` would see document rows whose Preview and history actions 403. **Unreachable today** — both seeded roles hold both permissions — and it **pre-dates this pass** (the existing Preview button already had it). Left as-is rather than widened unasked, because "should a vendors-only role see catalog documents" is a permission-scope question of the same kind FSD Q4 was, i.e. a business decision. Worth asking alongside any future role beyond the seeded two. |
| **N-16** | **The M4 screens have never been driven in a browser.** Backend is live-verified; the UI has only been exercised under Karma against mocked HTTP. The dispatch dialog is the highest-value thing to click through, since it is explicitly a phone-first flow (the prototype's `mobile` reference calls dispatch one of "the two tasks staff do from a phone while WhatsApp is open"). This is also what E12-08's per-screen visual sign-off requires, which DR-5 says should happen **at the time, not batched into M8**. |
| N-12, N-13, N-14 | **Unchanged**, all from §13.5. N-12 (a seed test that derived its expectation from the constant it was meant to protect) is still worth auditing the wider suite for. |
| N-10 | **Unchanged and still undecided.** Initial bundle 303.36 kB against a 300 kB warning budget; this pass added 0.00 kB. |
| N-1, N-3, N-5 | **Unchanged.** |
| N-9, N-11 | **Still only partially addressed.** M4's backend is live-verified, but M3 and the E0 surface remain unverified live, and N-16 now records the same gap for M4's screens. **Do not read M4's completion as closing these.** |
| **DR-4** | **CLOSED.** The `vendor_documents` schema gap it tracked since planning is built, as one reviewed migration with the surrounding upload machinery already proven — which is exactly the outcome the mitigation aimed at. |

### 14.6 Next

**M5 — Inventory & shipments (E7).** Independent of M4 and unblocked. FSD **Q3 is answered** (TECH_SPEC OI-8: inventory per-SKU with SKU optional, matching what is already built), so E7-01's stated blocker is gone. Note E7 needs no new lookup work — `shipment_statuses` was seeded at E3-06.

**Before M5, two cheap corrections worth folding in:** TECH_SPEC §6's `vendors` row still omits `payment_terms` (D-19), and E9-04's story text still contradicts the prototype (§14.4).

**Still needing the business owner, unchanged:** FSD Q9c (gates M6 entirely), FSD Q6 + Q8 (ask together — the legacy data *is* the initial volume), the deployment track, N-10, and D-18. **E5-07 is no longer on this list** — it was answered and is built.

---

## 15. M5 backend pass — Inventory & shipments (E7-01…E7-10)

**Last updated:** 2026-07-29, end of the M5 **backend** implementation pass. Same evidentiary standard as §9–§14: `Done` means an executed command backs it.

> **SUPERSEDED BY §16 — M5 is complete, 13 of 13.** The paragraph below is the historical record of what this pass was at the time; it is **not** a live work list. Kept because §16's head-note (on the pass being implemented twice) only makes sense against it. **Do not schedule work from this section.**

**At the time of writing this was a deliberate mid-milestone checkpoint, not an M5 close-out.** The ten backend stories E7-01…E7-10 are Done; the three screen ports **E7-11, E7-12 and E7-13 were then Not started** and were explicitly held out of this pass. **M5 was therefore 10 of 13 stories at that point and could not yet be called complete** — §5's M5 exit criterion is written against demonstrable behaviour ("UC-06 and UC-07 are demonstrable"), and that is met at the API level only.

The two corrections §14.6 required were folded in first, in commit `62d2588`: TECH_SPEC §6's `vendors` row now carries `payment_terms` (D-19), and E9-04's story text in §4 now matches the prototype and the built flow (§14.4). The **same wrong dispatch-step wording was also found in TECH_SPEC §5.2** and corrected there — it existed in two places, and fixing one would have left the next reader to rediscover it. E7-01's stale "blocked on FSD Q3" marker was removed at the same time, since Q3 is answered.

### 15.1 Story status

| ID | Status | Verification |
| --- | --- | --- |
| E7-01 | **Done** | `InventoryController` full CRUD, `Inventory.View`/`.Edit` gated with a 403 test per endpoint, mutations audit-logged. Per-SKU with `sku` optional, matching the answered FSD Q3. |
| E7-02 | **Done** | `POST /inventory/{id}/inbound` writes an `inventory_inbound_entries` row and increments `on_hand_qty` in the same transaction, audit-logged. Returns the updated item alongside the entry so the caller re-renders both without a second round trip. |
| E7-03 | **Done** | List with `search` (name + sku), `categoryId`, `vendorId`, `stockLevel` and paging. `low` means below reorder **or** negative — one option, matching the prototype's single "Low or negative" dropdown entry and its `i.qty < i.reorder` predicate. |
| E7-04 | **Done** | `stockLevel` (`HEALTHY`/`LOW`/`NEGATIVE`) returned by the API per the story, classified exactly as the prototype's `invRows()` does. Raw `onHandQty`/`reorderThreshold` returned alongside it so the visual bar's ratio maths stays in the component, where presentation belongs. |
| E7-05 | **Done** | `ShipmentsController` shipment + line CRUD, `Shipments.View`/`.Edit` gated, audit-logged. `reference` generated server-side as `SHP-YYMM-NNN` and never accepted from the caller (D-37). |
| E7-06 | **Done** | Decrement happens in the same transaction as the lines. Negative-stock guard is D-35: 409 by default with the offending items named, `allowNegativeStock: true` to override deliberately. |
| E7-07 | **Done** | `PUT /shipments/{id}/status` is the **only** path that writes status, and it writes both a `shipment_status_history` row (D-33) and an audit entry. Any status in the lookup may be set — a hard-coded transition graph would break the moment the business adds a stage — but transitioning to the status already held is rejected. |
| E7-08 | **Done** | List filtered by `statusId`, `customerId`, `from`/`to` on dispatch date, plus `search` on reference and paging. Returns `statusCounts` across the whole filtered set **excluding the status filter itself**, or every tab would read its own total or zero. |
| E7-09 | **Done** | Upload/list/download/delete reusing the E1-05 validator and `IFileStorage` unchanged. `filePath` never crosses the service boundary, matching `VendorDocumentDto`. `awbOrBl` stored on the shipment. |
| E7-10 | **Done** | A `FREIGHT_ONLY` shipment with lines is rejected 400; without lines it moves no stock (FSD A8). Switches on `ServiceType.Code`, which is immutable per D-12 — label and id are not. |
| E7-11, E7-12, E7-13 | **Not started at this pass — now Done, see §16.1** | The three screen ports. **Deliberately deferred, not dropped** — this pass was scoped to the backend so the API contract is settled and diffable before any screen is built against it, which is the D-22 lesson. See §15.6. |

### 15.2 Verified on this machine, this pass

| Check | Result |
| --- | --- |
| `dotnet build` | 0 warnings, 0 errors. **Re-run by the coordinator**, not taken on report. |
| `dotnet test` | **557 passing, 0 failed** (340 unit + 217 integration on Testcontainers Postgres), up from 375 at the M4 close — +105 unit, +77 integration. **Re-run by the coordinator.** |
| Migration builds standalone | Commit `99df421` was staged alone and built with the services and tests stashed, confirming 0 warnings — so the schema commit does not depend on the code commit and `git bisect` cannot land on a non-building tree. |
| Migration reversibility | `Up`/`Down` hand-checked as a true structural inverse, then exercised as a generated SQL script applied **inside the container** with `ON_ERROR_STOP=1`: down reverted every object, up restored full M5 state, API returned Healthy. **Not** run via `dotnet ef database update` — see N-17. |
| Live `docker compose` (api+db, clean volume) | Performed by the delegated build agent and reported: migration applied from scratch, shipment document types seeded, inbound/decrement/409-override/freight-only/status-history/upload/anonymous-401 all exercised over real HTTP. **Not re-run by the coordinator** — see N-21, which is why this row is not written in the same voice as the two above. |

### 15.3 The API contract (the handoff artifact for E7-11…E7-13)

Recorded here rather than left in a build report, because the frontend pass happens later and **a screen built against an assumed DTO instead of the real one is this project's most-repeated defect** (D-22, and M1's only two real defects). Shapes below are as the API actually serialises them.

**Routes.** All under `/api/v1`, all policy-gated:

- `GET|POST /inventory`, `GET|PUT|DELETE /inventory/{id}` — `Inventory.View` / `Inventory.Edit`
- `POST|GET /inventory/{id}/inbound` — `Inventory.Edit` / `Inventory.View`
- `GET|POST /shipments`, `GET|PUT|DELETE /shipments/{id}` — `Shipments.View` / `Shipments.Edit`
- `PUT /shipments/{id}/status` — `Shipments.Edit`
- `GET|POST /shipments/{id}/documents` — `Shipments.View` / `Shipments.Edit`
- `GET /shipment-documents/{id}/download`, `DELETE /shipment-documents/{id}` — `Shipments.View` / `Shipments.Edit`

**Embedded lookup convention.** This track follows the **M4 vendor/catalog convention** (embed the resolved lookup object), *not* the CRM track's bare-id convention. `StatusRefDto` is `{id, code, label}`; `CategoryRefDto` is `{id, name}` — categories use `name`, everything else uses `code`+`label`, the same deliberate asymmetry §10.3 confirmed live. `VendorRefDto`/`CustomerRefDto` are `{id, name}`.

```jsonc
// InventoryItemDto
{ "id": "guid", "name": "string", "sku": "string|null", "description": "string|null",
  "category": { "id": "guid", "name": "string" },
  "vendor":   { "id": "guid", "name": "string" },   // nullable
  "unit": "string", "onHandQty": 0, "reorderThreshold": 0,
  "unitCost": "number|null",
  "stockValue": "number|null",   // COMPUTED onHandQty*unitCost; null = not costed, not "worth zero"
  "stockLevel": "HEALTHY|LOW|NEGATIVE" }

// GET /inventory  -> InventoryListResultDto
{ "items": [ /* InventoryItemDto */ ], "page": 1, "pageSize": 20, "totalCount": 0,
  "summary": { "onHandValue": 0, "itemCount": 0, "lowStockCount": 0, "negativeStockCount": 0 } }
  // summary is over the WHOLE filtered set, not the page — it backs the four stat tiles

// POST /inventory/{id}/inbound -> RecordInboundResultDto
{ "entry": { "id": "guid", "inventoryItemId": "guid", "quantity": 0, "entryDate": "2026-07-29",
             "reference": "string|null", "recordedByUserId": "guid", "recordedByName": "string",
             "createdAt": "iso-8601" },
  "item": { /* InventoryItemDto, already re-computed */ } }

// GET /shipments -> ShipmentListResultDto
{ "items": [ { "id": "guid", "reference": "SHP-2607-014|null",
               "customer": { "id": "guid", "name": "string" }, "destination": "string|null",
               "serviceType": { "id": "guid", "code": "CIF|FREIGHT_ONLY", "label": "string" },
               "dispatchDate": "iso-8601|null",
               "status": { "id": "guid", "code": "string", "label": "string" },
               "freightCost": "number|null", "totalValue": "number|null",
               "mode": "string|null", "awbOrBl": "string|null", "eta": "iso-8601|null",
               "lineCount": 0 } ],
  "page": 1, "pageSize": 20, "totalCount": 0,
  "statusCounts": [ { "statusId": "guid", "code": "string", "label": "string",
                      "sortOrder": 0, "count": 0 } ] }   // backs the status tabs

// GET /shipments/{id} -> ShipmentDetailDto = every list-item field above, plus these five:
{ "createdAt": "iso-8601", "recordedByName": "string|null",
  "lines": [ { "id": "guid", "inventoryItemId": "guid", "inventoryItemName": "string",
               "inventoryItemSku": "string|null", "unit": "string", "quantity": 0,
               "unitCost": "number|null", "lineTotal": "number|null" } ],
  "statusHistory": [ { "id": "guid", "status": { "id": "guid", "code": "string", "label": "string" },
                       "changedByUserId": "guid", "changedByName": "string",
                       "changedAt": "iso-8601", "note": "string|null" } ],  // renders the stepper's "when"
  "documents": [ { "id": "guid", "shipmentId": "guid", "originalFilename": "string",
                   "sizeBytes": 0,
                   "documentType": { "id": "guid", "code": "string", "label": "string" },
                   "uploadedByUserId": "guid", "uploadedByName": "string",
                   "uploadedAt": "iso-8601" } ] }

// 409 from POST/PUT /shipments when a decrement would go negative (D-35)
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Insufficient stock.", "status": 409,
  "detail": "Recording this shipment would drive on-hand quantity negative for one or more items. Retry with allowNegativeStock: true to override deliberately.",
  "instance": "/api/v1/shipments",
  "insufficientStock": [ { "inventoryItemId": "guid", "itemName": "string", "sku": "string|null",
                           "requestedQty": 0, "availableQty": 0 } ] }
```

**Two request-shape traps the screens must respect**, both deliberate:

- `UpdateInventoryItemRequest` has **no `onHandQty`** (D-42). Stock moves only through an inbound entry or a shipment line. An edit form that renders an editable on-hand field will silently drop it.
- `UpdateShipmentRequest` has **no `statusId`** (D-43). Status moves only through `PUT /shipments/{id}/status`, the one path that writes history. An edit form that includes a status dropdown will silently fail to change it.

### 15.4 Deviations and additions from this pass

Continuing the document's D-# sequence from D-29. The build report used provisional letters; the mapping is given in each row so its reasoning stays traceable. D-30…D-39 were settled by the coordinator **before** delegation, from diffing the E7 story text against the approved prototype; D-40…D-49 arose during the build.

| # | Deviation | Rationale |
| --- | --- | --- |
| D-30 *(a,b)* | **`unit_cost` added to both `inventory_items` and `shipment_lines`**, the latter snapshotted at line creation. | The approved inventory screen renders a **Stock Value** column and an "On-Hand Value" tile, and the shipment detail screen renders per-line "Unit Cost"/"Line Total" — no cost column existed anywhere, so both screens were unportable. Snapshotted on the line rather than read live because **E8 (M6) raises CIF invoices against a shipment**: a later item price change would otherwise silently rewrite the basis of an already-issued invoice. Stock value and line total are always computed, never stored. |
| D-31 *(c)* | **`shipments.total_value` is server-computed when the shipment has lines**, and accepted from the caller only when it has none. | Never trust a client total that contradicts the lines. The no-lines case is the freight-only one, which genuinely has no lines to compute from. |
| D-32 *(d)* | **New table `inventory_inbound_entries`.** | E7-02's criteria say "an inbound entry **with date and reference**" — a durable business record. The audit log is a cross-cutting concern with a jsonb detail blob, not a queryable business table, so it could not be the only home for this. |
| D-33 *(e)* | **New table `shipment_status_history`.** | The approved shipment detail screen renders a 4-step stepper with a **`when` under each step**, unrenderable without transition timestamps, and E7-07 requires transitions be "audit-logged **and timestamped**". Both are written — they serve different readers. An opening row is seeded at shipment creation. |
| D-34 *(f)* | **`shipment_documents.doc_type` → `document_type_id` FK, and `document_types` gains `scope`.** | The DoD and **D-25** (which created `document_types` for exactly this shape) require it; the entity comment that argued for free text is superseded and was updated rather than left contradicting the code. `scope` exists because mixing shipment types into one undifferentiated lookup would make the **vendor** upload dropdown offer "Packing List". Existing rows backfilled to `Vendor`; four shipment-scoped defaults seeded (Packing List, Bill of Lading, Airway Bill, Invoice), all `isSystemDefault` and therefore retire-only per N-8. |
| D-35 *(g)* | **Negative stock: reject 409 by default, `allowNegativeStock: true` to override**, with the override recorded in the audit detail. | FSD says "prevent **or** flag" and E7-06 says "rejected **or** explicitly flagged" — this is both halves, and it is not splitting the difference. **The approved prototype's own seed data contains an item at `qty: -40`, a `NEGATIVE` badge, a "Low or negative" filter and a "Negative Stock: 1" tile** — the design demonstrably expects negative stock to be reachable and visible, so a hard block alone would contradict the screen E7-11 must port. 409 not 400: the request is well-formed and may legitimately be retried unchanged with the flag. |
| D-36 *(h)* | **Freight-only shipments reject supplied lines with 400** and move no stock (FSD A8). | Switches on `ServiceType.Code`. Codes are immutable by design (D-12); labels and ids are not, so switching on either would break the first time the business renames a service type. |
| D-37 *(i)* | **`reference` generated server-side as `SHP-YYMM-NNN`**, per-month sequence, never accepted from the caller, with a retry on unique violation. | The partial unique index already existed from the M1 pass, which anticipated exactly this. See N-18 — the retry branch is not proven to have fired. |
| D-38 *(j)* | **Stock movement on shipment update is delta-based**; deleting a shipment restores what its lines consumed. | Re-decrementing from scratch on every edit would compound. Note there is **no "Cancelled" shipment status** in the seeded set, so status transitions never restore stock — only line edits and deletion do. |
| D-39 *(k)* | **The inventory list response carries a `summary` block** over the whole filtered set. | The approved screen has exactly these four stat tiles. Deliberately duplicates a little of what **E10-05** (analytics, M7) will aggregate: E10-05 aggregates by category across the business, this is screen-local and must honour the caller's active filters, so neither can serve the other's job without a parameter both would rather not have. Recorded so the overlap is a choice, not an accident. |
| D-40 *(l)* | **`inventory_items.description` added.** | Named by E7-01's own criteria, absent from TECH_SPEC §6 — the same class of spec inconsistency as D-2 and D-19. |
| D-41 *(m)* | **`shipment_documents` gains `size_bytes` and `uploaded_by_user_id`.** | The approved screen's document rows show "184 KB · 21 Jul 2026", and uploader attribution is parity with `vendor_documents` plus the FSD's Auditability NFR. |
| D-42 *(n)* | **`UpdateInventoryItemRequest` cannot change `onHandQty`.** | Stock moves only through the two recorded paths, so a plain edit can never silently rewrite a balance that an inbound entry or a shipment is the audit record for. An opening balance is settable once, at create. **Leaves stock-take correction unserved — see N-19.** |
| D-43 *(o)* | **`UpdateShipmentRequest` cannot change `statusId`.** | `PUT /{id}/status` is the only path that writes history; letting a general edit set status too would produce transitions with no history row and a stepper with gaps. |
| D-44 *(p)* | **`document_types.scope` is plain text, not an FK to a lookup.** | A deliberate narrow exception to the FK rule: `scope` is a **code-path discriminator** the application switches on, not business data a Super Admin should be able to add rows to. Inventing a third scope at runtime would have no code path to serve it. |
| D-45 *(q)* | **`LookupItemDto.scope` is a trailing nullable across all six master-data collections**, null for the five that have no scope. | One shape for all collections keeps `MasterDataService`'s generic machinery generic. Trailing, so no existing positional consumer breaks. |
| D-46 *(r)* | **A composite `(status_id, dispatch_date)` index replaces `ix_shipments_status_id`.** | A btree composite serves any leading-column predicate, so keeping both would cost write throughput for no read benefit. |
| D-47 *(s)* | **`recordedByName` reads the earliest `shipment_status_history` row.** | The approved detail screen shows a "Recorded by" field and `shipments` has no `created_by_user_id`. Rather than add a column, this reads a fact D-33's table already stores. |
| D-48 *(t,u)* | **`DELETE` guards**: 409 rather than orphaning stored files, 400 rather than leaving a dangling FK. | Same reasoning as D-29 — fail in a recoverable direction. |
| D-49 *(v)* | **`UniqueViolationDetector` duck-types Npgsql's `SqlState` via reflection.** | `Application` must not reference Npgsql (TECH_SPEC §4.1 keeps the provider in Infrastructure). Reflection is the seam that preserves that boundary without introducing a new abstraction for one string comparison. |

**Two real bugs were found and fixed during the build**, both diagnosed to root cause rather than patched:

1. `ShipmentService.UpdateAsync` called `Remove()` on line entities and then `Clear()` on the navigation collection, which flipped the ChangeTracker entries from `Deleted` back to `Modified` and failed `SaveChanges`. Diagnosed by inspecting the ChangeTracker. `VendorService`'s superficially similar pattern does **not** have the bug, because `VendorCategory`'s FK is part of its composite primary key — worth knowing before anyone "fixes" the vendor code to match.
2. `ChangeStatusAsync` wrote duplicate history rows by adding to both the `DbSet` and the navigation collection.

### 15.5 Open items after the M5 backend pass

| # | Item |
| --- | --- |
| **N-17** | **`dotnet ef database update` cannot run on this machine** — a host-installed Postgres occupies port 5432 and collides with Docker's published port. Reversibility was verified instead by generating the migration SQL and applying it **inside the container** with `ON_ERROR_STOP=1` (down reverted every object, up restored full M5 state, API Healthy). That is genuine evidence, but it is a **different tool path from the one E2-07's deploy job uses**, and E2-07 is already the least-validated story in the plan (§9: "Draft — never executed"). Worth resolving before the deployment track starts, not during it. |
| **N-18** | **D-37's reference-retry branch has never actually fired.** The `SHP-YYMM-NNN` format and its uniqueness are proven; the 23505 collision-catch path is not, because nothing generated a genuine concurrent collision. It is a real code path guarding a real race, currently untested by execution. Cheap to close with a forced-collision test — worth doing before shipments are created concurrently in the field. |
| **N-19** | **Stock-take correction has no path**, as a direct consequence of D-42. Stock moves only via inbound entries and shipment lines, so when a physical count disagrees with the system there is **no way to correct it** short of inventing a fake inbound entry — which would corrupt the very audit record D-42 exists to protect. **This needs a business answer, not an engineering one:** does the owner do periodic physical stock-takes? If yes, the natural shape is `POST /inventory/{id}/adjustment` with a mandatory reason — a small story and one more recorded movement type. Deliberately not built unasked. |
| **N-20** | **The D-34 scope change has three frontend must-dos that are not yet done**, and one is a live FSD §3.3 risk. (a) The `LookupItem` TypeScript interface needs `scope?: string \| null`. (b) The admin master-data **create-document-type form needs a scope selector** — without it every new type silently defaults to `Vendor` and **can never back a shipment upload**, which is precisely the quiet configurability violation **DR-6** exists to catch and **D-26** already had to close once for this same screen. (c) Both upload dropdowns need client-side scope filtering. All three land in the E7-11…E7-13 pass. |
| **N-21** | **M5's live verification is second-hand.** `dotnet build` and `dotnet test` were re-run by the coordinator; the clean-volume `docker compose` HTTP run was performed by the delegated build agent and is recorded on its report, not re-executed. That is weaker evidence than §13/§14 carry for M4, where the live pass was run directly. It should be re-run first thing in the E7-11…E7-13 pass, when the screens give it a reason to exist anyway. **Do not read §15.2's live row as equal in weight to the two rows above it.** |
| N-16 | **Extended, not closed.** M4's screens have still never been driven in a browser, and M5 now adds ten endpoints whose screens do not exist yet. |
| N-10 | **Unchanged and still undecided.** Initial bundle 303.36 kB against a 300 kB warning budget. **This pass touched no Angular at all**, so it is untouched by definition. |
| N-12, N-13, N-14 | **Unchanged**, all from §13.5. N-12 (a seed test deriving its expectation from the constant it was meant to protect) is still worth auditing the wider suite for — this pass added 77 integration tests without that audit having happened. |
| N-15 | **Unchanged**, and worth re-reading now: M5 introduces `Shipments.View`, whose detail screen renders inventory-item names. A role holding one of `Shipments.View`/`Inventory.View` without the other has the same latent mismatch N-15 records for vendors/catalogs. Still unreachable with the two seeded roles. |
| N-1, N-3, N-5, N-9, N-11 | **Unchanged.** |

### 15.6 Next

**To finish M5:** the three screen ports **E7-11** (Inventory), **E7-12** (Shipments list), **E7-13** (Shipment detail), against the contract recorded in §15.3 — plus the three D-34 frontend must-dos in N-20, which are not optional polish: (b) is a configurability violation of the kind DR-6 exists to catch.

The API contract should be **diffed against a live response before the screens are wired**, not assumed from §15.3. That diff is exactly what D-22 and M1's two defects were caused by skipping, and §15.3 is a written record, not an executed one. Sequence the live `docker compose` run (N-21) into that pass rather than after it, so the screens and the endpoints are proven together.

**Then M6 — Invoicing (E8)**, which M5 was a prerequisite for: E8-01 references a shipment for the CIF case, and D-30's snapshotted line cost exists specifically so that invoice basis cannot drift. M6 remains **fully gated on FSD Q9c** and still needs **DR-3** (no PDF library chosen) settled *before* it starts, not during it.

**Still needing the business owner:** FSD **Q9c** (gates M6 entirely), FSD **Q6 + Q8** (ask together — the legacy data *is* the initial volume), the **deployment track** (N-17 now adds a reason to settle it), **N-10**, **D-18**, and newly **N-19** (stock-take corrections).

---

## 16. M5 close-out — the three screen ports (E7-11, E7-12, E7-13)

> **This pass was implemented TWICE, in parallel, from `cda360d`.** Neither side knew the
> other existed: this section's work (`6f808bd` + `88b2735`, on a worktree branch) and a
> second, independent pass committed as **`9bed1e7` on `master`**. Both wrote a §16 claiming
> "13 of 13", and **both allocated D-50…D-58 to *different* deviations**. This section is the
> surviving record; `9bed1e7` remains in history via the consolidation merge, and **its D-#
> and N-# numbers do not resolve against this document** — read them only against the commit
> itself.
>
> The consolidation kept this implementation (it builds the create/edit/inbound flows that
> actually make §5's M5 exit criterion demonstrable) and carried across the other pass's
> documentation work and four code items, recorded below as **D-59…D-61** and **N-23**.
> Root cause: the pm-orchestrator loop re-issued M5's screen stories against a plan that
> still read "10 of 13", because §15's checkpoint was never updated when the worktree pass
> completed. **Update the milestone tracker at the moment a pass lands, not at close-out.**

**Last updated:** 2026-07-31 (consolidation). Body below unchanged from the 2026-07-30 frontend pass. Supersedes §15's "10 of 13 stories" status. Same evidentiary standard as §9–§15: `Done` means an executed command backs it.

**M5 is complete: 13 of 13 stories Done.** The three screen ports landed against a contract that was **diffed against a live response before any component was wired**, which is what §15.6 required and what D-22 and M1's two defects were caused by skipping. That diff found one real defect (D-50) that no test in the suite could have caught, because the suite's own fixtures encoded the same wrong assumption — see §16.3.

**N-21 is closed.** §15.2's live row was reported by a delegated build agent and not re-executed; this pass re-ran the clean-volume `docker compose` stack **first-hand as the coordinator**, before delegating any frontend work, and every row in §16.4 was likewise re-run directly rather than taken on an agent's report.

**M5's exit criterion is met at the UI level with one stated exception.** UC-06 and UC-07 are demonstrable through the screens against live endpoints. The exception is that the screens have still never been driven in a **browser** — see N-16, which this pass attempted to close and could not.

### 16.1 Story status

| ID | Status | Verification |
| --- | --- | --- |
| E7-11 | **Done** | Inventory screen ported: search + category + stock-level filters, the four `summary`-driven stat tiles, and the low-stock visual bar with its ratio maths ported verbatim from the prototype's `invRows()` (plus a zero-threshold guard the prototype never needed — D-53). Wired to `GET /inventory`; add/edit and record-inbound dialogs wired to E7-01/E7-02. |
| E7-12 | **Done** | Shipments list ported: status tabs built from the live `statusCounts[]` in `sortOrder` — including zero-count statuses, and correctly showing each tab's true total because the API computes counts *excluding* the active status filter (E7-08). Stock Impact derives from `serviceType.code`, never the label (D-36). |
| E7-13 | **Done** | Shipment detail ported: the 4-step stepper, shipment lines, details panel and reference-documents section. The stepper's per-step "when" reads the real `shipment_status_history` (D-33) rather than the prototype's invented `stepWhen` array. Invoice document left as a slot (D-52). |
| **N-20 (a)** | **Done** | `scope?: string \| null` added to `LookupRow` in **both** `core/models/master-data.models.ts` and `admin/models/admin-master-data.models.ts`. Live-confirmed the API serialises `scope` on every collection, null for all but `documentTypes`. |
| **N-20 (b)** | **Done** | The admin create-document-type form now has a **Scope** selector (`Vendor`/`Shipment`), shown only for the `documentTypes` collection. This closes a live DR-6-class configurability violation: `UpsertMasterDataRequest.Scope` **defaults to `"Vendor"` when omitted**, so before this every type a Super Admin created was silently vendor-scoped and could never back a shipment upload. Read-only on edit — see D-51. |
| **N-20 (c)** | **Done for the shipment side; the vendor side has no consumer to fix** — see N-22. `MasterDataService.documentTypeOptions(scope)` takes a **required** `'Vendor' \| 'Shipment'` parameter, so a caller cannot ask for an unscoped list and get the wrong one. The shipment upload dialog consumes it. |

### 16.2 The live-contract diff (the §15.6 gate)

Run first, before any component was wired, against `docker compose` (api+db) on a **clean volume**, over real HTTP, by the coordinator.

**Outcome: §15.3's recorded contract is substantially accurate.** Every embedded lookup shape, `stockValue: null` semantics, the filtered `summary` block, `statusCounts` behaviour, the 409 `insufficientStock` extension and the freight-only 400 matched exactly as documented. Four imprecisions were found in §15.3's **example JSON** (not in the API), and are corrected here rather than left to mislead the next reader:

| # | §15.3 says | The API actually does |
| --- | --- | --- |
| 1 | `"onHandQty": 0`, `"quantity": 0` — integers | **Decimals.** `onHandQty: 1840.0`, `quantity: 500.0`, and the 409's `availableQty: 10.000`. TypeScript models them as `number` either way, but a screen must not assume whole numbers when formatting. |
| 2 | `"pageSize": 20` | Default is **25**. §15.3's `20` was an example literal from a request that passed `pageSize=20` explicitly. |
| 3 | `dispatchDate`/`eta`/`entryDate` all as "iso-8601" | `dispatchDate`/`eta` are **full ISO-8601 with time** (`2026-07-22T00:00:00Z`) despite being logical dates; `entryDate` is a **bare date string** (`2026-07-29`). Two different shapes, modelled distinctly. |
| 4 | (silent) | `POST /shipments` returns the **full `ShipmentDetailDto`**, not the list-item shape, and auto-seeds one `statusHistory` row noted `"Shipment created."` |

**The two request-shape traps were verified by attack, not by reading.** `PUT /inventory/{id}` with `onHandQty: 999999` left the stored value at 2000 (D-42 holds). `PUT /shipments/{id}` with a different `statusId` left the status at `PACKED`, and only `PUT /shipments/{id}/status` moved it — writing a history row as it went (D-43 holds). Both screens are built so the field cannot be rendered in the first place, and both have a regression test asserting its **absence**.

### 16.3 The defect the diff caught — and why no test could have

`StatusStyleService`'s `SVC` map was keyed on **`'Freight-only'`**, the prototype's display label. The live API's `serviceType.code` is **`FREIGHT_ONLY`** (its *label* is `Freight-only`). Every caller passes `code`. Consequences, all live and all pre-dating this pass:

- Every freight-only chip app-wide fell through to the grey default instead of the orange freight pair.
- `intake.component.ts`'s `showExtRef` compared `code === 'Freight-only'`, so **the lead-intake form's external-purchase fields never appeared** against the real API — a functional break on a closed M3 story.
- `customer-detail.component.ts` branched on the *presentation label* `'FREIGHT-ONLY'`, so its freight fields never rendered either.

**The whole suite was green throughout, because every spec fixture in the client declared `code: 'Freight-only'`.** The fixtures encoded the same wrong assumption as the code, so the tests confirmed the bug rather than catching it. This is exactly the failure mode **N-12** recorded ("a test that derives its expectation from the constant it is meant to protect"), generalised from one seed test to a whole suite's fixtures — and it was only findable by diffing against a live response. **N-12 should now be read as a suite-wide concern, not a single-test one.**

Fixed: the map is keyed on the API `code`, both functional branches compare `code` (customer-detail gained a private `serviceTypeCode` computed so no branch can reach for the label again), every fixture uses the real code, and a new regression test hardcodes the literal `FREIGHT_ONLY` rather than deriving it from the map it guards.

### 16.4 Verified on this machine, this pass

Every row re-run by the coordinator. None taken on a delegated agent's report — that distinction is why N-21 existed.

| Check | Result |
| --- | --- |
| Live `docker compose` (api+db, **clean volume**) | Migration applied from scratch; 8 document types seeded with correct `Vendor`/`Shipment` scopes; login → forced password change → full-scope token; inventory create/list/filter/inbound, shipment create/detail/list/status-change, negative-stock **409** with named items, freight-only-with-lines **400**, freight-only-without-lines **201**, and both D-42/D-43 traps all exercised over real HTTP. Stack and volumes torn down afterwards. |
| `dotnet build` | 0 warnings, 0 errors. |
| `dotnet test` | **557 passing, 0 failed** (340 unit + 217 integration on Testcontainers Postgres) — unchanged from §15.2, as expected: this pass touched **no C# at all** (verified against `git status`; every changed path is under `client/` or `docs/`). |
| `NODE_OPTIONS= npx ng build` | Succeeds. All three screens correctly lazy-chunked (`inventory-component` 27.50 kB, `shipment-detail-component` 16.51 kB, `shipments-component` in the tail). |
| `NODE_OPTIONS= npx ng test` | **284 passing, 0 failed**, up from 198 at the M4 close (§14.3) — +86. |
| **Screens driven in a real browser (N-22/N-16)** | **Done, 2026-07-31, on the consolidated tree** — API from source on `:5000` against live Postgres, `ng serve` on `:4300`. Five screens rendered and exercised as Super Admin: **Inventory** (On-Hand tile at `₹34.5 L` per D-61; the oversold item at `-40` showing the red NEGATIVE chip, full-width bar and row wash together), **Shipments** (status tabs `All 5 / Packed 2 / Dispatched 0 / In Transit 2 / Delivered 1`, including the zero), **Shipment detail** (stepper, lines, totals, `Recorded by` resolving via D-47, empty-documents state), **New Lead Intake** and **Customer detail**. **D-50 confirmed live and functionally, for the first time:** the freight-only chip renders orange `FREIGHT-ONLY` rather than the grey `FREIGHT_ONLY` fallback, and **all six of FSD Q1's external-purchase fields actually appear** on intake when Freight-only is selected, plus the four on customer detail. Those fields had never once been seen rendering against a real API. **Earlier screenshots were discarded and re-shot** — they were taken before consolidation and are evidence for a build that no longer exists. One cosmetic defect found and fixed (see D-63). |
| Bundle budget (N-10) | Initial **304.87 kB** vs a **rebuilt-not-recalled** baseline of **303.36 kB** → **+1.51 kB**. The baseline was re-measured by stashing this pass's work and building `HEAD` directly rather than trusting §15.5's recorded figure; it came back at exactly 303.36 kB, so the record was accurate and the delta is honestly attributable to this pass. |

### 16.5 Deviations and additions from this pass

Continuing the document's D-# sequence from D-49.

| # | Deviation | Rationale |
| --- | --- | --- |
| D-50 | **`StatusStyleService`'s `SVC` map is keyed on the API `code`, not the prototype's label**, with the two codes extracted to `shared/constants/service-type-codes.ts`. | The prototype's own `SVC` constant is label-keyed; ported faithfully, that was wrong against a live API whose `code` is `FREIGHT_ONLY`. The **API is the binding contract** — the same conclusion D-22 reached about the catalog upload dialog. Full impact, and why the suite stayed green, in §16.3. **Found independently by both parallel passes**, which is strong convergent evidence the diagnosis is right. The named constants come from the other pass and replace an inlined `'FREIGHT_ONLY'` literal at four call sites (`status-style.service`, `intake`, `customer-detail`, and the `mock-invoices` doc comment), so a future code change is one edit plus compile errors everywhere else. The regression spec deliberately keeps the literal — a test that imports the constant it guards would have passed against the bug (N-12's defect class). Its wider impact was also worse than first recorded: `intake` and `customer-detail` both gated **FSD Q1's external-purchase fields** on the label, so those fields never rendered against the real API, and `customer-detail` branched on the Super-Admin-editable *label* rather than the immutable code. |
| D-51 | **The admin scope selector is create-only; on edit, scope renders read-only.** | Not a UI shortcut — verified against the actual C# DTO: `UpsertMasterDataRequest.Scope` is *"honoured on create and ignored on update"*, on the same reasoning as `Code` under **D-12** (a type whose scope changed after documents referenced it would silently move those documents into the other module's dropdown). Rendering an editable control the server ignores would have been the worse lie. |
| D-52 | **The shipment detail invoice document stays a plain slot.** | `document_types` really does seed an `INVOICE` type scoped to `Shipment` (D-34) and the prototype's mock data has an invoice row, but the actual invoice *link* is E8/M6 and out of scope. An uploaded invoice renders like any other document row; no generation, linking or navigation was built. **M6 remains gated on FSD Q9c.** |
| D-53 | **The low-stock bar formula gained a zero-reorder-threshold guard.** | Ported verbatim from `invRows()`, the formula is `clamp(round((qty / (reorder * 2.5)) * 100), 0, 100)` — which divides by zero when `reorderThreshold` is 0. The prototype's mock data never seeds a zero threshold; live data can, and the API imposes no minimum. A zero-threshold item has no "below reorder" concept, so the bar reads full when there is any stock and empty when there is none. |
| D-54 | **Stock-level colours live in a feature-local `stock-level.util.ts`, not `StatusStyleService`.** | `HEALTHY`/`LOW`/`NEGATIVE` are not lead/vendor/shipment statuses and have no entry in the shared `ST` map — routing them through it would have silently returned `ST.NEW`'s grey for every row. The prototype makes the same split for the same reason, computing this triple locally in `invRows()` rather than through its own shared `ST` constant. The values are the existing success/warning/danger tokens, so no new palette was invented. |
| D-55 | **`--color-danger-bg-subtle` (`#fff8f8`) added, distinct from `--color-danger-bg` (`#ffebee`).** | The prototype tints a negative-stock **row** with `#fff8f8` and a danger **chip** with `#ffebee`; they are not interchangeable, and the stronger tint behind a full-width row reads as an error state rather than the quiet flag the approved screen intends. Caught on review of delegated work that had reached for the nearest existing token. `docs/DESIGN_TOKENS.md` updated in the same edit, per that file's own "change one, change the other" rule. |
| D-56 | **Three new shared atoms — `.pill-tab`, `.stock-bar`, `.stepper` — settled by the coordinator *before* delegation.** | `.pill-tab` is deliberately a **second** tab atom rather than a variant of the existing underline `.tab`: the prototype uses two unrelated tab treatments that share no declarations beyond font size. Fixing these centrally up front is what stopped two parallel agents from each inventing their own; both reported needing no further shared atom. |
| D-57 | **Currency formatting consolidated into `shared/utils/money.util.ts`.** | Three byte-identical `Intl.NumberFormat('en-IN', …)` copies had accumulated (invoices, inventory, shipments). The last two were an **artefact of this pass's own file-ownership split** — each agent was told to stay inside its feature folder — not a design decision, so the coordinator merged them afterwards rather than shipping the duplication. Sits beside `date-format.util.ts`, already the established home for cross-feature formatters. |
| D-58 | **The inventory screen gained a third header button, "+ Add Item", and per-row Inbound/Edit actions.** | The prototype's inventory screen has **zero wired actions** — every button is a static mock. E7-01/E7-02 require create, edit and inbound to be reachable, and the prototype offers no entry point for them. Row actions sit inside the Item cell rather than in a new column, so the table still has exactly the seven columns the approved design specifies. |

**Carried across from the parallel pass (`9bed1e7`) during consolidation:**

| # | Deviation | Rationale |
| --- | --- | --- |
| D-59 | **TECH_SPEC §6 resynced to the schema** — `vendor_documents` and `document_types` rows **added**, and `inventory_items`, `inventory_inbound_entries`, `shipment_lines`, `shipment_status_history`, `shipment_documents` corrected. | This pass never touched `docs/TECH_SPEC.md`; the other one did, and it is the single largest artefact carried across. Two of the additions are **M4** gaps, not M5 ones: `vendor_documents` and `document_types` were built in the pass that closed DR-4 and never written back to §6, so the spec has been silently missing two tables since M4. Same correction-in-place precedent as D-2, D-19 and commit `62d2588`. |
| D-60 | **`MasterDataResponse` (core) gains `documentTypes`.** | Numbered retroactively: **this pass made the change** as part of N-20(a) but never gave it a D-number, so §16.5 did not match the code. `MasterDataAggregateDto` has always served seven collections while the core model declared six — document types were invisible to every consumer outside the admin screen, which re-declares its own shapes. A blind spot, not a new field. |
| D-61 | **The On-Hand Value tile renders abbreviated (`₹41.2 L`), via `formatInrCompact` in `money.util.ts`.** | This pass's tile used full precision (`₹41,20,000.00`). Correct arithmetic, but the approved screen reads `'₹41.2 L'` (prototype ~line 1572) and **E7-11's acceptance criterion is a 1:1 port**, so the tile was failing its own test. Tiles are glanced at, not reconciled — the Stock Value column below keeps full precision. Carried from the other pass, which got this right. |

| D-62 | **`Jwt:SigningKey` for local development is committed, in `appsettings.Development.json`** (N-23). | Flagged explicitly because "a signing key in a committed file" is exactly what a security review should stop on — so the reasoning needs to be on the record rather than reconstructed. It is **not a secret and guards nothing**: it signs only tokens issued by a developer's own machine against their own database. It **cannot reach a deployed environment**, because ASP.NET Core loads that file only under `ASPNETCORE_ENVIRONMENT=Development`, and both the container and the deploy pipeline set the real key from `JWT_SIGNING_KEY`, which overrides it. The alternative, user-secrets, imposes per-developer setup on a two-person project for zero security gain. **The value is deliberately long** — HS256 needs ≥256 bits, which is what `IDX10703` was complaining about. |
| D-63 | **`sourceChannel` renders `—` when empty on the customer detail screen.** | Trivial, but worth recording for *how* it was found: `sourceChannel` is non-nullable on the DTO, so it never hit the `?? '—'` its nullable neighbours use — yet it can be an **empty string**, which rendered a blank row in a column of em-dashes. **Invisible to the unit tests, whose fixtures all populate it**, and caught in the first minute of the N-22 browser pass. A small piece of evidence for why DR-5 wants the visual sign-off at the time. |

| D-64 | **Standing rule, from the N-12 audit: a test may reference a constant to prove *behaviour*, but never to prove *identity*. An identity assertion terminates in a literal, or it proves nothing.** | One sentence that would have caught all three of this milestone's evidence failures — D-50 (client fixtures encoding the same wrong service-type code the production map used), the `DbSeederTests` round-trips below, and N-25 (a merge duplication a green build missed). The audit found the defect class was **systemic in `DbSeederTests.cs`, not the single instance N-12 recorded**: eight assertions of the form "the database contains exactly what the constant says", where the constant is the seeder's own input. **All rewritten to literals**, the highest-value being the permission catalogue and the Associate role mapping — the Associate set was derived as `All.Except(AdminOnly)`, so shrinking `AdminOnly` would have made the test assert that Associates *should* hold `Admin.ManageUsers`, and pass. Permission codes cross three boundaries (seeder → `[Authorize(Policy=…)]` → the client's `permissionGuard`), so a silent divergence there is an authorisation hole, not a rendering bug. **Verified by mutation**: renaming one code by one character now fails 3 tests where the previous version passed. Idempotency and preservation assertions correctly still reference the constants — there the constant is an *input* to the behaviour under test, not the answer being checked. |
| D-65 | **Two contract literals pinned server-side, closing the audit's "other direction" gap.** | The client asserts `scope` values and `stockLevel` values as literals, but every such test mocks HTTP — so **nothing pinned what the API actually serialises**. `stockLevel` turned out to be already covered by the M5 integration tests (`"HEALTHY"`/`"LOW"`/`"NEGATIVE"` asserted literally), so only `scope` needed work: `GET /master-data` now asserts the wire values are exactly `"Vendor"`/`"Shipment"`, that **both** scopes are actually present (a "filtered to Shipment" dropdown could otherwise be legitimately empty for a reason no test would catch), and that D-45's trailing nullable really is `null` on the other collections. A drift here empties a dropdown **silently** — the precise failure mode N-20(c) exists to prevent. |

| D-66 | **`Microsoft.EntityFrameworkCore.Design` added to `SourcingOps.Api.csproj`** — and with it, **E2-07's deploy command has now actually been run for the first time**. | Found by finally executing N-17's blocked path rather than reasoning about it. `Infrastructure` already referenced the package, but with `PrivateAssets="all"`, so it deliberately does **not** flow to the startup project — and the EF CLI requires it *there*. `dotnet ef database update` therefore failed outright with *"Your startup project 'SourcingOps.Api' doesn't reference Microsoft.EntityFrameworkCore.Design"*. **That is the exact command E2-07's deploy job runs, and it had never worked.** It went unnoticed because every migration to date was applied either by the API's own auto-migrate on startup (Development only) or by generated SQL run inside the container — neither of which exercises the CLI path the deploy job depends on, which is precisely why E2-07 sat as "Draft — never executed" since M1. `PrivateAssets="all"` keeps it build-time-only, so the container image is unchanged. **Proven end to end**: all six migrations applied from the host against a clean volume, then the API returned `{"status":"Healthy","checks":{"api":"Healthy","database":"Healthy"}}`. |

### 16.6 Open items after M5

| # | Item |
| --- | --- |
| **N-22** | **E5-07's vendor-document UI does not exist.** N-20(c) asks for scope filtering on "both the vendor-upload and shipment-upload dropdowns" — but there is no vendor upload dropdown. All four vendor-document endpoints are live and permission-gated (`VendorDocumentsController`, plus `GET`/`POST /vendors/{id}/documents`), and §14.1 marked E5-07 **Done** on verification that is **entirely backend/HTTP** — the client has *zero* consumers of any of them (grep for `vendorDocument`/`docType` returns nothing). So E5-07 is Done as an API and absent as a feature. The scope-filtering half is already built and reusable (`documentTypeOptions('Vendor')` needs only a caller). **This is the same "closed slightly overstated" pattern §13.2 recorded for E4-07** and should be resolved the same way: either a small story to build the UI, or an explicit note that vendor documents are API-only by intent. **Resolved during consolidation: now carried as story `E5-10` in §4**, so the endpoint stays and the UI gets planned scope. Confirm against FSD Q5 before building — compliance documents are filed for reference with **no enforcement**, so this is upload/list/download and explicitly not a blocking-rules feature. |
| **N-24** | **CLOSED.** The admin document-type scope selector now starts empty with a "Choose…" first option and refuses to submit without a scope, restoring the stricter behaviour from the implementation dropped in consolidation. Two regression specs added, including the "issues no request" assertion — the point being that no silently `Vendor`-scoped row can reach the server, since a mis-scoped type can never back a shipment upload and cannot be re-scoped (D-34). |
| **N-25** | **A green `ng build` did not catch duplicate class members introduced by the consolidation merge.** `-X theirs` only auto-resolves *conflicting* hunks, so where both parallel passes added the same member in different regions the merge kept both — `isDocumentTypeTab` and `documentTypeScopes` were each declared twice in `admin-master-data.component.ts`, plus a duplicated import. **The development build passed anyway; only the test compile (TS2300) caught it.** Recorded because it generalises: a green dev build is not evidence a merge is sound, and it strengthens the N-12 audit case — the compile path that actually type-checks this project is the test path. |
| **N-23** | **CLOSED.** `appsettings.Development.json` now carries a clearly-labelled local-dev-only `Jwt:SigningKey`, so `dotnet run` no longer starts, connects, and then 500s on the first login with `IDX10703` — an error that reads like a credentials problem and is not. See **D-62** for why committing it is safe. |
| **N-16** | **CLOSED for M5's screens; still open for M4's.** The browser pass ran on 2026-07-31 against the consolidated tree (API from source on :5000, `ng serve` on :4300, live Postgres) and is recorded in §16.4. Inventory, Shipments, Shipment detail, New Lead Intake and Customer detail were all rendered and exercised. **M4's vendor/catalog/dispatch screens remain undriven** — the dispatch dialog especially, which the prototype calls one of the two things staff do from a phone. Do not read M5's closure as covering them. |
| **N-12** | **CLOSED, 2026-07-31.** The audit ran and found the defect class was **systemic in `DbSeederTests.cs`, not the single instance this item named** — eight round-trip assertions, the worst being the permission catalogue and the Associate role mapping. All rewritten to literals and **verified by mutation**: a one-character permission rename now fails 3 tests that previously passed. The rule is recorded as **D-64** so it survives the next contributor; the wire-contract half is **D-65**. **No live bugs were hiding** — every cross-boundary literal now agrees, so this was hygiene, not firefighting. Deliberately **not** done: rewriting client fixtures wholesale (already literals, and correct post-D-50), and pinning *labels* — those are Super-Admin-editable by design, so a test asserting them would be **wrong**, not merely weak. |
| **N-10** | **Reframed into a decision, because the previous framing is why two asks went unanswered.** The question is *not* "did M5 make us fat" — the baseline was **303.36 kB, already over 300 before M5 started**, so ~3.4 kB of the 4.87 kB overage predates this milestone and has been drifting since §12.6, which already recommended raising the budget. The real question is: **the 300 kB figure has been wrong for three milestones — what should it be?** Evidence, from a production build on 2026-07-31: initial total **304.87 kB raw / 87.50 kB estimated transfer**. The initial payload is almost entirely framework — `chunk-ENMMPHKQ` 139.06 kB, `chunk-56VNZ22W` 82.74 kB, polyfills 34.59 kB — against **`main` at just 4.68 kB**. Every feature screen is correctly lazy-chunked (inventory 27.50 kB, intake 19.53 kB, shipment-detail 16.51 kB, plus 20 more) and touches initial not at all. **There is nothing to cut without removing Angular or undoing D-56's atoms consolidation**, which exists to prevent future growth. **Recommendation: raise the initial warning to 320 kB with this rationale recorded**, leaving headroom for M6 invoicing and M7 analytics. Explicitly **not** recommended: declaring the budget advisory — TECH_SPEC §5.4 defines it and the frontend DoD carries "stays within its configured bundle budget" as a checked line, so voiding it silently weakens that check across every past and future UI story. **Ask the owner:** *300 kB was set at M1 before the app had features; it is now 304.87 kB across nine screens and has been over since M4. We propose 320 kB. Confirm, or give us a number.* Nothing is broken meanwhile — this is a **warning**, the error threshold is 500 kB, and the page actually ships 87.5 kB. |
| **N-19** | **Unchanged and still needs a business answer.** Stock-take correction has no path (a consequence of D-42). Now more visible, not less: the inventory screen renders an on-hand figure a user can see is wrong and has no way to correct. |
| **N-17** | **CLOSED, 2026-07-31.** Root cause was **in this repo, not the environment**: `docker-compose.override.yml` published `"5432:5432"`, and that file is auto-merged on every local `docker compose up`, so it collided with any machine running a native PostgreSQL service — which on Windows wins host connections, producing `28P01` against credentials that were correct. Fixed by publishing **`55432:5432`** on the host side only; the container still listens on 5432 internally, so `Host=db;Port=5432` inside the compose network is untouched. The local connection string moved to `appsettings.Development.json` beside the dev Jwt key (D-62 reasoning), and `stash@{0}`'s machine-specific edit was dropped as fully subsumed. **Note the password genuinely cannot be shared**: `POSTGRES_PASSWORD` initialises the volume on first run only, so each developer's database holds whatever their own `.env` said that day — the committed value is `.env.example`'s, correct for a fresh clone, and the file documents `ConnectionStrings__Default` as the override for anyone whose volume predates it. **E2-07 is no longer "Draft — never executed"** — see D-66; running it is what exposed the missing EF Design reference. |
| **N-18** | **Unchanged from §15.5.** D-37's `SHP-YYMM-NNN` collision-retry branch has still never fired. Cheap to close with a forced-collision test; worth doing before concurrent field use. |
| N-13, N-14, N-15 | **Unchanged.** N-15 is worth re-reading now that both screens exist: a role holding `Shipments.View` without `Inventory.View` sees shipment lines naming inventory items — the same latent mismatch N-15 records for vendors/catalogs. Still unreachable with the two seeded roles. |
| N-1, N-3, N-5, N-9, N-11 | **Unchanged.** |

### 16.7 Next

> **SUPERSEDED by §17.** The "do not start it yet" instruction below was overtaken on 2026-08-03: both blockers it names were resolved (DR-3 chosen, Q9c converted into a configurable settings row) and the M6 backend was built. §17.0 records that decision and its reasoning. The paragraph is left intact as the record of what the gate said at the time.

**M6 — Invoicing (E8). Do not start it yet.** It remains **fully gated on FSD Q9c**, and **DR-3** (no PDF library chosen) must be settled *before* it starts, not during it. M5 was its prerequisite and that prerequisite is now genuinely met: E8-01 can reference a real shipment, and D-30's snapshotted `unit_cost` on `shipment_lines` exists precisely so an issued invoice's basis cannot drift when an item price later changes.

**Two things worth doing before M6 rather than inside it**, both cheap now and awkward later:
1. **N-16** — get one browser pass over the M4 and M5 screens. It has slipped three passes, DR-5 explicitly warns against batching it into M8, and E0-05's invoicing screens will pile more unverified surface on top.
2. **N-12's suite-wide fixture audit** — §16.3 is direct evidence that green tests are not currently sufficient evidence of a working screen. M6 will be written against the same fixture conventions.

**Still needing the business owner:** FSD **Q9c** (gates M6 entirely), FSD **Q6 + Q8** (ask together), the **deployment track** (N-17), **N-10** (now 4.87 kB over budget), **D-18**, **N-19** (stock-take corrections), and newly **N-22** (is E5-07 meant to have a UI, or is it API-only by intent?).

---

## 17. M6 pass — Lightweight Invoicing backend (E8-01…E8-08)

**Last updated:** 2026-08-03. Same evidentiary standard as §9–§16: `Done` means an executed command backs it.

**M6 is 10 of 10 stories Done as of 2026-08-03.** E8-01…E8-08 landed as the backend pass below; E8-09/E8-10 landed in the screen pass recorded in §17.11. `mock-invoices.ts` is **deleted** — both screens read the live API.

> **One caveat on "Done", stated plainly: M6 meets its story list but NOT yet its §5 exit criterion in production terms.** The exit criterion requires an invoice to move Draft → Issued → Paid and download as a PDF. That is now demonstrable *and was demonstrated in a browser* — but only because this pass populated the company billing block with **placeholder values in a throwaway verification database**. With the real (empty) settings row, issuing is blocked by design. **FSD Q9c is the one thing standing between M6 and genuinely done** — see §17.7.

### 17.0 The §16.7 gate was crossed — deliberately, and here is the reasoning

§16.7 said **"M6 — Invoicing (E8). Do not start it yet."** It was started. The two blockers it named were resolved first, and this subsection exists so that decision is written down rather than inferred from the diff:

- **DR-3 (no PDF library chosen) is CLOSED.** The choice is **QuestPDF 2026.7.2**, referenced from `SourcingOps.Infrastructure` only. `QuestPDF.Settings.License = LicenseType.Community` is accepted **once**, in `Infrastructure/DependencyInjection.cs`, never scattered across call sites. It is a pure-managed library with no native binaries, which is what keeps it inside TECH_SPEC §7.2/§7.3's constraint C1 (single small VPS, minimal footprint). **Residual: the Community licence is revenue-gated — see N-26.**
- **FSD Q9c was not answered; it was made unnecessary as a blocker.** Rather than wait on the owner's GSTIN/address/bank values, this pass built the *mechanism*: `company_settings` is a Super-Admin-editable singleton exposed at `GET`/`PUT /admin/company-settings`, returned as an **all-null shape when unset** so "not configured yet" is representable without inventing placeholder values. Engineering is unblocked; **the business is not** — an invoice issued today renders with blank billing details. The exact values still owed are listed in §17.7.

**What was skipped, and should not be read as done:** §16.7's two "do these before M6" items. N-12's fixture audit *was* completed (commit `64a0e92`), but **N-16's browser pass over the M4 screens was not** — it remains open and now has invoicing screens queued behind it.

### 17.1 Story status

| ID | Status | Verification |
| --- | --- | --- |
| E8-01 | **Done** | `POST /invoices` creates against a customer with invoice number, date, line description, amount, tax, currency. `ShipmentId` is nullable — CIF invoices reference a shipment, freight-only stand alone — and when supplied is validated to belong to the same customer. `CreateInvoiceRequest` deliberately carries **no** `StatusId`, `InvoiceNumber` or `PdfFilePath`: all three are server-owned. 20 unit + 28 integration tests across E8-01…E8-07. |
| E8-02 | **Done** | `PUT /invoices/{id}/status` drives the configurable `invoice_statuses` lookup. The transition graph is enforced **by `Code`, never by the Super-Admin-editable `Label`** (D-50 precedent), via the new `InvoiceStatusCodes` constants. Every transition writes an `invoice_status_history` row and an audit entry. |
| E8-03 | **Done** | Issuing (DRAFT→ISSUED) renders the PDF via `IInvoicePdfRenderer`, stores it through `IFileStorage` at `invoices/{id}.pdf` — TECH_SPEC §4.6's stated convention — and sets `PdfFilePath`. `GET /invoices/{id}/pdf` streams it behind `Invoicing.View`. **`PdfFilePath` never appears in any DTO**; `HasPdf` (a bool) is what the client sees. |
| E8-04 | **Done** | Invoice creation and status changes appear on the E4-07 customer timeline, **read live** from `invoices`/`invoice_status_history` rather than duplicated into a timeline table. The creation-time DRAFT history row is skipped when emitting `InvoiceStatusChanged`, since `InvoiceCreated` already reports that same moment — otherwise one real event surfaces twice. |
| E8-05 | **Done** | `GET /invoices` filters by customer, status, service type and date range, searches invoice number or customer name, and pages (default page size 25). Returns `statusCounts[]` with **E7-08's semantics reused verbatim**: counts computed across the whole filtered set *excluding* the status filter itself, zero-count statuses included, ordered by the lookup's `sortOrder`. |
| E8-06 | **Done** | `dispatches` now carries a nullable `invoice_id` alongside a now-nullable `catalog_document_id`, with a DB `CHECK` constraint (`ck_dispatches_exactly_one_target`) enforcing exactly one target, mirrored by service-level validation. The invoice detail view can therefore reuse the M4 click-to-chat flow and have the send recorded in the dispatch log. **See D-67 — this reuses the `CatalogDispatched` timeline kind rather than adding `InvoiceDispatched`, and that needs a decision.** |
| E8-07 | **Done** | `POST /invoices/{id}/mark-paid` behind its own `Invoicing.MarkPaid` policy. `PaidAt` defaults to now when omitted, `PaidReference` is optional free text. **Only reachable from ISSUED** — 409 otherwise. Deliberately *not* a value on `ChangeInvoiceStatusRequest`, so the separate permission cannot be bypassed through the general status endpoint. |
| E8-08 | **Done, mechanism only** | `AdminCompanySettingsController` (`GET`/`PUT /admin/company-settings`) over the pre-existing `company_settings` singleton. `GenerateInvoiceNumberAsync` produces `{PREFIX}-{YYMM}-{NNN}`, with the prefix read from `CompanySettings.InvoiceNumberPrefix`. **The numbering format and every billing value are still unset — §17.7.** |
| E8-09 | **Done** (2026-08-03) | Invoicing list screen, live against `GET /invoices`. Server-side search/customer/status/service-type/date filtering and paging — every filter change issues a fresh request. Status **pill tabs driven by `statusCounts`** (D-72). Chips via `StatusStyleService` on `code`. **Browser-verified:** four seeded invoices render, the Draft tab filters to one, and the other tabs' counts stay unchanged — confirming E7-08's exclude-the-active-filter semantics in the UI. |
| E8-10 | **Done** (2026-08-03) | Generate/detail screen on `/invoices/new` and `/invoices/:id`. Lifecycle-driven actions, PDF download, Mark Paid dialog, company-settings gate. **Browser-verified end-to-end:** created INV-2608-005 through the form; drove INV-2608-002 Draft → Issued → Paid, watching Edit/Issue disappear and Download PDF/Mark Paid enable at the right moments. Disabled actions carry explanatory tooltips rather than being silently inert. |

**All three permissions (`Invoicing.View`, `Invoicing.Edit`, `Invoicing.MarkPaid`) were already seeded** from M2, so this pass required no RBAC migration and no seed change.

### 17.2 The status lifecycle (E8-02)

Enforced in `InvoiceService` by `Code`:

| From | To | Allowed |
| --- | --- | --- |
| *(create)* | DRAFT | Server-side only — `CreateInvoiceRequest` has no `StatusId`, and the table below has no "→ DRAFT" arrow, so DRAFT is unreachable by transition. |
| DRAFT | ISSUED | Yes — **this is the transition that renders and stores the PDF.** |
| DRAFT | CANCELLED | Yes |
| ISSUED | CANCELLED | Yes |
| ISSUED | PAID | **Not through this endpoint.** `POST /{id}/mark-paid` only, which carries `Invoicing.MarkPaid`. |
| anything else | — | 409 `InvoiceConflictException` |

An invoice is **editable only while DRAFT** (`PUT /invoices/{id}` returns 409 otherwise), which is the `UpdateShipmentRequest` precedent from D-42/D-43.

### 17.3 The API contract

Reads require `Invoicing.View`, general writes `Invoicing.Edit`, marking paid `Invoicing.MarkPaid`. `/admin/company-settings` is Super-Admin.

| Method | Route | Notes |
| --- | --- | --- |
| GET | `/invoices` | `?search=&page=&pageSize=&customerId=&statusId=&serviceTypeId=&fromDate=&toDate=` → `InvoiceListResultDto { items[], page, pageSize, totalCount, statusCounts[] }` |
| GET | `/invoices/{id}` | `InvoiceDetailDto` — list row + `lineDescription`, creator, `paidReference`, full `statusHistory[]`. One call, no second round trip. |
| POST | `/invoices` | `CreateInvoiceRequest` → 201 `InvoiceDetailDto`, always DRAFT |
| PUT | `/invoices/{id}` | `UpdateInvoiceRequest` → 200, **409 if not DRAFT** |
| PUT | `/invoices/{id}/status` | `ChangeInvoiceStatusRequest { statusId, note? }` → 200, **409 on an illegal transition** |
| POST | `/invoices/{id}/mark-paid` | `MarkInvoicePaidRequest { paidAt?, paidReference? }` → 200, **409 unless ISSUED** |
| GET | `/invoices/{id}/pdf` | file stream, filename `{invoiceNumber}.pdf`, **409 if never issued** |
| GET | `/admin/company-settings` | `CompanySettingsDto` — all-null when unset |
| PUT | `/admin/company-settings` | `UpsertCompanySettingsRequest` — upsert; blank strings trim to null |

**Wire-shape rules the screens must build against** (M4/M5 convention, carried forward):
- Lookups are **embedded resolved objects** (`CustomerRefDto`, `StatusRefDto`), never bare ids.
- `serviceType` resolves from the **customer's** service type, not the shipment's — a freight-only invoice has no shipment at all.
- `totalAmount` is **computed** (`amount + taxAmount`), never stored.
- `invoiceDate` is a bare date string (`DateOnly`); `paidAt` is a full UTC instant.
- `hasPdf` is a bool; the storage path is never serialised.

### 17.4 Schema — migration `20260801115938_AddM6InvoicingSchema`

The `invoices` and `company_settings` tables **already existed**, created by the pre-M3 deferred-schema migration `20260727125250`. This migration only completes them:

- `invoices` += `paid_at` (timestamptz, null), `paid_reference` (varchar 300, null)
- new table `invoice_status_history` (id, invoice_id, status_id, changed_by_user_id, changed_at, note) — cascade on invoice, restrict on status and user; indexed `(invoice_id, changed_at)`
- `dispatches.catalog_document_id` **altered to nullable**, += `invoice_id` (FK, indexed)
- new `CHECK ck_dispatches_exactly_one_target` — exactly one of `catalog_document_id` / `invoice_id` is non-null

### 17.5 Verified on this machine, this pass — 2026-08-03

| What | Command | Result |
| --- | --- | --- |
| Solution builds | `dotnet build SourcingOps.sln` | **Clean — 0 warnings, 0 errors** |
| Full backend suite | `dotnet test SourcingOps.sln` | **630 passed, 0 failed** — 373 unit + 257 integration. Up from 557 at §16. |
| Migration applies from scratch | implied by the above | The integration suite runs against a **fresh `postgres:16-alpine` Testcontainer** and `Program.cs` calls `MigrateAsync()` on startup, so all 257 integration tests execute against a database built by applying every migration in order to an empty volume. **This is stronger evidence than N-17's apply-SQL-inside-the-container workaround**, and it is the same tool path a real deploy uses. |

~~**Not verified, and not claimed:** no live HTTP pass over the new endpoints, and no browser pass (there is no UI yet). **This pass reverted to the test-evidence-only pattern of N-9/N-11** that §13.3 deliberately broke for M4.~~ **Superseded — the live pass was run on 2026-08-03 as the opening move of the screen pass; see §17.10.** It found one thing 631 green tests did not: the Q9c issue-time hard block (§17.7's correction).

### 17.6 Deviations and additions from this pass

| # | Deviation |
| --- | --- |
| **D-67** | ~~**E8-06 reuses the `CatalogDispatched` timeline kind for invoice dispatches instead of adding `InvoiceDispatched`.**~~ **RESOLVED 2026-08-03, before the screen pass — a real `InvoiceDispatched` kind was added on both sides.** Three findings decided it, and two of them correct the framing above. **(1) The original risk was overstated:** `Title`/`Body` are **server-authored** (the invoice branch already sent "Invoice sent" / "Sent invoice {n} via WhatsApp"), so no client was ever going to *display* an invoice send as a catalog send. **(2) `kind` is not stored** — it is computed at read time in `MapDispatchTimelineEvent`, so **no migration was required**; the change was a const, a client union member, one colour-map entry and two tests. **(3) The real cost of leaving it was forward-looking:** `TimelineEventDto` deliberately carries no colour field, making `kind` the client's *only* semantic handle on an event, and **E10's aggregates will group by `kind`** — a shared kind would have made invoice sends permanently uncountable apart from catalog sends, surfacing as a quietly wrong dashboard number in M7 rather than a visible bug now. **The two kinds share `#2d5be3` deliberately**: both are "sent via WhatsApp", the prototype has no separate row for an invoice send, and inventing a colour would breach DESIGN_TOKENS. Distinct kinds, shared colour. **A regression test pinned both halves and was mutation-verified** — reverting the mapper fails it (D-64's literal-assertion rule; the test asserts the string `"InvoiceDispatched"`, not the constant, so repointing the constant cannot hide the regression). |
| **D-68** | **The migration's `Down` is deliberately lossy, and says so.** An invoice-targeted dispatch is *unrepresentable* in the pre-M6 schema, which requires a non-null `catalog_document_id`. Reverting therefore cannot preserve those rows. This is a real asymmetry with every prior migration in this repo, all of which round-trip cleanly — do not assume M6 is reversible in the way §15's were. |
| **D-69** | **`InvoiceConflictException` → 409, joining `InsufficientStockException` in `ExceptionHandlingMiddleware`.** Same reasoning as M5's: the request is well-formed and the caller may legitimately retry once the invoice's state allows it, so 409 rather than 400. Covers all three conflict cases — editing a non-DRAFT invoice, an illegal transition, and requesting a PDF for an invoice that was never issued. |
| **D-70** | **`company_settings` returns an all-null DTO rather than 404 or seeded placeholders when unset**, and `PUT` trims blank strings to null, making "cleared in the form" and "never set" indistinguishable. Correct for a row a Super Admin fills in incrementally, and it is the specific design choice that let Q9c stop being an engineering blocker without anyone inventing a GSTIN. |
| **D-71** | **Invoice numbering is `{PREFIX}-{YYMM}-{NNN}`, max-plus-one within the month prefix**, mirroring D-37's `SHP-YYMM-NNN`. It inherits D-37's collision-retry design **and its weakness** — see N-18/N-26. |
| **D-72** | **§E0-05a's two money stat-tiles ("total issued" / "total outstanding") were NOT built, and the status filter became pill tabs rather than a `<select>`.** Both are departures from the approved design, both deliberate, and the design is the older artifact in each case. **The tabs** follow the live `statusCounts` contract and match the shipments screen precedent (E7-12) — the design predates that contract existing. **The tiles are the one that matters:** the API returns per-status **counts only, no amount sums**, so an honest total would require fetching every matching invoice client-side, and a total computed from the current page would be quietly wrong — the worst of the three options. Left out rather than faked. **This is a real, approved design element that the screen does not have** — recorded here rather than allowed to lapse silently, which is the E4-07/E5-07 "closed slightly overstated" pattern §13.2 and N-22 both record. See **N-31** for the fix. |

### 17.7 What the business owner still owes — FSD Q9c, itemised

> **CORRECTION, 2026-08-03 (live pass).** This section previously said an invoice issued without these values "renders with blank billing details". **That is wrong, and the truth is more serious: an invoice cannot be issued at all.** `InvoiceService.RenderPdfAsync` fails loudly when `CompanySettings` is absent or `LegalEntityName`/`RegisteredAddress` is null, so `DRAFT → ISSUED` returns **400** with an `errors.companySettings` message. Since issuing is also what renders the PDF, and PAID is only reachable from ISSUED, **every downstream step is blocked**. Confirmed live: the transition returned 400 until the settings row was populated, then succeeded and produced a 42 KB PDF.
>
> **This means M6 cannot meet its own §5 exit criterion without Q9c** ("an invoice … moves Draft → Issued → Paid, downloads as a PDF"). Q9c is not a cosmetic gap or a polish item — it is the last hard blocker on the milestone. **This is exactly the class of thing test evidence did not surface** (the tests configure settings in their fixtures, so the gate never fired) and the live pass did — see N-27.

The mechanism is built and editable at `PUT /admin/company-settings`. Only the first two are strictly required to unblock issuing; the rest shape what the PDF actually says. Each maps to exactly one field:

| Field | What is needed |
| --- | --- |
| `legalEntityName` | **REQUIRED to issue.** The registered legal name to print as the issuer — not the trading name, if they differ. |
| `registeredAddress` | **REQUIRED to issue.** Full registered address as it should appear on a tax invoice. |
| `gstin` | The 15-character GSTIN. |
| `bankAccountName` | Account holder name as per bank records. |
| `bankAccountNumber` | Account number. |
| `bankIfsc` | IFSC code. |
| `bankBranch` | Branch name. |
| `invoiceNumberPrefix` | The prefix for `{PREFIX}-{YYMM}-{NNN}`. **Also confirm the format itself** — is `YYMM` + a 3-digit monthly sequence acceptable, or is a continuous annual/financial-year sequence required? Some accountants require the latter. **Changing this after invoices exist is disruptive**, so it is worth settling before first production use. |
| `declarationText` | The standard declaration/footer line, if their accountant requires one. |

### 17.8 Open items after M6

| # | Item |
| --- | --- |
| **N-32** | **The invoice UI cannot link a CIF invoice to a shipment.** `CreateInvoiceRequest.shipmentId` is always sent as `null` — no shipment picker was built. The API fully supports the link, E8-01 explicitly calls for it ("optionally referencing a shipment (CIF)"), and **D-30 snapshotted `unit_cost` on `shipment_lines` specifically so an issued invoice's basis could not drift** — that whole design is currently unreachable from the UI. A CIF invoice is therefore indistinguishable from a freight-only one in practice. **The smaller half of the same gap:** the list/detail DTOs already carry `shipmentId`/`shipmentReference`, so the read side is ready and only the picker plus a display line are missing. Should be fixed with N-31 in the next M6 pass. |
| **N-33** | **Login does not honour `returnUrl`.** Hitting a deep link while unauthenticated correctly redirects to `/login?returnUrl=…`, but after signing in the app lands on `/dashboard` and the target is dropped. **Pre-existing and not M6's** — it affects every deep link in the app, and is the kind of thing only a browser pass surfaces. Small, and it matters most for exactly the workflow this business has: someone pasting a link to a specific invoice or customer into WhatsApp. |
| **N-31** | **The invoice list has no money totals, because the API has no field for them (D-72).** §E0-05a's approved design calls for "total issued" and "total outstanding" tiles. **Recommended fix: add summed amount fields alongside `statusCounts` in `InvoiceListResponse`** — the aggregate must be computed server-side over the whole filtered set, exactly as the counts already are, because that is the only place the full set exists. Small (one grouped query in `InvoiceService.ListAsync`, extending a shape the client already consumes) and cheap to do while the contract is fresh. **Deliberately not built inside the screen pass** to avoid widening it mid-flight, but it should be the first item of the next M6 pass, not left to M8. |
| **N-29** | **The rendered invoice carries the seller's GSTIN but has nowhere to put the BUYER's — customers have no GSTIN field at all.** Confirmed by inspecting a real generated PDF and by grepping the customer entity/DTOs. For a GST-registered business issuing B2B invoices to Indian buyers, the recipient's GSTIN is normally required on a tax invoice, and without it the buyer generally cannot claim input credit. **This is a business/compliance question, not a bug** — FSD §6.8 scopes invoicing as "lightweight" and document-based, and A9 explicitly forbids anything resembling reconciliation, so adding a field unasked would be scope creep. **Ask the owner together with Q9c**, since it is the same conversation: *do your invoices need to carry the customer's GSTIN?* If yes it is a customer-schema change plus a PDF change, and is much cheaper before first production use. |
| **N-30** | **Money on the PDF renders unformatted — `INR 125000.00`, no digit grouping — and this contradicts the app's own established convention.** `shared/utils/money.util.ts` already formats every on-screen amount through `Intl.NumberFormat('en-IN', …)`, giving `₹1,47,500.00` with lakh grouping, and it exists precisely to keep that call in one place. **So this is not an open question about what format to use; it is an inconsistency** — the same invoice reads one way in the UI and another in the PDF the customer receives. Fixing it means teaching the server the same grouping (`CultureInfo("en-IN")`), not deciding a policy. **It is not only the PDF** — the browser pass found the same raw formatting in the server-authored customer-timeline bodies (`"Invoice INV-2608-001 created for INR 147500.00."`), which render beside client-formatted amounts elsewhere in the app. So the rule is: **every money string composed server-side bypasses `money.util.ts` and is therefore ungrouped.** Two known sites (`QuestPdfInvoiceRenderer`, `CustomerService`'s timeline bodies); worth grepping for more. Observed on a live-generated PDF and in the browser. |
| **N-26** | **The QuestPDF Community licence is revenue-gated and nobody has checked the threshold against this business.** QuestPDF Community is free below a stated annual-revenue ceiling; above it a paid licence is required. This is a **commercial** question, not an engineering one, and DR-3 was closed on the technical merits alone. Cheap to answer now, expensive to discover at launch. |
| **N-18** | **Now doubled, and still never fired.** D-37's `SHP-YYMM-NNN` collision-retry branch is joined by D-71's identical branch for invoice numbers. Neither has ever executed under test. One forced-collision test would close both. Worth doing before concurrent field use — two staff issuing invoices in the same minute is an ordinary Tuesday, not an edge case. |
| **N-27** | **M6 was closed on test evidence alone.** No live HTTP pass; §13.3's clean-volume `docker compose` standard was not applied. The suite is genuinely strong here (630 tests, migration proven from an empty volume), but §16.3 is direct evidence in this very repo that green tests can encode the same wrong assumption the code does. Fold a live pass into the E8-09/E8-10 pass, where a contract diff is required anyway. |
| **N-28** | **The M6 handoff contract was written outside the repo and lost.** Production XML doc comments across `InvoiceDtos`, `InvoiceService`, `InvoicesController`, `CompanySettingsDtos` and `InvoiceStatusCodes` cite an **"M6 contract §0/§1/§2/§3/§4/§5"** that exists nowhere on disk — it lived in a session that was cleared. §15.3 was written into this document specifically so M5's contract would survive its session; that lesson was not carried forward. **§17.2/§17.3/§17.4 above are a reconstruction from the code**, and the citations in the source now resolve to them only approximately. **Rule: any contract the source code cites must live in this document before the session ends.** |
| **N-16** | **Unchanged and now more urgent.** Still open for M4's screens, and §16.7 asked for it *before* M6. It slipped again. E8-09/E8-10 will add two more unverified screens on top. DR-5 warns explicitly against batching this into M8. |
| **N-10** | **Unchanged, and M6's screens will make it worse.** Still awaiting a number from the owner; the recommendation remains 320 kB. The backend pass did not move the bundle at all. |
| N-1, N-3, N-5, N-9, N-11, N-13, N-14, N-15, N-19, N-25 | **Unchanged.** N-19 (stock-take corrections) and N-15 (permission-scope mismatch) still need business answers. |

### 17.9 Next

**E8-09 and E8-10 — the two invoicing screens — are the whole of what remains in M6.** They are no longer gated on E0-06 (closed 2026-07-27 by owner authorisation) and the E0-05 design exists in `SCREEN_DESIGNS.md`, so the path is clear.

Three things the screen pass must do, all learned the hard way in earlier passes:
1. **Diff §17.3 against a live response before wiring any component** — the §15.6 gate, which caught D-50 in M5 when no test could have.
2. **Settle D-67 first.** If the timeline should distinguish an invoice send from a catalog send, that is a two-sided change and belongs at the start of the pass, not retrofitted.
3. **Replace `mock-invoices.ts` entirely, and delete it.** Leaving it beside a live service is exactly how a screen ends up half-wired and passing its own tests — the E5-07/N-22 shape.

**Still needing the business owner:** **FSD Q9c's nine values (§17.7)**, **N-26** (QuestPDF licence revenue check), FSD **Q6 + Q8** (ask together), the **deployment track** (N-3), **N-10** (bundle budget number), **D-18**, **N-19**, and **N-22**.

### 17.10 The live API pass (the §15.6 gate, run before any component was wired)

Run by the coordinator, first-hand, **before** the screen brief was written and before any build agent was launched — the sequence §16.2 established and §17.9 required. Against a **fresh, empty database** (`sourcingops_ui`, created for this pass so no prior state could mask a defect) with the API run from source on `:5000` and migrations applied by `Program.cs` at startup.

| # | Checked | Result |
| --- | --- | --- |
| 1 | `GET /health` | `Healthy`, database `Healthy` |
| 2 | Bootstrap admin login + forced password change | 200; `mustChangePassword` flips false on re-login |
| 3 | `GET /master-data` | `invoiceStatuses` seeded `DRAFT`/`ISSUED`/`PAID`/`CANCELLED` with sortOrder 1–4 |
| 4 | `POST /invoices` (freight-only, no shipment) | 201. Number **`INV-2608-001`** generated; status DRAFT; `statusHistory` has exactly one row, note `"Invoice created."` |
| 5 | `GET /admin/company-settings` unset | **200 with every field null** — D-70 confirmed, not a 404 |
| 6 | `GET /invoices/{id}/pdf` while DRAFT | **409** `"This invoice has not been issued yet…"`, `currentStatus: DRAFT` |
| 7 | `POST /invoices/{id}/mark-paid` while DRAFT | **409** `"can only be marked paid from 'ISSUED'…"`, `fromStatus`/`toStatus` present |
| 8 | `PUT /invoices/{id}/status` → ISSUED, **settings unset** | **400** `errors.companySettings` — **the finding of this pass; see §17.7's correction** |
| 9 | `PUT /admin/company-settings` then retry (8) | 200; status ISSUED, `hasPdf` true, history 2 rows |
| 10 | `GET /invoices/{id}/pdf` after issue | 200, **42,508 bytes, `%PDF-` magic**, `Content-Disposition: attachment; filename=INV-2608-001.pdf` |
| 11 | PDF rendered content | Opened and read. Correct issuer block, Bill-To, description, tax, total, bank block, declaration, "Page 1 of 1". Raised **N-29** (no buyer GSTIN) and **N-30** (no digit grouping) |
| 12 | `PUT /invoices/{id}` while ISSUED | **409** `"This invoice is 'ISSUED' and can only be edited while Draft."` |
| 13 | `POST /invoices/{id}/mark-paid` from ISSUED | 200; PAID, `paidAt` set, `paidReference` `"NEFT ref 88213"`, history 3 rows |
| 14 | `GET /invoices` list | `statusCounts` `[(DRAFT,0,1),(ISSUED,0,2),(PAID,1,3),(CANCELLED,0,4)]` — **zero-count statuses included and sortOrder-ordered**, E7-08 semantics confirmed |
| 15 | `POST /dispatch-log` with `invoiceId` → customer timeline | `InvoiceDispatched` / `refType=Invoice` — **D-67's fix confirmed end-to-end** |
| 16 | Timeline event set | `InvoiceDispatched`, `InvoiceStatusChanged` ×2, `InvoiceCreated`, `EnquiryCaptured`. The creation-time DRAFT row is correctly **not** duplicated as a status change (E8-04) |

**The contract in §17.3 was found accurate** — no field renames, no shape surprises. Two live details were nonetheless worth writing into the client models because they are invisible in the written contract and easy to assume wrong: `invoiceDate` is a **bare date string** while `paidAt`/`createdAt`/`changedAt` are **full instants** (both shapes in one DTO), and `customer.name` on the embedded ref already resolves to the **business name** where one exists.

**Two machine-level traps cost real time and are worth recording** (both sibling to N-17/N-13):
1. The running `china_m2c-db-1` volume's password diverges from `.env.example` (it was initialised on an earlier day), so the API 500s on startup with `28P01`. The fix is the one `appsettings.Development.json` already documents — export `ConnectionStrings__Default` for the run.
2. **`BOOTSTRAP_ADMIN_PASSWORD` in the environment is NOT read by the API.** It is a docker-compose-level variable name; the .NET config key differs, so setting it silently has no effect and the seeder still reports `Source: generated`. The generated password is printed **once**, so a container whose volume predates the current session has an unrecoverable admin login (N-13). Creating a **fresh database** rather than mutating the existing one is the clean way out, and has the side benefit of proving the migrations apply from empty.

### 17.11 The screen pass — E8-09/E8-10, and the browser pass N-16 asked for

**Sequence, deliberately:** D-67 settled and committed first → live API pass (§17.10) → the cross-cutting client contract written by the coordinator → only then two build agents launched in parallel with **disjoint file ownership** → coordinator-owned integration, routes, mock deletion and git. This is the M5 consolidation lesson applied: the two parallel M5 passes collided because nobody fixed the shared contract before launching them.

**Suites: 631 backend (373 unit + 258 integration) and 303 frontend, both green.** `mock-invoices.ts` deleted.

#### What the browser pass actually showed

Run against `ng serve` on :4300 proxying the API on :5000, against the fresh `sourcingops_ui` database seeded with four invoices — one in each status.

| Checked | Result |
| --- | --- |
| Auth guard on `/invoices` | Redirects to login with `returnUrl`. **But the returnUrl is not honoured** — after signing in it lands on `/dashboard`. Pre-existing, not M6; recorded as **N-33** |
| List renders live data | Four invoices, correct numbers/customers/dates/chips, money as `₹1,47,500.00` |
| Status tabs | `All 4 / Draft 1 / Issued 1 / Paid 1 / Cancelled 1` |
| Filtering | Draft tab → one row, **and the other tabs' counts did not change** — E7-08's exclude-the-active-filter semantics confirmed in the UI, not just in a test |
| Detail, DRAFT | Download PDF disabled *"This invoice has not been issued yet…"*; Mark Paid disabled *"Only issued invoices can be marked paid."*; Edit/Issue/Cancel enabled |
| **Company settings cleared** | FROM card becomes a **"Company billing details not configured"** panel naming the two missing fields and where to set them; **Issue disabled** with the reason in its tooltip. The screen reads as unfinished-by-configuration, not broken — which is exactly what E0-05b asked for |
| Settings restored → Issue | Status → Issued **live, without a reload**; Edit and Issue disappear; Download PDF and Mark Paid enable; trail advances |
| Mark Paid | Dialog collecting date + optional reference, captioned *"No gateway, no reconciliation (FSD A9)"* — the A9 constraint stated in the UI itself |
| Full lifecycle | Draft → Issued → Paid driven entirely through the browser |
| Generate mode | Invoice number shows **"Not yet assigned — assigned automatically by the server"**, correctly refusing to invent the format §E0-05b's stale text still describes. Created **INV-2608-005** through the form |
| Customer timeline | `Invoice created` / `Invoice status changed` / **`Invoice sent`** all render, the last with CatalogDispatched's dispatch-blue dot — **D-67 confirmed end-to-end in a browser** |
| **N-16 (M4 screens)** | **Partially closed.** Vendors list and vendor detail rendered, including E5-10's Compliance Documents section (so `e60417c` is visually confirmed). **The dispatch dialog was still not driven** — it remains the single highest-value unverified surface, and the prototype calls it one of the two things staff do from a phone. N-16 stays open for that alone |

#### The defect the browser pass caught, that 934 green tests did not

The detail screen showed **"Marked paid on 03 Aug 2026 · 05:30"** — a time the user never entered. Cause: the Mark Paid dialog collects a **date**, so the server stores `2026-08-03T00:00:00Z`, and the view rendered that instant in local time (IST, +5:30). Fixed by rendering `paidAt` with the UTC, date-only `formatInvoiceDate` instead of `formatTimelineDate`; the genuine wall-clock moment of the transition is still on the PAID status-history row, where it belongs.

**No test could plausibly have caught this** — the value was correctly stored, correctly transmitted, and correctly parsed. It was only wrong *once rendered in a timezone*, which is exactly the class of defect §16.3 recorded for M5 and the reason N-27 exists. **It is now two milestones running where the browser pass found something the suite structurally could not.**

#### Deviations from the approved design, both recorded not smoothed

**§E0-05b is stale in three places** because it was written before the backend existed, and the live behaviour won each time: the invoice number is server-generated (not a Q9-blocked placeholder); "send via WhatsApp" now *could* be wired but was deliberately left inert and out of scope; and the "From" empty state, which the design called for speculatively, turns out to be **load-bearing** rather than cosmetic — it is the UI for a hard block.

**D-72** records the two E8-09 departures (status pill tabs instead of a `<select>`; the two money stat-tiles omitted). **N-31** is the fix for the tiles: sum server-side beside `statusCounts`.

#### Assumptions the build agents made, verified by the coordinator rather than taken on trust

- `Invoicing.Edit` as the create-button permission — **checked against `PermissionCodes.cs`; correct.**
- `shipmentId` is always sent as `null` on create, because no shipment picker was in scope. **This is a real functional gap, not a nit:** E8-01 explicitly allows a CIF invoice to reference a shipment, and D-30's snapshotted `unit_cost` exists precisely so an issued invoice's basis cannot drift. The API supports it; the UI cannot reach it. Recorded as **N-32**.
- Currency hard-coded to `INR`, per `money.util.ts`'s documented single-currency assumption — no currency picker invented.

---

## 18. M6 residual pass — N-31, N-32, N-30

**Why this pass exists rather than going straight to M7.** §17.8 named N-31 as "the first item of the next M6 pass, not left to M8", and N-32 is worse than a nit: **M6's own §5 exit criterion requires "an invoice is generated against a CIF shipment", and no UI path existed to create one.** Closing both here means M6's exit criterion stops being overstated — the E4-07/E5-07 "closed slightly overstated" pattern §13.2 and N-22 record. N-30 was folded in because it lives in the same contract and is a one-place fix.

**Shape:** the coordinator settled the contract extension first, then two build agents ran in parallel on **disjoint file trees** (`src/` and `tests/` vs `client/`), with the coordinator owning integration, the contract, git and the seam checks. This is §17.11's sequence repeated because it worked.

### 18.1 Story status

| # | Item | Status |
| --- | --- | --- |
| **N-31** | Money totals on the invoice list | **Closed.** Per-status amount sums added server-side; the two §E0-05a stat-tiles now exist. |
| **N-32** | Shipment picker (CIF link unreachable from the UI) | **Closed.** Optional, customer-scoped picker on the invoice form; `shipmentId` is now really sent. **No backend change was needed** — `ShipmentsService.list({ customerId })` and the nullable `shipmentId` on both requests already existed, so this was pure UI. |
| **N-30** | Server-composed money strings bypass the app's formatting | **Closed.** One shared `MoneyFormatter`, both known sites routed through it, and a grep confirmed there is no third site. |

### 18.2 The contract extension

`InvoiceStatusCountDto` gains **one** appended field:

```csharp
public sealed record InvoiceStatusCountDto(Guid StatusId, string Code, string Label, int SortOrder, int Count, decimal TotalAmount);
```

`TotalAmount` is the sum of `Amount + TaxAmount` per status, computed in the **same grouped query** as the counts (one round trip, not two) and — critically — over the same **status-excluded** filtered set, so E7-08's semantics hold for money exactly as they already did for counts: selecting a status tab does not move the other statuses' figures.

The client derives the two tiles from it, by `Code` and never by `Label` (D-50):

| Tile | Derivation |
| --- | --- |
| Total issued | `ISSUED` + `PAID` |
| Total outstanding | `ISSUED` |

### 18.3 Deviations and additions from this pass

| # | Deviation |
| --- | --- |
| **D-73** | **Per-status sums, not bespoke `totalIssued`/`totalOutstanding` fields.** The obvious shape was two scalars on `InvoiceListResultDto`. Rejected: per-status sums reuse the counts' exact query, zero-fill and ordering, and — the deciding reason — **keep the server from hard-coding a business definition of "outstanding"**. Which statuses roll into which tile is a labelling decision the owner has not confirmed (**H-14**); with per-status sums that decision is a one-line client change instead of an API change. Zero-count statuses zero-fill (`Count = 0`, `TotalAmount = 0m`) rather than being omitted or null. |
| **D-74** | **The two money tiles render at FULL precision (`₹3,50,000.00`), NOT through `formatInrCompact`.** The build agent used the compact form, reasoning from the inventory screen's On-Hand Value tile, and flagged it as a judgement call rather than burying it — correctly, because **the precedent does not transfer**. `money.util.ts` justifies the compact helper as a *1:1 port of a prototype tile that literally reads `₹41.2 L`*; the invoicing screens are net-new from the E0 pass, so there is no prototype tile here to be faithful to, and §E0-05a is silent on precision. **The deciding difference is what the number is:** on-hand stock value is an indicative aggregate, but total outstanding is **receivables** — reconciled against a bank statement. The compact form rounds to one decimal in lakhs, so `₹1,47,500` and `₹1,52,400` both render `₹1.5 L`: a ±₹5,000 band on money owed, which reads as simply wrong to whoever knows the real figure. Overridden by the coordinator; the reasoning is in the computed's doc comment so it is not "fixed" back to match inventory later. |
| **D-75** | **`MoneyFormatter` pins `NumberGroupSizes = [3, 2]` explicitly rather than trusting `en-IN`'s ICU data.** Plain `CultureInfo("en-IN")` was verified on this machine to already group 2,2,3 correctly — but the API also runs in `postgres`-adjacent containers and CI, where ICU data can differ, and a silently-wrong grouping on a customer-facing invoice is not a failure that announces itself. Pinning removes the dependency. |
| **D-76** | **The PDF and timeline keep the ISO code (`INR 1,47,500.00`), not the `₹` glyph the UI uses.** Deliberate, and it means the two surfaces still differ by prefix even after N-30. The ISO code is the correct label on a tax invoice, and QuestPDF's default font is not guaranteed to carry `₹` — a missing glyph renders as a blank box on a document sent to a customer. **Grouping was the whole of N-30's complaint**; the prefix difference is intended. |

### 18.4 The seam the parallel agents could not test, and what it caught

Both agents' suites were green, and each had genuinely tested its own half. The seam between them was still unverified, and the check found a real hole — **in the test, not the code**.

The new endpoint test asserted the wire contract via `ReadFromJsonAsync<InvoiceListResultDto>()`. That **round-trips through the same serializer on both ends**, so a camelCase/PascalCase mismatch between the API and the Angular client would have been completely invisible to it: the client reads `totalAmount` off untyped JSON and would have silently seen `undefined`, rendering `₹0.00` tiles that look like a legitimately empty month. Exactly the §16.3 class of defect — value computed, stored and transmitted correctly, wrong only where a *different runtime* reads it.

Closed by asserting the **raw JSON property name** before deserialising. The wire name is confirmed `"totalAmount"`.

> **Rule worth carrying:** a test that both writes and reads through the same serializer proves the value, never the wire name. Where a different runtime consumes the field, pin the raw string.

### 18.5 Verified on this machine, this pass — 2026-08-03

| What | Command | Result |
| --- | --- | --- |
| Solution builds | `dotnet build SourcingOps.sln` | **Clean — 0 warnings, 0 errors** |
| Full backend suite | `dotnet test SourcingOps.sln` | **643 passed, 0 failed** — 384 unit + 259 integration. Up from 631 at §17. |
| Frontend suite | `ng test --watch=false --browsers=ChromeHeadless` | **313 passed** — up from 303. Re-run by the coordinator after the D-74 override, not taken from the agent's report. |
| Raw wire name | targeted integration test | `"totalAmount":` present in the live JSON body |
| Mutation verification | manual, both halves | Breaking the sum expression failed 2 tests; reverting `MoneyFormatter` to `0.00` failed 4. Both restored and re-run green. |

**Not verified, and not claimed:** no live HTTP pass over a running API and **no browser pass** — the standing instruction is that browser verification happens only on explicit request. §17.11's browser pass found a real defect, and §18.4 above is a reminder that the seams are where they hide; that trade is recorded as **H-10**, accepted rather than forgotten.

### 18.6 Open items after this pass

| # | Item |
| --- | --- |
| **H-14** (new) | **The two tiles' meaning is assumed, not confirmed.** Shipping as issued = `ISSUED + PAID`, outstanding = `ISSUED`. The alternative reading — issued meaning currently-unpaid — would make both tiles identical, which is why this reading was chosen, but it is a cash figure the owner reads at a glance. D-73 deliberately made this cheap to change. |
| **N-33** | **Unchanged.** Login still drops `returnUrl`, so every deep link lands on `/dashboard`. Pre-existing, affects the whole app, and matters most for this business's actual habit of pasting links into WhatsApp. |
| **N-16 / H-9** | **Unchanged.** The WhatsApp dispatch dialog has still never been driven — now five passes. The prototype calls it one of the two things staff do from a phone. |
| **N-18** | **Unchanged.** Both collision-retry branches (`SHP-` and `INV-`) have still never executed under test. One forced-collision test closes both. |
| **N-26 / H-4** | **Unchanged.** QuestPDF Community's revenue ceiling still unchecked against this business. |
| — | **`docs/HUMAN_TASKS.md` is deliberately NOT tracked, and this is not an oversight.** Committed once as `4620947` (a historical snapshot of H-1…H-13), gitignored in `f1bb012`, then untracked with `git rm --cached` — the third step being the one that makes the ignore rule actually bite, since `.gitignore` governs untracked files only. **The file still exists on disk and is still maintained every round under guardrail G2**; it is simply not in the repo, because rewriting it each round churned `git status` continuously. **Consequence to be aware of: the register is not backed up by git and does not travel with a clone.** Anyone picking this project up from the repo alone will not see the human-blocked items — H-1 (FSD Q9c) in particular is the reason no invoice can be issued, and that fact now lives only in this document and on the owner's disk. §18.7 and §17.7 carry it; keep it that way. |
| N-1, N-3, N-5, N-9, N-10, N-11, N-13, N-14, N-15, N-19, N-25, N-27, N-28, N-29 | **Unchanged.** |

**Closed by this pass:** N-31, N-32, N-30.

### 18.7 Next

**M6 is now complete on engineering terms, and its §5 exit criterion is no longer overstated** — a CIF invoice can be created against a shipment from the UI, which was the half that was unreachable.

**It is still not satisfiable in production, and that is not an engineering gap:** issuing any invoice requires FSD Q9c's company billing values (**H-1**), without which `DRAFT → ISSUED` returns 400. §17.10 crossed that gate with coordinator-invented placeholders in a throwaway database. **H-1 remains `Asked`, not `Answered`.**

**Next milestone is M7 — Analytics (E10-01…E10-10), ten stories**, per §5's dependency order. E10 reads aggregates from schema that M3–M6 have now finished shaping, which is exactly why §5 puts it last among feature milestones (DR-2). Two things it should inherit from this pass:

1. **`InvoiceDispatched` was split from `CatalogDispatched` in D-67 specifically so E10-06 could count them apart.** That decision was made for M7's benefit; use it.
2. **§18.4's rule.** E10's dashboard consumes a lot of new fields across ten aggregates, all read by a different runtime than the one that serialises them. Pin the wire names.

---

## 19. M7 — Analytics (E10). IN FLIGHT, NOT COMPLETE

> **STATUS AT SESSION END, 2026-08-03: two build agents were mid-write when the session was checkpointed.** HEAD is `959e6cd` and everything through §18 is committed and green. The M7 work below is **uncommitted, unreviewed, unbuilt and untested** — partial files are sitting in the working tree. **Nothing in §19.1–§19.3 is a claim that anything works.** See §19.5 for the exact resume procedure.
>
> **This section is written NOW, before the work lands, deliberately.** N-28 records this project losing an M6 contract because it lived only in a session that was cleared, leaving production XML doc comments citing a "§0/§1/§2" that existed nowhere on disk. The rule that came out of it — *any contract the source code cites must live in this document before the session ends* — applies here exactly: both agents were briefed with the contract below and their code cites it.

### 19.1 Scope of this pass — 9 of 10 stories

**In:** E10-01…E10-07 (six aggregates + the shared filter contract), E10-09 (dashboard screen), E10-10 (caching).

**Out, deliberately: E10-08 (CSV/Excel export).** It is a `[S]`, it is a different concern (file generation rather than aggregation), and it is absent from M7's §5 exit criterion. §17.11's lesson was that widening a pass mid-flight is how a screen ends up half-wired; it gets its own pass.

### 19.2 The API contract (the handoff artifact — this is the part that must survive)

One `AnalyticsController`, all routes under `/api/v1/analytics`, every one gated on the **existing** `PermissionCodes.AnalyticsView`.

**All six endpoints take the same four optional query parameters** — `fromDate`, `toDate` (`DateOnly?`), `categoryId`, `serviceTypeId` (`Guid?`). This is E10-07 satisfied structurally by one shared query record rather than bolted on afterwards. `fromDate > toDate` throws `AppValidationException`, mirroring `InvoiceService.ListAsync`.

| Route | Story | Response |
| --- | --- | --- |
| `GET /analytics/leads` | E10-01 | `LeadsAnalyticsDto` |
| `GET /analytics/service-split` | E10-02 | `ServiceSplitAnalyticsDto` |
| `GET /analytics/category-mix` | E10-03 | `CategoryMixAnalyticsDto` |
| `GET /analytics/vendors` | E10-04 | `VendorAnalyticsDto` |
| `GET /analytics/inventory` | E10-05 | `InventoryAnalyticsDto` |
| `GET /analytics/dispatch` | E10-06 | `DispatchAnalyticsDto` |

```
LeadsAnalyticsDto {
  series: [{ periodStart: DateOnly, count: int }]      // weekly buckets across the range
  bySource: [{ id, code, label, sortOrder, count }]
  byStatus: [{ id, code, label, sortOrder, count }]    // the funnel
  totalLeads, wonCount: int
  conversionRate: decimal        // 0..1; MUST be 0 (never NaN) when totalLeads is 0
  currentPeriodCount, priorPeriodCount: int   // prior = same-length window immediately before
}
ServiceSplitAnalyticsDto { totalCustomers: int, items: [{ id, code, label, sortOrder, customerCount }] }
CategoryMixAnalyticsDto  { items: [{ id, code, label, sortOrder, customerCount }] }
VendorAnalyticsDto       { totalActiveVendors: int, byCategory: [{ id, code, label, sortOrder, vendorCount, catalogCount }] }
InventoryAnalyticsDto    { onHandValue: decimal,
                           byCategory: [{ id, code, label, sortOrder, quantity, value }],
                           shipmentsByStatus: [{ id, code, label, sortOrder, count }],
                           inTransitCount, pastEtaCount: int }
DispatchAnalyticsDto     { series: [{ periodStart, count }], byStaff: [{ userId, name, count }],
                           byKind: [{ kind, count }],   // "CatalogDispatched" | "InvoiceDispatched"
                           totalDispatches: int }
```

**Three rules handed to both agents as non-negotiable, each paid for earlier in this project:**
1. **Every lookup-keyed breakdown zero-fills from the master table**, ordered by `SortOrder` then `Code` ordinal. A category with no rows arrives as `count: 0`, never omitted — `InvoiceService.BuildStatusCountsAsync` is the model. A dashboard that silently drops empty categories misreports the business.
2. **Match by `Code`, never `Label`** — labels are Super-Admin-editable master data (D-50).
3. **`byKind` must split `CatalogDispatched` from `InvoiceDispatched`.** D-67 was resolved specifically so this aggregate could count them apart; that decision was pre-paid for this story.

### 19.3 Two design decisions worth not re-litigating on resume

**No `/analytics/summary` endpoint — the stat tiles derive client-side from the six aggregates.** Every tile the prototype shows is derivable, and a separate summary query could disagree with the chart printed directly beneath it. That class of defect never gets reported; it just makes the dashboard quietly untrusted.

**Caching covers five of six. `/analytics/inventory` is deliberately NOT cached** — it carries live stock and live shipment status, and TECH_SPEC §4.5 plus E10-10's own wording forbid caching anything user-write-adjacent. Cache keys must incorporate the route **and every filter value**: a key that ignores filters serves one filtered dashboard to the next request, which is a correctness bug wearing a performance costume.

### 19.4 A gap found BEFORE delegating, not after

The prototype's **On-Hand Value** tile carries the sub-line **"3 items below reorder"**. **No reorder-level field exists anywhere in the schema** — confirmed by grep while writing the contract, so no agent burned effort on an uncomputable figure. Both were told explicitly not to invent a threshold nor hard-code the prototype's `3`; the sub-line is omitted.

Recorded as **H-15**. The real question is not "add a field" but whether the business restocks against per-item minimums at all: if yes it is a field, an admin screen and a story; if no, the sub-line should be **formally dropped from the design** so it stops reading as a gap on every future review. Pairs with **H-6** (no way to correct on-hand quantity after a physical count) — both ask whether stock is managed by numbers or by judgement.

### 19.5 Resume procedure — read this first

The working tree contains **partial output from two agents that were still writing.** Do not assume any of it is complete or correct.

1. `git status` — HEAD should be `959e6cd`. Everything at or below §18 is committed and green (643 backend, 313 frontend).
2. **Decide per-file whether to keep or discard the in-flight work.** It was never built, never run and never reviewed. `git stash`/`git checkout` back to `959e6cd` and re-delegating from §19.2 is a perfectly good option and is often cheaper than auditing half-written files — **the contract above is the expensive artifact, and it is now safe.**
3. If keeping it: build, run both suites, and apply **§18.4's rule** — assert the RAW JSON property names for all six endpoints, because `ReadFromJsonAsync<TDto>` round-trips through one serializer and proves the value, never the name. Six new endpoints of fields read by a different runtime is exactly the exposure that rule exists for.
4. Then write the real §20 close-out. **This section is a contract record, not a status claim; do not let it read as one.**


