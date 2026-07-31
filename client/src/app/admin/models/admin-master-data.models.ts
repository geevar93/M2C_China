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

/**
 * The `code`/`label` shape shared by `serviceTypes` and every `*Statuses` collection.
 *
 * `scope` (D-34/D-44/D-45) is a trailing nullable field the API serialises on
 * every collection: `null` for everything except `documentTypes`, where it is
 * `"Vendor"` or `"Shipment"`. See the matching note on
 * `core/models/master-data.models.ts`'s `LookupRow` — same field, re-declared
 * here per this file's header comment.
 */
export interface LookupRow {
  id: string;
  code: string;
  label: string;
  sortOrder: number;
  isActive: boolean;
  isSystemDefault: boolean;
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

/**
 * Single request shape for create/update. Categories only ever send `name`;
 * every other collection sends `code` (create only — PUT never changes it,
 * D-12) and `label`.
 *
 * `scope` applies only to `documentTypes` (D-34/D-44). Confirmed against the
 * backend's `UpsertMasterDataRequest`/`MasterDataService.UpdateLookupAsync`
 * (SourcingOps.Application/MasterData): scope is honoured on **create only**
 * and silently ignored on update — same treatment as `code` under D-12, so
 * the create form sends it and the edit form must render it read-only
 * rather than offer a control that looks live but does nothing server-side.
 */
export interface UpsertMasterDataRequest {
  name?: string;
  code?: string;
  label?: string;
  scope?: string;
}

/** The only two valid `documentTypes.scope` values (D-44: a plain-text discriminator, not an FK lookup). */
export const DOCUMENT_TYPE_SCOPES = ['Vendor', 'Shipment'] as const;

/** One row of the bulk reorder request body: `[{ id, sortOrder }, ...]`. */
export interface ReorderItem {
  id: string;
  sortOrder: number;
}
