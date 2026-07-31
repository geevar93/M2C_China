/**
 * Wire contract for the existing `MasterDataController` (M2; ACTION_PLAN
 * E3-03..E3-09), as consumed by the admin configuration screen (E0-04b).
 *
 * Deliberately **not** the same interfaces as `core/models/master-data.models.ts`:
 * that file models only what the read-only lookup consumers (customers,
 * intake, etc.) need and omits `isSystemDefault`, which this screen needs to
 * decide whether Delete is offered at all (N-8: seeded defaults are
 * retire-only). Re-declared here rather than editing the shared core model,
 * which is off-limits for this pass — both shapes describe the same backend
 * DTOs (`CategoryDto` / `LookupItemDto` in `MasterDataDtos.cs`), just with the
 * field this screen additionally needs.
 */

/** A `categories` row — uses `name`, not `code`/`label` (real DB asymmetry, TECH_SPEC §6). */
export interface CategoryRow {
  id: string;
  name: string;
  sortOrder: number;
  isActive: boolean;
  isSystemDefault: boolean;
}

/** The `code`/`label` shape shared by `serviceTypes` and every `*Statuses` collection. */
export interface LookupRow {
  id: string;
  code: string;
  label: string;
  sortOrder: number;
  isActive: boolean;
  isSystemDefault: boolean;
  /** Only ever populated for `documentTypes` — `Vendor` or `Shipment` (D-34/D-44/D-45). */
  scope?: string | null;
}

/** Row shape used wherever a screen doesn't need to distinguish category vs. lookup. */
export type MasterDataRow = CategoryRow | LookupRow;

export interface MasterDataAggregate {
  categories: CategoryRow[];
  serviceTypes: LookupRow[];
  leadStatuses: LookupRow[];
  shipmentStatuses: LookupRow[];
  invoiceStatuses: LookupRow[];
  vendorStatuses: LookupRow[];
  documentTypes: LookupRow[];
}

export type MasterDataCollectionKey = keyof MasterDataAggregate;

/** Only `categories` uses `CategoryRow` — every other key uses `LookupRow`. */
export function isCategoryCollection(key: MasterDataCollectionKey): boolean {
  return key === 'categories';
}

/** URL segment per collection (kebab-case) — the binding contract's exact spelling. */
export const COLLECTION_SEGMENTS: Record<MasterDataCollectionKey, string> = {
  categories: 'categories',
  serviceTypes: 'service-types',
  leadStatuses: 'lead-statuses',
  shipmentStatuses: 'shipment-statuses',
  invoiceStatuses: 'invoice-statuses',
  vendorStatuses: 'vendor-statuses',
  documentTypes: 'document-types'
};

/** Display label per collection tab. */
export const COLLECTION_LABELS: Record<MasterDataCollectionKey, string> = {
  categories: 'Categories',
  serviceTypes: 'Service Types',
  leadStatuses: 'Lead Statuses',
  shipmentStatuses: 'Shipment Statuses',
  invoiceStatuses: 'Invoice Statuses',
  vendorStatuses: 'Vendor Statuses',
  documentTypes: 'Document Types'
};

export const COLLECTION_KEYS: MasterDataCollectionKey[] = [
  'categories',
  'serviceTypes',
  'leadStatuses',
  'shipmentStatuses',
  'invoiceStatuses',
  'vendorStatuses',
  'documentTypes'
];

/** Single request shape for create/update. Categories only ever send `name`;
 * every other collection sends `code` (create only — PUT never changes it,
 * D-12) and `label`.
 *
 * `scope` is sent only by the `documentTypes` tab and, like `code`, only on
 * create — the server honours it on POST and ignores it on PUT (D-34), because
 * re-scoping a type that documents already reference would silently move those
 * documents into the other module's dropdown. */
export interface UpsertMasterDataRequest {
  name?: string;
  code?: string;
  label?: string;
  scope?: string;
}

/**
 * The two values `documentTypes.scope` may hold, mirroring
 * `DocumentTypeScopes` server-side. A closed set on purpose: `scope` is a
 * code-path discriminator the application switches on, not business data a
 * Super Admin can add rows to (D-44), so it is a fixed selector rather than a
 * lookup-backed dropdown — this is the one place DR-6's "no hard-coded lookup
 * lists" rule does not apply, and the reason is that a third scope invented at
 * runtime would have no code path to serve it.
 */
export const DOCUMENT_TYPE_SCOPES = ['Vendor', 'Shipment'] as const;

export type DocumentTypeScope = (typeof DOCUMENT_TYPE_SCOPES)[number];

/** One row of the bulk reorder request body: `[{ id, sortOrder }, ...]`. */
export interface ReorderItem {
  id: string;
  sortOrder: number;
}
