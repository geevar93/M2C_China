/**
 * Wire contracts for the `/customers` API surface (ACTION_PLAN E4-13/E4-14/E4-15).
 * Fixed by the cross-track coordinator ahead of the backend build — code
 * against this shape, do not invent fields it doesn't list.
 *
 * Master data arrives as **ids only** (serviceTypeId/statusId/categoryIds) —
 * resolve labels/colours via MasterDataService + StatusStyleService, never
 * hard-code a lookup. `ownerName`/`actorName` are the one exception: users
 * are not part of the master-data cache, so those come denormalised already.
 */

export interface CustomerListItem {
  id: string;
  name: string;
  businessName: string;
  phone: string;
  city: string | null;
  region: string | null;
  sourceChannel: string;
  serviceTypeId: string;
  statusId: string;
  categoryIds: string[];
  ownerUserId: string | null;
  ownerName: string | null;
  tags: string[];
  createdAt: string;
}

export interface CustomerDetail extends CustomerListItem {
  email: string | null;
  notes: string | null;
  externalMarketplace: string | null;
  externalOrderRef: string | null;
  externalSupplierName: string | null;
  externalOrderValue: number | null;
  externalOrderCurrency: string | null;
  externalOrderDate: string | null;
}

export interface CustomersListResponse {
  items: CustomerListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface CustomersListParams {
  search?: string;
  page?: number;
  pageSize?: number;
  statusId?: string;
  serviceTypeId?: string;
  categoryId?: string;
  region?: string;
  ownerUserId?: string;
  tag?: string;
}

/**
 * Body for `POST /customers`. `confirmDuplicate` is only sent on the retry
 * after a 409 (E4-10) — see CustomersService.create().
 *
 * ASSUMPTION (flagged — the contract brief did not give a request-body
 * shape, only the response): mirrors CustomerDetail's writable fields.
 * `region` is never sent from the intake screen — the prototype's form only
 * has a single combined "City / Region" input, ported as `city`; there is no
 * second field to source `region` from, so it is left unset (see E4-13
 * report notes).
 */
export interface CreateCustomerRequest {
  name: string;
  businessName: string;
  phone: string;
  email?: string | null;
  city?: string | null;
  region?: string | null;
  sourceChannel: string;
  serviceTypeId: string;
  statusId: string;
  categoryIds: string[];
  ownerUserId?: string | null;
  notes?: string | null;
  externalMarketplace?: string | null;
  externalOrderRef?: string | null;
  externalSupplierName?: string | null;
  externalOrderValue?: number | null;
  externalOrderCurrency?: string | null;
  externalOrderDate?: string | null;
  confirmDuplicate?: boolean;
}

/**
 * The 409 ProblemDetails body for a duplicate-phone POST /customers (E4-10).
 * ASSUMPTION (flagged — extension field name is not specified by the
 * contract brief): the existing customer's summary is expected under
 * `existingCustomer`, shaped like CustomerListItem.
 */
export interface DuplicateCustomerProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  existingCustomer?: CustomerListItem;
  [key: string]: unknown;
}

export interface CreateInteractionRequest {
  type: string;
  text: string;
  followUpDate?: string | null;
}

export interface InteractionDto {
  id: string;
  customerId: string;
  type: string;
  text: string;
  followUpDate: string | null;
  authorUserId: string | null;
  authorName: string;
  createdAtUtc: string;
}

export interface DueFollowUp {
  interactionId: string;
  customerId: string;
  customerName: string;
  text: string;
  followUpDate: string;
  authorName: string;
}

export type TimelineEventKind =
  | 'EnquiryCaptured'
  | 'NoteAdded'
  | 'StatusChanged'
  | 'OwnerChanged'
  | 'CatalogDispatched'
  | 'ShipmentRecorded'
  | 'InvoiceCreated'
  | 'InvoiceStatusChanged';

export interface TimelineEvent {
  kind: TimelineEventKind;
  occurredAtUtc: string;
  title: string;
  body: string;
  actorUserId: string | null;
  actorName: string | null;
  refType: 'Shipment' | 'CatalogDocument' | 'Invoice' | null;
  refId: string | null;
}
