/**
 * Denormalised lookup references embedded directly in a Vendor/Catalog
 * response body — distinct from `core/models/master-data.models.ts`'
 * `CategoryRow`/`LookupRow`, which model the full `/master-data` rows
 * (sortOrder, isActive) used to populate a filter dropdown. These are the
 * smaller shapes the vendor/catalog-section endpoints embed for display
 * (ACTION_PLAN E5/E6 binding contract) — a list/detail row carries the
 * resolved `{ id, code, label }` or `{ id, name }` object directly rather
 * than a bare id, so no extra lookup is needed to render it.
 *
 * `CategoryRef` mirrors the same `name`-not-`label` asymmetry `CategoryRow`
 * already documents — it is the DB schema's shape, carried faithfully rather
 * than normalised away.
 */
export interface CategoryRef {
  id: string;
  name: string;
}

export interface StatusRef {
  id: string;
  code: string;
  label: string;
}
