# Technical Specification — Sourcing Ops Platform (Phase 1)

Status: Draft for review
Traceability: Implements `Functional_Spec.docx` v1.0 (Phase 1 scope, including the Section 6.8 Lightweight Invoicing addition)

## 1. Purpose & Constraints

This document translates the Functional Specification into a concrete technical design: architecture, data model, API surface, frontend approach, auth/RBAC model, caching strategy, and deployment topology. It is planning only — no code is written as part of this step.

Hard constraints supplied for this build:

| # | Constraint | Implication |
| --- | --- | --- |
| C1 | Deploys to a single Hostinger KVM VPS (4 vCPU / 8 GB RAM) | Minimize container count and per-container footprint; avoid heavy middleware (message queues, search engines, APM stacks) |
| C2 | Caching must be in-memory by default, with a config switch to Redis | Cache access goes through an abstraction (`ICacheService`); Redis client is wired but the Redis container does not run unless explicitly enabled |
| C3 | PostgreSQL as the database; Dockerized for local dev and prod | Single `docker-compose` definition reused (with overrides) across environments |
| C4 | Two account types now (Super Admin, Associate) with policy scaffolding for future granular roles | Build a permission/policy model, not a hard-coded two-role `if` statement, so the FSD's five personas (Section 4) can be layered on later as data, not code |
| C5 | Frontend must reuse the approved prototype in `Source/` "as-is, with minimal changes" | See Section 5.2 for what "as-is" can and cannot mean given the prototype's file format |
| C6 | Angular frontend, lightweight, minified build | No UI component library; standalone components; production build with tree-shaking/minification; static hosting |
| C7 | .NET 10 backend | ASP.NET Core Web API (.NET 10 LTS), Npgsql/EF Core |

---

## 2. Architecture Overview

```
                      ┌─────────────────────────────────────────┐
                      │      Hostinger KVM VPS (4 vCPU / 8GB)    │
                      │                                          │
  HTTPS (443)  ──────►│  Caddy (edge: TLS + static + proxy)      │
                      │   ├─ serves Angular dist/ (static files) │
                      │   └─ reverse_proxy /api/* ──────────┐    │
                      │                                      ▼    │
                      │                          ASP.NET Core API │
                      │                          (.NET 10, Kestrel)│
                      │                                      │    │
                      │                          ┌───────────┴───┐│
                      │                          ▼               ▼│
                      │                    PostgreSQL      In-Memory│
                      │                    (Docker volume)  Cache   │
                      │                                    (Redis   │
                      │                                    optional,│
                      │                                    off by   │
                      │                                    default) │
                      └─────────────────────────────────────────┘
```

Containers in production: `caddy`, `api`, `db`. `redis` exists in the compose file behind a profile flag and is **not started by default** — it only runs when `Caching:Provider=Redis` is switched on.

Angular is a static build (no Node server in production). Caddy serves the compiled `dist/` directly via `file_server` and reverse-proxies `/api/*` to the API container — this removes the need for a separate nginx container and its own config surface, which matters directly for the 8 GB RAM ceiling (C1).

Local development mirrors this with `docker-compose.override.yml`: `api` + `db` run in Docker; Angular runs via `ng serve` with a dev-server proxy to the API for fast iteration (hot reload), rather than rebuilding a container on every change.

---

## 3. Technology Stack

