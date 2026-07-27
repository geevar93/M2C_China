# Action Plan & Delivery Backlog — Sourcing Ops Platform (Phase 1)

Status: Draft for review
Source documents (authoritative — this plan invents no scope beyond them):

- `C:\work\DevZone\Hermes\China_M2C\Functional_Spec.docx` — Functional Specification (FSD) v1.0, Phase 1 baseline, **including the Section 6.8 Lightweight Invoicing module (FR-BIL-01…07)**.
- `C:\work\DevZone\Hermes\China_M2C\docs\TECH_SPEC.md` — Technical Specification implementing that FSD.

> **Source-of-truth note.** The readable mirror at `C:\work\DevZone\Hermes\China_M2C\Source\uploads\spec.txt` (and `spec_document.xml`) were found stale during this planning pass — they predated the invoicing addition. Both have since been regenerated from the current `.docx` and now include §6.8/FR-BIL-*/A9/Q9. Re-run the same regeneration after any future edit to the `.docx` so the mirrors don't drift again (see DR-13).

> **Delivery status is tracked in §9 (added 2026-07-27).** This document is now a living tracker, not only a plan. §9 records per-story status with what was actually verified and how. Statuses there are set from executed commands, never from intent.

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
| E7-01 | Inventory item CRUD | Items carry name/description, category, source vendor, unit and on-hand quantity; permission-gated and audit-logged. **Granularity (per SKU vs product line) is blocked on FSD Q3** — the schema's `sku` column supports per-SKU; confirm before building the intake UI. | FR-INV-01 [M]; FSD Q3 |
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
| E9-04 | Port the 3-step dispatch dialog | The prototype's open chat → download PDF → mark sent dialog is ported 1:1; step 2 uses the authenticated download so the file is ready to attach in WhatsApp with minimal steps. | FR-WA-02 [M], FR-WA-03 [M]; TECH_SPEC §5.2 |
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
| DR-3 | **No PDF generation library is chosen.** TECH_SPEC §3's stack table is silent on it, yet FR-BIL-03 requires storing invoices as PDFs. The obvious candidates vary hugely in footprint and licensing, which collides with constraint C1. | Either a blocked M6, or a heavy/licence-encumbered dependency chosen under time pressure. | Decide in M0 alongside OI-7 version pinning; evaluate against the C1 footprint budget and licence terms before M6 starts. |
| DR-4 | **FR-VEN-07 (vendor documents) has no table in TECH_SPEC §6.** The requirement is Could-have, so the schema gap is currently invisible. | If FR-VEN-07 is pulled into scope late it needs a migration plus a new upload path, mid-flight. | Keep it explicitly last in E5 and treat any decision to build it as a schema-change decision, not a UI story. Otherwise drop it per PA-4. |
| DR-5 | **"Port the prototype 1:1" is a subjective acceptance criterion.** TECH_SPEC §5.1 itself warns that identical markup does not guarantee identical rendering. | Repeated review/rework loops on every screen-port story; disputes about what "done" means. | Define 1:1 concretely as: same DOM structure, same computed style values from E0-01's token reference, same status→colour maps. Per-screen visual diff sign-off (E12-08) is in the DoD for every UI story, done at the time — not batched into M8. |
| DR-6 | **Configurable master data is easy to violate quietly.** Any feature component that hard-codes a category or status list to move faster breaks FSD §3.3 without failing a test. | The business's core "change it without a deploy" promise is silently false in some screens. | E3-10 provides the single service; the DoD carries an explicit "no hard-coded lookup lists" check, enforced at code review. |
| DR-7 | **Four open FSD questions change story shape, not just detail.** Q1 (freight-only external-purchase fields), Q3 (inventory per-SKU vs product line), Q4 (who may dispatch), Q9 (invoice numbering and company details) each land inside a story already in this backlog. | Building E4-12, E7-01, E9-08 or E8-08 on a guess means rework in the module where the guess was wrong. | All four are in the M0 decision list. Each affected story names its blocking question. If an answer is late, build the minimum the prototype already shows and flag the field set as provisional. |
| DR-8 | **Cross-cutting concerns are cheap upfront and expensive retrofitted.** Permission attributes and `IAuditLogger` calls touch every mutating endpoint. | Adding them after M3–M6 means revisiting every controller and every integration test. | E1-09 and E3-01 are hard gates before feature CRUD; permission-checked and audit-logged are DoD line items, not optional polish. |
| DR-9 | **There is one VPS and no staging environment** (TECH_SPEC §7 describes exactly one box). "Demo in a prod-like environment" and "don't disturb live data" pull against each other once the business starts using the tool. | Demos on the live box risk polluting real data; demos only on laptops defeat the purpose of the early E2. | PA-3 assumes local compose is the demo target and the VPS is the deploy target. Revisit at M8: once real users are on the box, either add a second small VPS or agree a maintenance-window demo protocol. |
| DR-10 | **Permission changes take effect only at next login/refresh** — an accepted trade-off in TECH_SPEC §4.3, but a support-load risk once the owner starts managing accounts. | "I changed their role and nothing happened" tickets; the owner loses trust in the admin screens. | E11-05 revokes refresh tokens on role change; the admin UI states plainly that the user must sign in again. |
| DR-11 | **Integration tests need Docker in CI.** TECH_SPEC §4.1 specifies Testcontainers Postgres; if the CI runner cannot run containers, the integration half of the DoD quietly degrades to unit tests only. | Endpoint-level regressions ship undetected while the DoD appears satisfied. | E2-06 explicitly verifies a Testcontainers-based test runs green in CI before M3 depends on it. |
| DR-12 | **Uploaded documents are the only durable state outside Postgres.** A backup routine or restore drill that covers only `pg_dump` loses every catalog and invoice PDF while appearing to succeed. | Catastrophic, and only discovered during a real recovery. | E2-08 backs up both; E12-04's restore drill is not complete until a restored document opens. |
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
| E0-02…E0-05 | **Not started** | Needs business input; deliberately out of scope this pass. |
| E0-06 | **Not started** | **Still an open gate.** E1-13/E1-14 were built without it under the §5 M1 fallback — they need sign-off, and a restyle if it diverges. Still gates E8-09/E8-10 and E11-07/E11-08. |
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
| **N-7** | **A deactivated user keeps full API access for up to 8 hours.** Verified live: after `DELETE /admin/users/{id}`, login and refresh both correctly return 401, but the user's **already-issued access token still returns 200**. The access token lifetime is 8h (TECH_SPEC §4.2), so that is the exposure window. E11-03's own wording is "blocks login and **revokes sessions**" — refresh tokens *are* revoked, so the gap is specifically the bearer token. TECH_SPEC §4.3 knowingly accepts this for *permission* changes, but deactivation is a different risk class: it is the "remove access now" operation, used when someone leaves or is compromised. Genuinely low risk at Q7's stated 2–5 internal staff, but it should be an **explicit accepted decision, not an unnoticed gap**. Three options: (a) shorten the access-token lifetime and lean on refresh rotation — simplest; (b) check `IsActive` per request — a DB hit §4.3 deliberately avoided; (c) a revocation deny-list in `ICacheService` keyed by user id with TTL equal to the token lifetime — no DB hit, bounded size, and the cache now has a proven consumer. **Recommend (c), or (a) if simplicity wins.** |
| N-8 | **Seeded default lookups are deletable while unreferenced.** Observed during verification: `DELETE` removed the seeded `ACTIVE` vendor status because no vendor referenced it yet. This is *correct* per E3-08 and consistent with FSD §3.3 configurability, but it means an early mistake can remove a default the seeder will not restore (seeding is idempotent and only fills gaps on an empty set). Consider whether seeded defaults should be retire-only. |
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
