import { Injectable } from '@angular/core';

/** A background/foreground colour pair for a chip/badge. */
export interface StatusColor {
  bg: string;
  fg: string;
}

export interface ServiceTypeColor extends StatusColor {
  label: string;
}

/**
 * Ported verbatim from the approved prototype's `SVC` constant
 * (Source/Sourcing Ops Platform.dc.html, ~line 1233). Do not edit these values
 * without updating docs/DESIGN_TOKENS.md §2 first — this is the single source
 * of truth for service-type colours; no screen may redefine it locally.
 *
 * Keyed by the real API `serviceType.code` (`CIF` / `FREIGHT_ONLY`), NOT the
 * display label. The prototype's own `SVC` constant was keyed by its label
 * `Freight-only` — faithful to the prototype, but wrong once ported against
 * the live API, whose `code` is `FREIGHT_ONLY` and whose `label` is
 * `Freight-only`. Every caller passes `code`, so a label-keyed map silently
 * fell through to the grey default for every freight-only row. Fixed as part
 * of the M5 frontend pass's live-API diff; the rendered colour/label output
 * is unchanged, only the lookup key.
 */
const SVC: Record<string, ServiceTypeColor> = {
  CIF: { label: 'CIF', bg: '#e3f2fd', fg: '#1565c0' },
  FREIGHT_ONLY: { label: 'FREIGHT-ONLY', bg: '#fff3e0', fg: '#f57f17' }
};

/**
 * Ported verbatim from the approved prototype's `ST` constant
 * (Source/Sourcing Ops Platform.dc.html, ~line 1237). Covers lead/customer
 * status, vendor status and shipment status — the prototype uses one shared
 * map for all three. Do not edit without updating docs/DESIGN_TOKENS.md §2 first.
 *
 * ASSUMPTION (recorded in docs/DESIGN_TOKENS.md §10.1): invoice statuses
 * (DRAFT/ISSUED/PAID/CANCELLED) have no prototype precedent since invoicing
 * post-dates the approved prototype. They are mapped onto the closest
 * existing semantic colour below and flagged for E0-06 sign-off.
 */
const ST: Record<string, StatusColor> = {
  NEW: { bg: '#e5e7eb', fg: '#374151' },
  QUALIFIED: { bg: '#e3f2fd', fg: '#1565c0' },
  ACTIVE: { bg: '#e8f5e9', fg: '#2e7d32' },
  WON: { bg: '#e8f5e9', fg: '#2e7d32' },
  LOST: { bg: '#ffebee', fg: '#e53935' },
  DORMANT: { bg: '#e5e7eb', fg: '#6b7280' },
  'ON-HOLD': { bg: '#fff3e0', fg: '#f57f17' },
  INACTIVE: { bg: '#e5e7eb', fg: '#6b7280' },
  PACKED: { bg: '#e5e7eb', fg: '#374151' },
  DISPATCHED: { bg: '#e3f2fd', fg: '#1565c0' },
  'IN TRANSIT': { bg: '#fff3e0', fg: '#f57f17' },
  DELIVERED: { bg: '#e8f5e9', fg: '#2e7d32' },

  // Net-new — invoicing has no prototype precedent (assumption, see above).
  DRAFT: { bg: '#e5e7eb', fg: '#374151' },
  ISSUED: { bg: '#e3f2fd', fg: '#1565c0' },
  PAID: { bg: '#e8f5e9', fg: '#2e7d32' },
  CANCELLED: { bg: '#ffebee', fg: '#e53935' }
};

/**
 * Single source of truth for status → colour mapping across the whole app
 * (TECH_SPEC §5.1). Every list/detail screen must consume this service (or
 * the accompanying pipes) instead of hard-coding a colour map locally —
 * enforced at code review per ACTION_PLAN DR-6.
 */
@Injectable({ providedIn: 'root' })
export class StatusStyleService {
  /** Colour + label for a service-type API `code` (`CIF`, `FREIGHT_ONLY`). */
  serviceType(code: string | null | undefined): ServiceTypeColor {
    return (code && SVC[code]) || { label: code ?? '—', bg: '#e5e7eb', fg: '#374151' };
  }

  /** Colour for a generic status code, falling back to `ST.NEW` like the prototype does. */
  status(code: string | null | undefined): StatusColor {
    return (code && ST[code]) || ST['NEW'];
  }
}