| Layer | Choice | Why |
| --- | --- | --- |
| Backend runtime | .NET 10 (LTS), ASP.NET Core Web API | Per C7; LTS support window |
| ORM | EF Core 10 + Npgsql provider | First-class Postgres support, code-first migrations |
| Auth | Custom JWT bearer (not full ASP.NET Core Identity) | Identity's full package (external logins, email confirmation, 2FA scaffolding) is unneeded surface area for an internal tool with two seeded account types; a lean `Users`/`Roles`/`Permissions` schema plus the built-in `PasswordHasher<T>` keeps dependencies minimal (C1) while still being a real, tested hashing implementation |
| Caching | `ICacheService` abstraction over `IMemoryCache` (default) / `IDistributedCache` + `StackExchange.Redis` (switchable) | Per C2 |
| Database | PostgreSQL 16 (Alpine image) | Per C3 |
| File storage | Local disk volume behind an `IFileStorage` abstraction | No object-storage dependency for Phase 1; abstraction allows a later move to S3-compatible storage without touching business logic |
| Frontend framework | Angular 19.2.x, standalone components, esbuild-based builder | Per C6; standalone components avoid NgModule boilerplate; esbuild builder is the current Angular default and produces smaller, faster production builds. **Pinned to v19** (not the current "latest" major, v22) because that's the newest line compatible with this machine's installed Node (v20.12.2) — Angular 20+ raised its minimum to Node ^20.19.0. Revisit the pin if/when Node is upgraded (OI-7). |
| Frontend state | Angular services + RxJS, no NgRx | App is CRUD-and-forms shaped; a store adds bundle weight and ceremony this app doesn't need |
| Frontend UI kit | None — hand-ported CSS from the prototype | The approved prototype's visual system is bespoke inline CSS, not built on Material/Bootstrap; adding a UI kit would both contradict "use the CSS as-is" and bloat the bundle |
| Icons | Emoji glyphs (as used in the prototype: 📊 👥 📝 🏭 📚 📦 🚚 📱) | Zero-dependency; avoids shipping an icon font/SVG sprite just for lightweight nav icons |
| Reverse proxy / TLS | Caddy | Automatic HTTPS (Let's Encrypt), single small binary (~30 MB RAM), config is a few lines vs. nginx+certbot |
| Containerization | Docker + Docker Compose | Per C1/C3 |
| Logging | Built-in `Microsoft.Extensions.Logging` → stdout, captured by Docker | Avoids standing up Seq/ELK on a resource-constrained box; revisit if log volume grows |

Deliberately **not** included in Phase 1, to hold the line on C1: message queue/broker, search engine (e.g. Elasticsearch), APM/tracing stack, generic repository layer, AutoMapper, MediatR, full ASP.NET Core Identity, a second reverse-proxy tier, NgRx.

---

## 4. Backend Design

### 4.1 Solution structure

```
SourcingOps.sln
├── src/
│   ├── SourcingOps.Api            # Controllers, middleware, Program.cs composition root
│   ├── SourcingOps.Application    # DTOs, service interfaces + implementations, validation
│   ├── SourcingOps.Domain         # Entities, enums, domain constants — no framework deps
│   └── SourcingOps.Infrastructure # EF Core DbContext + migrations, ICacheService impls,
│                                   #   IFileStorage impl, password hashing, WhatsApp link builder
└── tests/
    ├── SourcingOps.Application.Tests
    └── SourcingOps.Api.Tests      # WebApplicationFactory-based integration tests against
                                    #   a Testcontainers Postgres instance
```

Four projects, not the fuller "Clean Architecture" five/six-project split — kept intentionally thin per C1's "minimize dependency" instruction. `Application` services talk to the `DbContext` directly (via a narrow `IAppDbContext` interface for testability); there is **no generic repository layer** — it adds a layer of indirection EF Core's `DbSet<T>`/LINQ already provides, for no benefit at this scale.

### 4.2 Authentication

- Users authenticate with email + password against the `users` table.
- Passwords hashed with `Microsoft.AspNetCore.Identity.PasswordHasher<User>` (PBKDF2-based, no extra NuGet package — it's usable standalone without the rest of Identity).
- On success, API issues a short-lived JWT access token (claims: `sub` = user id, `role`, `permissions[]`, exp ~ 8h) signed with a key from configuration/secret store (never committed).
- A refresh token (random 256-bit value, stored hashed with expiry) is issued alongside the access token to avoid forcing re-login every 8 hours; refresh endpoint rotates it.
- `MustChangePassword` flag on the user record: when true (fresh account or after an admin-triggered reset), the API accepts login but scopes the resulting token to only the "change password" endpoint until the user sets a new password. Enforced by a small `RequirePasswordChangeFilter`.
- No external logins, no email verification, no MFA in Phase 1 (matches FSD Assumption A1 — internal tool only).

### 4.3 Authorization — permission/policy scaffold

Two account types exist today, but the model is permission-based from day one so the FSD's five personas (Owner/Admin, Sales Exec, Sourcing Mgr, Inventory Exec, Viewer/Analyst — Section 4 of the FSD) can be introduced later purely as data:

```
Permission (static catalog, seeded)          RolePermission (mapping table)
──────────────────────────────               ──────────────────────────────
Customers.View / .Edit                        role_id → permission_id
Vendors.View / .Edit
Catalogs.View / .Edit
Inventory.View / .Edit
Shipments.View / .Edit
Invoicing.View / .Edit / .MarkPaid
Dispatch.Send
Analytics.View
Admin.ManageUsers
Admin.ManageMasterData
```

- `roles` table holds named roles (seed: `SuperAdmin`, `Associate`); `role_permissions` maps roles to the permission catalog above.
- Seed data: `SuperAdmin` → every permission incl. `Admin.*`; `Associate` → every permission **except** `Admin.ManageUsers`/`Admin.ManageMasterData` (i.e. Associate can operate the whole business workflow today; only account/master-data administration is Super-Admin-only, matching the ask).
- A user can hold one or more roles (FSD's "roles are additive — union of permissions" principle, Section 4), even though Phase 1 only seeds one role per user.
- Enforcement: a custom `PermissionRequirement`/`PermissionAuthorizationHandler` backs `[Authorize(Policy = "Customers.Edit")]`-style attributes on controllers; policies are registered once at startup by iterating the permission catalog, so adding a new permission is a one-line addition, not new plumbing.
- The JWT carries the resolved permission set at login time (avoids a DB hit per request); a role/permission change takes effect on next login/refresh, which is acceptable for an internal tool of this size.
- Every create/update/delete goes through a small `IAuditLogger` that writes user id, action, entity, entity id, and timestamp — satisfies FSD's Auditability NFR and FR-ADM-04.

### 4.4 Account management (Super Admin only)

| Endpoint | Behavior |
| --- | --- |
| `POST /api/v1/admin/users` | Creates a user with a generated temp password (`MustChangePassword=true`). Temp password is returned **once** in the API response body for the Super Admin to relay to the new user (WhatsApp/verbally) — see Open Item OI-1, no SMTP dependency assumed for Phase 1. |
| `POST /api/v1/admin/users/{id}/reset-password` | Generates a new temp password, sets `MustChangePassword=true`, invalidates existing refresh tokens. Returned once, same as above. |
| `DELETE /api/v1/admin/users/{id}` | **Soft-deletes** (`IsActive=false`, login blocked, existing sessions revoked). Hard delete is not used because `Customer.OwnerId`, `Interaction.AuthorId`, `Dispatch.StaffId`, `AuditLog.UserId` etc. all reference users — hard-deleting would either orphan history or require cascading deletes that destroy audit trail. OI-2 confirmed with the business: deactivate is the intended meaning. |
| `POST /api/v1/admin/users/{id}/restore` | **Added in the M2 build pass** (not in the original design). Reactivates a soft-deleted user. Without it, deactivation is a one-way door and an accidental deactivation is unrecoverable through the API — `restore` itself requires `Admin.ManageUsers`, and the bootstrap seeder is idempotent so it will not re-create an existing account. |
| `GET /api/v1/admin/users` | List/search, incl. inactive, for Super Admin oversight. |
| `PUT /api/v1/admin/users/{id}/roles` | Assigns one or more roles; effective permissions are the union. Revokes the user's refresh tokens so the change takes effect promptly (see DR-10 in the action plan). |

Temp password generation: `RandomNumberGenerator`-backed, 12 characters, mixed alphanumeric + one symbol, excludes visually ambiguous characters (0/O, 1/l).

**Last-active-Super-Admin guard** (added in the M2 build pass, not in the original design). Both `DELETE /admin/users/{id}` and `PUT /admin/users/{id}/roles` reject with `400` when the operation would leave **zero** active Super Admins — i.e. deactivating the last one, or reassigning its roles so it no longer holds `SuperAdmin`. This is a lockout-safety property, not a convenience: there is no hard delete, `restore` itself requires `Admin.ManageUsers`, and the bootstrap seeder only creates an admin when none exists, so a zero-Super-Admin state would leave the system **permanently unadministrable with no recovery path through the API**. Verified live on both paths.

**Access-token revocation (deny-list).** Soft-deleting a user blocks future logins and revokes refresh tokens, but a JWT already in the caller's hands stays cryptographically valid until it expires. §4.3 knowingly accepts that lag for *permission* changes, but deactivation is the "remove access now" operation, so it is closed with a `ICacheService`-backed deny-list keyed per user, checked in the auth pipeline alongside JWT validation, with a TTL equal to the access-token lifetime (which bounds its size automatically — once every pre-revocation token has expired the entry is dead weight). It fires on deactivation and role reassignment. No per-request DB read is introduced, per §4.3. **Constraint worth stating plainly: with the default in-memory provider the deny-list is per-process, so it is only complete while the deployment runs a single API instance** — the current topology (§7.2) and OI-6's accepted no-HA assumption. Running more than one instance requires switching `Caching:Provider=Redis`, or revocation becomes partial.

### 4.5 Caching abstraction (C2)

```csharp
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);
    Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
}
```

- `MemoryCacheService` wraps `IMemoryCache` (built into the ASP.NET Core runtime — zero extra package). **Default.**
- `RedisCacheService` wraps `IDistributedCache` via `Microsoft.Extensions.Caching.StackExchangeRedis`. Only registered/connects when enabled.
- A single DI extension, `services.AddAppCaching(configuration)`, reads the switch and registers the matching implementation behind `ICacheService`. Application/Infrastructure code depends only on `ICacheService` — never on `IMemoryCache`/`IDistributedCache` directly — so the switch is a config change, not a code change.

`appsettings.json`:

```json
"Caching": {
  "Provider": "InMemory",            // "InMemory" | "Redis"
  "InMemory": { "SizeLimitMb": 64 },
  "Redis": { "ConnectionString": "", "InstanceName": "sourcingops:" }
}
```

Candidate cache consumers in Phase 1: master-data lookups (categories, statuses — read-heavy, rarely change), dashboard aggregate queries (Section 4.7 below), vendor/customer list filter option sets. Anything user-write-adjacent (inventory quantities, shipment status) is read straight from Postgres to avoid staleness bugs on a first release.

### 4.6 File storage

- `IFileStorage` (`SaveAsync`, `OpenReadAsync`, `DeleteAsync`, `GetPath`) with a `LocalDiskFileStorage` implementation writing to a mounted Docker volume (`/data/uploads`, configurable via `Storage:RootPath`).
- Path convention: `/uploads/catalog-docs/{catalogSectionId}/{documentId}-{originalFilename}`, `/uploads/invoices/{invoiceId}.pdf`, `/uploads/shipment-docs/{shipmentId}/{filename}`.
- Server-side validation on upload: content-type + magic-byte sniff for `%PDF`, max size (default 20 MB, configurable) — satisfies FR-CAT-06.
- The volume is included in the backup routine (Section 7.4) — this is the one piece of durable state outside Postgres.

### 4.7 API surface (by FSD module)

| Controller | Key routes | FSD traceability |
| --- | --- | --- |
| `AuthController` | `POST /login`, `POST /refresh`, `POST /change-password`, `POST /logout` | FR-ADM-03 |
| `AdminUsersController` | see 4.4 | FR-ADM-01 |
| `MasterDataController` | CRUD on categories, service types, lead/shipment/invoice statuses | FR-ADM-02, FSD 3.3 (configurable master data) |
| `CustomersController` | CRUD, search/filter, `POST /{id}/interactions` (notes/timeline) | FR-CRM-01…11 |
| `VendorsController` | CRUD, search/filter | FR-VEN-01…07 |
| `CatalogSectionsController` / `CatalogDocumentsController` | CRUD sections; upload/list/download/version documents | FR-CAT-01…08 |
| `InventoryController` | CRUD items, inbound stock entries, low-stock query | FR-INV-01…09 (FSD §6.4) |
| `ShipmentsController` | CRUD, status transitions, attach reference docs | FR-INV-01…09 (FSD §6.4, shipment side) |
| `InvoicesController` | Generate, list/filter, status transitions, mark-paid, PDF download | FR-BIL-01…07 (FSD §6.8) |
| `DispatchController` | `POST /dispatch-log` (records a WhatsApp send event) | FR-WA-01…07 |
| `AnalyticsController` | Aggregate endpoints per dashboard (leads/conversion, service split, category mix, vendor overview, inventory & shipments, dispatch activity) | FR-AN-01…08 |
| `AuditController` | Read-only audit trail query (Super Admin) | FR-ADM-04 |

All routes versioned under `/api/v1`. Standard REST verbs; list endpoints support `search`, `page`, `pageSize`, and the filter fields the FSD calls out per module (status, service type, category, region, owner, date range).

### 4.8 Cross-cutting

- Global exception-handling middleware → RFC 7807 `ProblemDetails` responses.
- Request logging + structured logs to stdout (Docker log driver handles retention).
- Rate limiting on `/auth/login` via the built-in `Microsoft.AspNetCore.RateLimiting` middleware (no extra package) to blunt brute-force attempts.
- CORS restricted to the single deployed frontend origin.
- No background worker process/queue in Phase 1 — the only "deferred work" candidates (low-stock flagging, dispatch logging) are cheap enough to run in-request.

---

## 5. Frontend Design (Angular)

### 5.1 What's in `Source/` and how it's used

`Source/` contains:

- `Sourcing Ops Platform.dc.html` + `support.js` — the approved prototype. **Important**: this is not static HTML/CSS. `support.js` is a generated bundle of a proprietary React-based prototyping-tool runtime ("dc-runtime") that interprets custom elements (`<x-dc>`, `<sc-for>`, `<sc-if>`) and `{{ mustache }}` bindings at runtime. It cannot be embedded in an Angular app, and isn't meant to be — it's a preview tool, not a deployable artifact.
- `_ds/industry-…/` — a generic design-system bundle (steel-blue "blueprint" wireframe theme, Barlow Condensed typography). It uses a **completely different visual language** than the actual approved screens (which use a navy sidebar, blue accent `#2d5be3`, system font stack, and emoji nav icons) and isn't referenced by the approved prototype at all. Treated as unused scaffolding from the prototyping tool — see Open Item OI-3.

**Interpretation of "use the CSS and HTML as-is with minimal changes"**: since the prototype file itself can't be deployed, "as-is" is applied to what it produces — the exact DOM structure, layout, spacing, and every inline style *value* (hex colors, px sizes, font stack) in the approved screens are carried over 1:1 into Angular templates. Concretely:

| Prototype construct | Angular equivalent |
| --- | --- |
| `<sc-for list="{{x}}" as="y">…</sc-for>` | `*ngFor="let y of x"` |
| `<sc-if value="{{cond}}">…</sc-if>` | `*ngIf="cond"` |
| `onClick="{{ handler }}"` | `(click)="handler()"` |
| `style="color:#2d5be3;…"` inline on every element | Same declarations, moved into the component's scoped `.css` file (Angular convention) — same computed styles, not hand-invented ones |
| `{{ someValue }}` text interpolation | `{{ someValue }}` (Angular's syntax is the same token, convenient coincidence) |
| Hardcoded per-status color maps (`SVC`, `ST` objects in the prototype's JS) | Ported verbatim into a small `StatusStyleService`/pipe so the same status→color mapping is defined once and reused, instead of copy-pasted per screen |

This preserves the approved look pixel-for-pixel while producing a real, buildable, lightweight app. Recommend a quick visual sign-off pass per screen after porting (diff against the prototype) rather than assuming byte-identical markup guarantees byte-identical rendering.

### 5.2 Screens identified in the prototype (7 real screens + 1 reference screen)

| Screen | Prototype state key | Notes |
| --- | --- | --- |
| Dashboard | `dash` | Stat tiles, lead funnel, category mix, service split, shipment status — maps to `AnalyticsController` |
| Customers (list) | `customers` | Search/filter by service type, status, category |
| New Lead Intake | `intake` | Form: name, business, phone (with country code), email, city, source, service type, categories, owner, status, notes, external-purchase ref (freight-only) |
| Customer detail | `custdetail` | Profile, activity timeline, notes, WhatsApp dispatch launch |
| Vendors (list) | `vendors` | Search/filter by category, region, status |
| Vendor detail | `vendordetail` | Profile, catalog sections, MOQ/lead time/rating, edit |
| Catalogs | `catalogs` | Cross-vendor browse by category/vendor/title, upload dialog |
| Inventory | `inventory` | List, search/filter, low-stock indicator with visual bar |
| Shipments (list) | `shipments` | Status tabs |
| Shipment detail | `shipdetail` | Line items, reference docs (packing list / invoice / BL scan — **already includes an "invoice" doc slot in the mock data**, ahead of the FSD's original scope) |
| Mobile Views | `mobile` | A reference screen showing responsive variants — treated as a design reference, not a distinct app route |

Also present: a global command palette (⌘K) searching across customers/vendors/catalogs/shipments/screens, and a 3-step WhatsApp dispatch dialog (**Download PDF → Open WhatsApp → Attach & send in chat**, with Log Dispatch as a separate action) — both ported as-is. *Corrected 2026-07-29: this line previously described the steps as "open chat → download PDF → mark sent", which does not match the prototype. See ACTION_PLAN §14.4.*

**Gap**: the prototype has no screens for Invoicing (FSD §6.8, added after the prototype was approved), Login, Force-password-change, or Admin/User management (FSD §6.7, and the new Super Admin account-management requirements). These four screens don't exist yet in the approved design and need to be built net-new, matching the existing visual language (same nav pattern, `.table`-style lists, form fields, stat tiles) — flagged as Open Item OI-4 for a fast business sign-off rather than blocking backend/API work on it.

### 5.3 App structure

Standalone Angular components (no NgModules), grouped by feature, lazy-loaded per route:

```
src/app/
├── core/            # shell (sidebar + topbar + command palette), auth interceptor,
│                    #   permission guard, api client base service
├── auth/            # login, force-change-password
├── dashboard/
├── customers/       # list, detail, intake form
├── vendors/         # list, detail
├── catalogs/        # browse, upload dialog
├── inventory/
├── shipments/       # list, detail
├── invoicing/        # NEW — no prototype yet
├── admin/           # NEW — user management, master data config
└── shared/          # status-style service/pipe, ported base styles, common UI atoms
                      #   (button/card/table/dialog wrappers ported from the prototype markup)
```

- Routing: `provideRouter` with lazy `loadComponent`/`loadChildren` per feature — keeps the initial bundle to shell + dashboard.
- HTTP: a thin `ApiService` wrapping `HttpClient`; one interceptor attaches the JWT and handles 401 → redirect to `/login`.
- Auth/permission guards: `canActivate` functions checking the permission set decoded from the JWT (mirrors backend policy names, e.g. `Customers.Edit`) so nav items and routes for functionality a user can't use are hidden/blocked client-side too (defense in depth; the server remains the authority).
- No NgRx/state library — feature services hold their own state (BehaviorSubject-backed lists) at the scale this app needs.

### 5.4 Build & minification (C6)

- Angular CLI's default esbuild-based application builder (`ng build` production configuration): tree-shaking, minification, and modern-JS output are on by default — no extra tooling required.
- `angular.json` bundle budgets set per feature chunk (e.g. warn at 120 KB / error at 200 KB) to catch regressions early.
- No icon font, no UI component library, no heavy date/chart library beyond what dashboards need — evaluate a small, tree-shakeable chart lib only if the FSD's dashboard visuals need more than the CSS-based bar/tile treatments already in the prototype (the prototype's charts are hand-rolled divs/bars, not a chart library — keep it that way where it still meets the requirement, to stay lightweight).
- Gzip/Brotli compression is handled at the edge (Caddy `encode` directive), not duplicated in the Angular build output.

### 5.5 Serving

Production build output (`dist/browser`) is copied into the `caddy` container's static root at image build time. No Node.js runtime exists in the production image — this both reduces the container footprint (C1) and removes an entire class of Node-specific security patching from the ops burden.

---

## 6. Data Model → PostgreSQL Schema

Physical schema derived from the FSD's Conceptual Data Model (Section 7), with FSD's "configurable master data" principle (Section 3.3) applied literally: categories and every status field are rows in a lookup table, not a hard-coded enum, so Super Admin can add/rename/retire them without a deploy.

| Table | Key columns | Notes |
| --- | --- | --- |
| `users` | id, name, email (unique), password_hash, must_change_password, is_active, created_at | |
| `roles` | id, name | Seed: SuperAdmin, Associate |
| `user_roles` | user_id, role_id | Many-to-many — supports "additive roles" for future personas |
| `permissions` | id, code (e.g. `Customers.Edit`) | Seeded catalog, Section 4.3 |
| `role_permissions` | role_id, permission_id | |
| `refresh_tokens` | id, user_id, token_hash, expires_at, revoked_at | |
| `categories` | id, name, is_active, sort_order | Configurable master data |
| `service_types` | id, code (`CIF`, `FREIGHT_ONLY`), label | Configurable master data |
| `lead_statuses` | id, code, label, sort_order | Configurable pipeline stages |
| `shipment_statuses` | id, code, label, sort_order | |
| `invoice_statuses` | id, code, label, sort_order | Draft/Issued/Paid/Cancelled by default |
| `vendor_statuses` | id, code, label, sort_order | Configurable master data (Active/Inactive/On-Hold by default) — corrected in the M1 build pass; an earlier draft of this table listed `vendors.status` as a plain column, which contradicted this document's own FK rule and FSD §3.3 |
| `customers` | id, name, business_name, phone, email, city, region, source_channel, service_type_id, status_id, owner_user_id, tags (text[]), created_at, **external_marketplace, external_order_ref, external_supplier_name, external_order_value, external_order_currency, external_order_date** | The six `external_*` columns are all **nullable** and only populated for freight-only customers, and `external_order_value` is `numeric(18,2)` so values are reportable. **Added per the FSD Q1 decision**, which chose structured fields over a single free-text one. They **replace** the earlier provisional `external_purchase_reference` free-text column, which matched what the approved intake screen collects but could not be aggregated or filtered — the reason Q1 was asked at all. Migrated in `20260727125250_AddDeferredSchemaAndSystemDefaults`. |
| `customer_categories` | customer_id, category_id | Many-to-many (category interest) |
| `interactions` | id, customer_id, author_user_id, type, text, follow_up_date, created_at | |
| `vendors` | id, name, contact_person, phone, email, region, status_id, moq, lead_time, **payment_terms**, reliability_rating, notes | FK to `vendor_statuses`, not a plain column. **`payment_terms` corrected in the M4 pass (ACTION_PLAN D-19)**: FR-VEN-06/E5-06 names "MOQ, lead time, **payment terms** and reliability rating", but this row omitted it — the same class of spec inconsistency D-2 corrected for `vendor_statuses`. Migrated in `AddVendorPaymentTermsAndM4Indexes`. |
| `vendor_categories` | vendor_id, category_id | |
| `catalog_sections` | id, vendor_id, title, category_id, tags (text[]), created_at | |
| `catalog_documents` | id, catalog_section_id, file_path, original_filename, size_bytes, version_label, is_latest, uploaded_by_user_id, uploaded_at | |
| `vendor_documents` | id, vendor_id, file_path, original_filename, size_bytes, doc_type_id, uploaded_by_user_id, uploaded_at | **Was missing from this table entirely** — built in the M4 pass (E5-07 / FR-VEN-07), which is what closed **DR-4**, but never written back here. Non-catalog vendor documents (business licence, quality certificate, test report), **filed for reference with no enforcement** per FSD Q5 (OI-8) and deliberately without version history — that is `catalog_documents`' job, not this table's. `doc_type_id` is an FK into `document_types` scoped to `Vendor` (D-25). |
| `document_types` | id, code, label, is_active, sort_order, is_system_default, **scope** | **Was missing from this table entirely** despite being created in M4 (D-25) and extended in M5 (D-34). Backs both `vendor_documents.doc_type_id` and `shipment_documents.document_type_id`. `scope` is `Vendor` or `Shipment` and decides which upload path offers the type — without it the vendor dropdown would offer "Packing List" and the shipment dropdown "Business Licence", a visible bug in both directions. **`scope` is plain text and deliberately NOT an FK to a further lookup (D-44)** — the one narrow exception to this section's FK rule, because it is a structural discriminator naming a code path, not business data a Super Admin should add rows to: a third scope invented at runtime would have nothing consuming it. It is honoured on create and ignored on update, since re-scoping a type that documents already reference would move those documents into the other module's list. |
| `dispatches` | id, customer_id, catalog_document_id, staff_user_id, message, sent_at | WhatsApp dispatch log |
| `inventory_items` | id, name, sku, **description**, category_id, vendor_id, unit, on_hand_qty, reorder_threshold, **unit_cost** | **Both additions corrected in the M5 pass**, same class of spec inconsistency as D-2 (`vendor_statuses`) and D-19 (`vendors.payment_terms`). `description` (ACTION_PLAN D-40) is named by E7-01's own acceptance criteria but was absent here. `unit_cost` (D-30) is nullable and exists because the approved inventory screen renders a per-row "Stock Value" column and an "On-Hand Value" tile — no cost column existed anywhere, so the screen was unportable. **Stock value is always computed (`on_hand_qty * unit_cost`), never stored**, so there is one source of truth; a null `unit_cost` means *not costed*, which is not the same as zero. `on_hand_qty` is **not settable by a plain update** (D-42) — stock moves only via `inventory_inbound_entries` or shipment lines. Migrated in the M5 schema commit. |
| `inventory_inbound_entries` | id, inventory_item_id, quantity, entry_date, reference, recorded_by_user_id, created_at | **New table, added in the M5 pass (D-32).** E7-02 requires "an inbound entry **with date and reference**" — a durable, queryable business record. `audit_logs` could not be its only home: that table is a cross-cutting trail keyed by action string with a free-form jsonb blob, not something the inventory screen can query. Recording an entry increments `inventory_items.on_hand_qty` in the same transaction *and* writes an audit row; the two serve different readers. |
| `shipments` | id, **reference**, customer_id, destination, service_type_id, dispatch_date, status_id, freight_cost, total_value, mode, awb_or_bl, eta | `reference` is the human-readable shipment ID (`SHP-YYMM-NNN`, e.g. `SHP-2607-014`), **unique**. **Corrected in the M1 pass**: an earlier draft had only the GUID `id`, yet the approved prototype shows this reference as the shipment's headline identifier on both the list and detail screens — staff quote it, so it must be stored, not derived. Nullable with a **partial** unique index (`WHERE reference IS NOT NULL`) rather than `NOT NULL`: the table is empty until E7-05 (M5) populates it, and a `NOT NULL` column today would force a meaningless throwaway value. Migrated in `20260727125250_AddDeferredSchemaAndSystemDefaults`. |
| `shipment_lines` | id, shipment_id, inventory_item_id, quantity, **unit_cost** | `unit_cost` added in the M5 pass (D-30) and **snapshotted at line creation** rather than read live from `inventory_items`: E8 (invoicing, M6) raises CIF invoices *against* a shipment, so a later item price change would otherwise silently rewrite the basis of an already-issued invoice. Line total is computed, never stored. |
| `shipment_status_history` | id, shipment_id, status_id, changed_by_user_id, changed_at, note | **New table, added in the M5 pass (D-33).** The approved shipment detail screen renders a 4-step stepper with a **`when` under each step**, unrenderable without stored transition times, and E7-07 requires transitions be "audit-logged **and timestamped**". Written alongside `audit_logs`, not instead of it — that is the cross-entity compliance trail, this is the shipment's own displayable history. A row is seeded at shipment creation for the opening status, so the stepper always has a first entry; `PUT /shipments/{id}/status` is the **only** path that writes here (D-43). |
| `shipment_documents` | id, shipment_id, file_path, original_filename, **size_bytes**, **document_type_id**, **uploaded_by_user_id**, uploaded_at | Packing list / BL / invoice-link. **Corrected in the M5 pass.** `doc_type` free text → `document_type_id` FK into `document_types` (D-34): the §7 DoD requires every category-shaped value be an FK to a lookup and never free text (FSD §3.3), and D-25 had already created that table for vendor documents on exactly this reasoning. **This row's earlier `doc_type` spelling is not evidence of intent** — it listed `vendors.status` as a plain column too, which D-2 corrected the same way. `size_bytes` and `uploaded_by_user_id` (D-41) back the approved screen's "184 KB · 21 Jul 2026" document rows and give parity with `vendor_documents` plus the FSD's Auditability NFR. |
| `invoices` | id, customer_id, shipment_id (nullable), invoice_number, invoice_date, amount, tax_amount, currency, status_id, pdf_file_path, created_by_user_id, created_at | FR-BIL-01…07 |
| `company_settings` | id, legal_entity_name, gstin, registered_address, bank_account_name, bank_account_number, bank_ifsc, bank_branch, invoice_number_prefix, logo_file_path, declaration_text, updated_at, updated_by_user_id | **Single-row** table holding the billing block that must appear on generated invoices (FR-BIL-07, FSD Q9). Added because no such table existed while §4.7 requires rendering invoice PDFs — an invoice cannot be produced without it. A table rather than `appsettings` so a Super Admin can change bank details without a deploy, per FSD §3.3. **Values are still pending FSD Q9c**; the shape is built, the table is deliberately **empty**. Single-row-ness is enforced by a `CHECK` constraint on a fixed id rather than by seeding a row of nulls, so "not configured yet" stays representable as "no row" instead of a row that could silently reach a real invoice. Anything needing these values must fail explicitly. Migrated in `20260727125250_AddDeferredSchemaAndSystemDefaults`. |
| `audit_logs` | id, user_id, action, entity_type, entity_id, occurred_at, details (jsonb) | FR-ADM-04 |

All FK-referenced "status"/"category"/"service type" columns are `*_id` foreign keys into the corresponding lookup table, not free-text or enum columns — this is the direct implementation of the FSD's configurability requirement.

**Every lookup table also carries `is_active`, `sort_order` and `is_system_default`.** `is_active` drives retire/restore: rows are **retired, not deleted**, so historical records keep resolving their labels. `is_system_default` is set **only by the seeder**, for the default set this document defines, and makes those rows **retire-only** — the delete path rejects them with `409` regardless of reference count. Rationale: FSD §3.3 only ever asks that master data be "add / rename / retire"; hard-deleting a default was never a requirement, and because seeding is idempotent and only fills gaps on an empty set, a deleted default is not restored on the next startup. (Found the hard way during the M2 pass, when an unreferenced seeded vendor status was deleted during verification.) Rows the business adds itself carry the flag as false and remain hard-deletable while unreferenced.

Retire semantics also mean the **`code` column of a lookup row is immutable after creation** — only the display field (`name` for categories, `label` elsewhere) is editable. The frontend's `StatusStyleService` keys the prototype's ported status→colour maps by `code`, so a mutable code would silently break status colours across every screen.

Migrations: EF Core Code-First migrations, applied automatically on API container startup in dev, and via an explicit `dotnet ef database update` step in the deploy pipeline for production (never auto-migrate against prod on container start, to avoid a bad deploy racing a schema change).

---

## 7. Docker & Deployment

### 7.1 Local development (`docker-compose.yml` + `docker-compose.override.yml`)

```yaml
services:
  api:
    build: ./src/SourcingOps.Api
    ports: ["5000:8080"]
    environment:
      - ConnectionStrings__Default=Host=db;Database=sourcingops;Username=app;Password=${DB_PASSWORD}
      - Caching__Provider=InMemory
    volumes:
      - uploads:/data/uploads
    depends_on: [db]

  db:
    image: postgres:16-alpine
    environment:
      - POSTGRES_DB=sourcingops
      - POSTGRES_USER=app
      - POSTGRES_PASSWORD=${DB_PASSWORD}
    volumes:
      - pgdata:/var/lib/postgresql/data
    ports: ["5432:5432"]   # local debug only — not exposed in prod

  redis:                    # NOT started unless explicitly requested
    image: redis:7-alpine
    profiles: ["redis"]
    ports: ["6379:6379"]

volumes:
  pgdata:
  uploads:
```

Angular runs via `ng serve` on the host (or a thin dev container) proxying `/api` to `http://localhost:5000`, for fast rebuild/hot-reload — it does not run inside `docker-compose` for local dev, to keep the inner loop fast. `docker compose --profile redis up` when specifically testing the Redis cache path.

### 7.2 Production topology (Hostinger KVM, 4 vCPU / 8 GB)

```yaml
services:
  caddy:
    image: caddy:2-alpine
    ports: ["80:80", "443:443"]
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile
      - angular-dist:/srv
      - caddy-data:/data     # TLS certs

  api:
    image: registry.example/sourcingops-api:<tag>
    environment:
      - ConnectionStrings__Default=Host=db;Database=sourcingops;Username=app;Password=${DB_PASSWORD}
      - Caching__Provider=InMemory
      - Jwt__SigningKey=${JWT_SIGNING_KEY}
    volumes:
      - uploads:/data/uploads
    depends_on: [db]
    deploy:
      resources:
        limits: { memory: 400M }

  db:
    image: postgres:16-alpine
    environment: [...]
    volumes: [pgdata:/var/lib/postgresql/data]
    deploy:
      resources:
        limits: { memory: 1200M }

  redis:                    # still off by default in prod
    image: redis:7-alpine
    profiles: ["redis"]

volumes: { pgdata: {}, uploads: {}, angular-dist: {}, caddy-data: {} }
```

`Caddyfile` (indicative):

```
sourcingops.example.com {
  encode gzip zstd
  handle /api/* {
    reverse_proxy api:8080
  }
  handle {
    root * /srv
    try_files {path} /index.html
    file_server
  }
}
```

### 7.3 Rough resource budget (8 GB ceiling)

| Component | Approx. RAM |
| --- | --- |
| OS + Docker daemon | ~0.5–1 GB |
| PostgreSQL (tuned `shared_buffers`) | ~1–1.5 GB |
| API (.NET, single instance) | ~250–400 MB |
| Caddy | ~30–50 MB |
| Headroom / OS cache / burst | ~4.5 GB |

Comfortable fit at this scale, with substantial headroom for growth before the VPS itself needs upsizing — Redis, if switched on later, adds a small (~50–100 MB idle) additional service within the same envelope.

### 7.4 Backups

- Nightly `pg_dump` (via a small scheduled job — either host cron calling `docker exec db pg_dump`, or a dedicated tiny backup sidecar) written to off-box storage (Hostinger snapshot target or an S3-compatible bucket).
- `uploads` volume synced (rsync/tar) on the same schedule — this is the one piece of state not in Postgres and must be backed up alongside it.
- Retention and restore-drill cadence: left as an Open Item (OI-5) pending business input on RPO/RTO expectations.

### 7.5 CI/CD (indicative, kept minimal)

- Build pipeline: build & test both projects, build Docker images for `api` and the Angular static bundle, push to a registry.
- Deploy: SSH to the VPS, `docker compose pull && docker compose up -d`, then run pending EF Core migrations as an explicit step.
- No build/compile work happens on the production VPS itself.

---

## 8. Security

- TLS everywhere via Caddy's automatic HTTPS.
- Password hashing via `PasswordHasher<T>` (PBKDF2, framework-provided).
- JWT signing key and DB credentials supplied via environment/secret store, never committed; `.env` files git-ignored.
- Rate limiting on the login endpoint (built-in middleware, no extra package).
- CORS restricted to the deployed frontend origin only.
- File upload validation: content-type + magic-byte check, size cap, stored outside the web root, served only through an authenticated download endpoint (not a public static path) for catalog/invoice/shipment documents.
- Audit log (Section 4.3) covers create/update/delete and auth events.
- Postgres port (5432) is exposed only in local dev; not published on the production compose file.

---

## 9. NFR Traceability (FSD Section 8)

| FSD NFR | Tech decision |
| --- | --- |
| Usability | Ported prototype preserved 1:1 visually; mobile-responsive via existing prototype patterns |
| Performance (2–3s loads) | Lean Angular bundle (no UI kit, lazy loading), cache-backed read-heavy master-data/dashboard queries, indexed FKs |
| Security | Section 8 above |
| Reliability & backup | Section 7.4 |
| Scalability | Permission model, lookup-table master data, and cache abstraction all designed to extend without rework; single-VPS topology is Phase 1-appropriate, not a ceiling — see OI-6 |
| Auditability | `audit_logs` table + `IAuditLogger` on every mutating action |
| Localisation | Phone stored with country code (FR-CRM-04); currency stored as INR now, `currency` column on `invoices` future-proofs multi-currency (FSD A7) |
| Availability | Single-VPS is a Phase 1 tradeoff; no HA/failover in this design — acceptable per business-hours-only NFR, flagged as OI-6 if that assumption changes |
| Data privacy | Access restricted via auth + permissions; no data leaves the box except backups |

---

## 10. Open Items (technical, distinct from the FSD's own register)

| # | Open item |
| --- | --- |
| OI-1 | Confirm temp-password delivery: shown once on-screen to Super Admin (assumed, no SMTP dependency) vs. emailed (requires adding an SMTP/email-provider dependency) |
| OI-2 | Confirm "delete an account" means deactivate (soft delete, assumed) vs. true hard delete — hard delete conflicts with retaining audit/ownership history |
| OI-3 | Confirm the `_ds/industry-…` design-system bundle in `Source/` is unused scaffolding and safe to ignore (it matches none of the approved screens) |
| OI-4 | Invoicing, Login, Force-password-change, and Admin/User-management screens don't exist in the approved prototype yet — need a fast design pass in the same visual language before/alongside their implementation |
| OI-5 | Backup retention window and restore-drill expectations (RPO/RTO) not yet specified by the business |
| OI-6 | Single-VPS, no-HA topology assumed acceptable for Phase 1 given business-hours-only availability NFR — flag if that changes |
| OI-8 | **FSD §10.2 answers received 2026-07-27** (relayed via the coordinator from the business owner — recorded as relayed, not as direct written sign-off; re-confirm before anything expensive depends on them). **Q1**: store structured freight-only external-purchase detail — marketplace, order ref, supplier name, order value + currency, order date, all optional (§6 `customers` updated). **Q3**: inventory per-SKU with SKU optional — matches what is already built, no change. **Q4**: any staff may dispatch over WhatsApp — matches the seeded Associate role already holding `Dispatch.Send`, no change; tightening later is a data change, not code (C4). **Q5**: compliance documents are filed for reference only with **no enforcement** — scope is vendor-level (FR-VEN-07/E5-07, needs the `vendor_documents` table DR-4 already flags) plus a shipment-level `doc_type` value; category-driven blocking rules are explicitly **not** in Phase 1. **Q9a**: invoice numbering is `INV-YYMM-NNN` with a monthly reset, mirroring the prototype's `SHP-2607-014` convention — explicitly **not** a gap-free annual sequence, so confirm with the accountant if GST filing later demands one. **Q2**: build **active** follow-up reminders — a "due follow-ups" view, not a passive log; note `interactions.follow_up_date` already exists, so E4-08 needs **no migration**, only the surfacing. **Q7**: ~2–5 staff accounts, with that many or fewer concurrent users — confirms the single-instance / 400 MB sizing and the OI-6 no-HA assumption both hold, and retires the earlier concern that there is no bulk account creation (a non-issue at this scale). **Q8**: yes, there **is** legacy data to migrate — E12-07 is held as "yes, scope TBD" and explicitly **not** dropped; sizing awaits a sample file from the owner. |
| OI-9 | **Still open. Neither blocks M2, but both block a later milestone.** (1) FSD **Q9c** — the actual billing block values: legal entity name, GSTIN, registered address, bank details. `company_settings` can be built empty, but **no invoice can be issued until these exist**, so this gates M6. Requested from the owner directly; it is real data, not a decision. (2) FSD **Q6** — expected data volumes (customers/vendors/catalogs per month). **This one was never actually put to the owner**: it was omitted from the question batch that produced the Q1–Q9a answers, so it is unanswered rather than pending. It sets the representative volumes E12-02's performance pass tests against, and gates the E2-10 memory-budget recheck (M1 only measured idle usage, which is weak evidence). **Ask it together with the Q8 sample file** — the legacy data being migrated *is* the initial data volume, so one request answers both. |
| OI-7 | **Reopened** (M1 pass). Versions pinned: .NET 10 SDK 10.0.203 / Angular CLI 19.2.27, core 19.2.25 for Node v20.12.2 compatibility (see Section 3 footnote); Docker 29.4, Compose v5.1.2, Caddy 2.11.3, PostgreSQL client 18 also confirmed present locally. **New finding**: `npm audit` reports moderate/high advisories in `@angular/core`/`@angular/compiler` ≤19.2.25, fixed only in the 22.x line, which the installed Node cannot run — so the version pin and staying on a patched Angular are currently mutually exclusive. Low immediate risk (internal tool, behind auth, no public exposure yet), but this is a **Node-upgrade decision for the business/ops owner**, not something to leave undiscovered until launch. |

---

## 11. Explicit Non-Goals (Phase 1)

Per FSD Section 3.2 (as amended) and the constraints above: no RFQ/quotation engine, no order management, no payment gateway/reconciliation, no landed-cost engine, no WhatsApp Business API, no customer/vendor self-service portals, no native mobile app, no multi-tenant support, no message queue/worker infrastructure, no generic repository/AutoMapper/MediatR/NgRx layers, no UI component library.
