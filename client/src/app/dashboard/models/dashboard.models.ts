/**
 * Wire contracts for `GET /api/v1/analytics/*` (ACTION_PLAN E10-09, TECH_SPEC
 * §5.4). Six aggregate endpoints, all accepting the same optional query
 * params — settled between the frontend and backend tracks building in
 * parallel this milestone; not yet live-diffed against a running instance
 * (unlike most other `*.models.ts` files in this app, whose header comments
 * warn about assumed-vs-live DTOs) because there was no running instance to
 * diff against while this screen was built. Re-verify at integration.
 *
 * Every lookup-keyed array is **zero-filled** by the server (a category with
 * no rows still arrives with `count: 0`/`customerCount: 0`) and pre-ordered
 * by `sortOrder` — components must not re-sort or filter out zero rows.
 *
 * Matching MUST be done on `code`, never on `label` — labels are
 * Super-Admin-editable master data (D-50 already shipped this defect once
 * elsewhere in the app; see `StatusStyleService`'s doc comment).
 */

export interface AnalyticsQueryParams {
  fromDate?: string;
  toDate?: string;
  categoryId?: string;
  serviceTypeId?: string;
}

/** One point on a time series (`leads.series`, `dispatch.series`). */
export interface AnalyticsSeriesPoint {
  periodStart: string;
  count: number;
}

/** A zero-filled, sortOrder-ordered lookup row carrying a single count. */
export interface AnalyticsLookupCount {
  id: string;
  code: string;
  label: string;
  sortOrder: number;
  count: number;
}

/** `GET /analytics/leads`. */
export interface LeadsAnalytics {
  series: AnalyticsSeriesPoint[];
  bySource: AnalyticsLookupCount[];
  byStatus: AnalyticsLookupCount[];
  totalLeads: number;
  wonCount: number;
  /** 0..1 — multiply by 100 to render as a percentage. Do not double-convert. */
  conversionRate: number;
  currentPeriodCount: number;
  priorPeriodCount: number;
}

/** One row of `serviceSplit.items` — same shape as `AnalyticsLookupCount` but keyed `customerCount`, not `count`. */
export interface AnalyticsServiceSplitItem {
  id: string;
  code: string;
  label: string;
  sortOrder: number;
  customerCount: number;
}

/** `GET /analytics/service-split`. */
export interface ServiceSplitAnalytics {
  totalCustomers: number;
  items: AnalyticsServiceSplitItem[];
}

/** One row of `categoryMix.items`. */
export interface AnalyticsCategoryMixItem {
  id: string;
  code: string;
  label: string;
  sortOrder: number;
  customerCount: number;
}

/** `GET /analytics/category-mix`. */
export interface CategoryMixAnalytics {
  items: AnalyticsCategoryMixItem[];
}

/** One row of `vendors.byCategory` — modelled for contract completeness; this screen has no consumer for it (see `dashboard.component.ts` header comment). */
export interface AnalyticsVendorCategoryItem {
  id: string;
  code: string;
  label: string;
  sortOrder: number;
  vendorCount: number;
  catalogCount: number;
}

/** `GET /analytics/vendors`. */
export interface VendorsAnalytics {
  totalActiveVendors: number;
  byCategory: AnalyticsVendorCategoryItem[];
}

/** One row of `inventory.shipmentsByStatus`. */
export interface AnalyticsShipmentStatusItem {
  id: string;
  code: string;
  label: string;
  sortOrder: number;
  count: number;
}

/** One row of `inventory.byCategory` — not consumed by this screen. */
export interface AnalyticsInventoryCategoryItem {
  id: string;
  code: string;
  label: string;
  sortOrder: number;
  quantity: number;
  value: number;
}

/** `GET /analytics/inventory`. */
export interface InventoryAnalytics {
  onHandValue: number;
  byCategory: AnalyticsInventoryCategoryItem[];
  shipmentsByStatus: AnalyticsShipmentStatusItem[];
  inTransitCount: number;
  pastEtaCount: number;
}

/** `dispatch.byKind[].kind` — the only two values the contract defines. */
export type DispatchKind = 'CatalogDispatched' | 'InvoiceDispatched';

export interface AnalyticsDispatchByStaffItem {
  userId: string;
  name: string;
  count: number;
}

export interface AnalyticsDispatchByKindItem {
  kind: DispatchKind;
  count: number;
}

/** `GET /analytics/dispatch`. */
export interface DispatchAnalytics {
  series: AnalyticsSeriesPoint[];
  byStaff: AnalyticsDispatchByStaffItem[];
  byKind: AnalyticsDispatchByKindItem[];
  totalDispatches: number;
}
