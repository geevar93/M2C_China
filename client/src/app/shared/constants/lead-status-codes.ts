/**
 * The seeded lead/customer pipeline-stage **codes**, mirroring
 * `SeedDefaults.LeadStatuses` in `SourcingOps.Domain`
 * (`NEW`→1, `QUALIFIED`→2, `ACTIVE`→3, `WON`→4, `LOST`→5, `DORMANT`→6).
 *
 * Codes, not labels — same rationale as `service-type-codes.ts`: labels are
 * Super-Admin-editable master data (D-50), so any code that branches on a
 * lead status (rather than just displaying its label) must key off `code`.
 *
 * `LEAD_FUNNEL_STAGE_CODES` is the ordered subset the Dashboard's "Lead
 * Funnel" panel plots (ACTION_PLAN E10-09) — the approved prototype's funnel
 * has exactly 4 stages (`New` → `Qualified` → `Active` → `Won`); `LOST` and
 * `DORMANT` are terminal/inactive states the funnel visualisation was never
 * designed to include. The order below is the funnel's stage order, not the
 * alphabetical or lookup order — do not re-sort it.
 */
export const LEAD_STATUS_NEW = 'NEW';
export const LEAD_STATUS_QUALIFIED = 'QUALIFIED';
export const LEAD_STATUS_ACTIVE = 'ACTIVE';
export const LEAD_STATUS_WON = 'WON';
export const LEAD_STATUS_LOST = 'LOST';
export const LEAD_STATUS_DORMANT = 'DORMANT';

export const LEAD_FUNNEL_STAGE_CODES = [
  LEAD_STATUS_NEW,
  LEAD_STATUS_QUALIFIED,
  LEAD_STATUS_ACTIVE,
  LEAD_STATUS_WON
] as const;
