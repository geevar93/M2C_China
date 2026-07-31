import { Injectable } from '@angular/core';
import { SERVICE_TYPE_CIF, SERVICE_TYPE_FREIGHT_ONLY } from '../constants/service-type-codes';

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
 */
const SVC: Record<string, ServiceTypeColor> = {
  [SERVICE_TYPE_CIF]: { label: 'CIF', bg: '#e3f2fd', fg: '#1565c0' },
  // KEYED BY CODE, NOT LABEL. The prototype's raw `svc` value was the string
  // 'Freight-only', and this map was ported using it as the key — but the API
  // serialises `code: 'FREIGHT_ONLY'` (SeedDefaults.ServiceTypeFreightOnly),
  // with 'Freight-only' as the *label*. Every caller passes `.code`, so every
  // freight-only chip fell through to the grey default with a raw
  // 'FREIGHT_ONLY' label instead of the prototype's orange 'FREIGHT-ONLY'.
  // Found during the M5 screen pass by diffing against a live response.
  [SERVICE_TYPE_FREIGHT_ONLY]: { label: 'FREIGHT-ONLY', bg: '#fff3e0', fg: '#f57f17' }
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
 * Inventory stock-level colours, ported verbatim from the approved prototype's
 * `invRows()` (Source/Sourcing Ops Platform.dc.html, ~line 1412) — the same three
 * pairs it computes inline for the level chip, the quantity text and the bar fill.
 */
const STOCK: Record<string, StatusColor> = {
  HEALTHY: { bg: '#e8f5e9', fg: '#2e7d32' },
  LOW: { bg: '#fff3e0', fg: '#f57f17' },
  NEGATIVE: { bg: '#ffebee', fg: '#e53935' }
};

/**
 * Single source of truth for status → colour mapping across the whole app
 * (TECH_SPEC §5.1). Every list/detail screen must consume this service (or
 * the accompanying pipes) instead of hard-coding a colour map locally —
 * enforced at code review per ACTION_PLAN DR-6.
 */
@Injectable({ providedIn: 'root' })
export class StatusStyleService {
  /** Colour + label for a service-type **code** (`CIF`, `FREIGHT_ONLY`) — never a label. */
  serviceType(code: string | null | undefined): ServiceTypeColor {
    return (code && SVC[code]) || { label: code ?? '—', bg: '#e5e7eb', fg: '#374151' };
  }

  /** Colour for a generic status code, falling back to `ST.NEW` like the prototype does. */
  status(code: string | null | undefined): StatusColor {
    return (code && ST[code]) || ST['NEW'];
  }

  /**
   * Colour for an inventory `stockLevel` (E7-11). Lives here rather than in the
   * inventory component so DR-6's "one colour source" rule holds for it too —
   * the shipment detail screen renders the same three levels against its lines.
   *
   * Deliberately a separate map from `ST`: stock level is a *computed* condition,
   * not a configurable status row, and it must not become reachable through
   * `status()` where a Super-Admin-created status code could collide with it.
   */
  stockLevel(level: string | null | undefined): StatusColor {
    return (level && STOCK[level]) || STOCK['HEALTHY'];
  }
}
