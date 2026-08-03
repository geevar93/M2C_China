import { Injectable } from '@angular/core';
import { TimelineEventKind } from '../../customers/models/customer.models';

/**
 * Kind → dot colour for the customer activity timeline (ACTION_PLAN E4-15).
 * Sibling to StatusStyleService rather than an extension of it: this maps a
 * *timeline event kind*, not a status/service-type code, to a colour, so it
 * gets its own small service rather than overloading `status()`/`serviceType()`.
 *
 * The API deliberately does not send a colour (see the E4-15 task brief) —
 * the prototype's mock timeline rows carried a `dot` hex directly
 * (Source/Sourcing Ops Platform.dc.html `baseTimeline`, ~line 1481) but the
 * real TimelineEvent only sends `kind`. Every value below is derived once,
 * here, and consumed by `{{ event.kind | timelineDot }}` — never inline per
 * template (DR-6 discipline extended to this new event-kind concept).
 *
 * Ported verbatim from `baseTimeline` where a kind has a direct precedent:
 *   - EnquiryCaptured  -> '#6b7280' ("Enquiry captured" row)
 *   - NoteAdded        -> '#6b7280' ("Note added" row)
 *   - StatusChanged    -> '#2e7d32' ("Status changed" row)
 *   - CatalogDispatched -> '#2d5be3' ("Catalog dispatched" row)
 *   - InvoiceDispatched -> '#2d5be3' (D-67: a distinct KIND sharing the same
 *     colour. Both are "sent via WhatsApp", the prototype has no separate row
 *     for an invoice send, and inventing a colour would breach DESIGN_TOKENS.
 *     The kinds are separate so E10 can count them apart; the colour is shared
 *     so the timeline still reads as one family of event.)
 *   - ShipmentRecorded -> '#f57f17' ("Shipment recorded" row)
 *
 * ASSUMPTIONS (flagged — no prototype precedent, these three kinds don't
 * exist in `baseTimeline` because ownership reassignment and invoicing
 * post-date the approved prototype):
 *   - OwnerChanged        -> '#1565c0' (info blue, same family as the
 *     QUALIFIED/DISPATCHED status colour — a neutral administrative change)
 *   - InvoiceCreated      -> '#1565c0' (info blue, same reasoning)
 *   - InvoiceStatusChanged -> '#2e7d32' (reuses StatusChanged's green — same
 *     underlying "a status changed" shape, just for an invoice)
 */
const TIMELINE_DOT: Record<TimelineEventKind, string> = {
  EnquiryCaptured: '#6b7280',
  NoteAdded: '#6b7280',
  StatusChanged: '#2e7d32',
  OwnerChanged: '#1565c0',
  CatalogDispatched: '#2d5be3',
  InvoiceDispatched: '#2d5be3',
  ShipmentRecorded: '#f57f17',
  InvoiceCreated: '#1565c0',
  InvoiceStatusChanged: '#2e7d32'
};

const FALLBACK_DOT = '#6b7280';

@Injectable({ providedIn: 'root' })
export class TimelineStyleService {
  dotColor(kind: TimelineEventKind | string | null | undefined): string {
    if (!kind) return FALLBACK_DOT;
    return TIMELINE_DOT[kind as TimelineEventKind] ?? FALLBACK_DOT;
  }
}
