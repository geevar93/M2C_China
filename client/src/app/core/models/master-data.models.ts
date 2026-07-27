/**
 * Wire contract for `GET /api/v1/master-data?includeRetired=true`
 * (ACTION_PLAN E3-10; backend track's `MasterDataController`, TECH_SPEC §4.7).
 *
 * Rows are **retired, not deleted** (E3-08) — every collection here includes
 * retired rows (`isActive: false`) because historical records may still
 * reference them. `MasterDataService` is responsible for filtering retired
 * rows out of anything offered as a dropdown option; this file only models
 * the shape the API actually returns.
 */

/**
 * A `categories` row. Deliberately **not** unified with `LookupRow` — the
 * `categories` table uses `name` where every other lookup table uses
 * `code`/`label` (TECH_SPEC §6). That asymmetry mirrors the DB schema, so it
 * is modelled faithfully rather than normalised away. Use `masterDataLabel()`
 * below if you need a single display string without branching on shape.
 */
export interface CategoryRow {
  id: string;
  name: string;
  sortOrder: number;
  isActive: boolean;
}

/**
 * A `code`/`label` lookup row — the shape shared by `serviceTypes` and every
 * `*Statuses` collection (TECH_SPEC §6).
 */
export interface LookupRow {
  id: string;
  code: string;
  label: string;
  sortOrder: number;
  isActive: boolean;
}

/** The full aggregate response body. One HTTP call backs every lookup in the app. */
export interface MasterDataResponse {
  categories: CategoryRow[];
  serviceTypes: LookupRow[];
  leadStatuses: LookupRow[];
  shipmentStatuses: LookupRow[];
  invoiceStatuses: LookupRow[];
  vendorStatuses: LookupRow[];
}

/** Every key of `MasterDataResponse` — used to keep `MasterDataService`'s per-collection helpers generic. */
export type MasterDataCollectionKey = keyof MasterDataResponse;

/**
 * Single way to get a display string for any master-data row without every
 * consumer having to branch on `'name' in row` themselves — see the module
 * doc comment above for why the two shapes are not unified outright.
 */
export function masterDataLabel(row: CategoryRow | LookupRow): string {
  return 'name' in row ? row.name : row.label;
}
