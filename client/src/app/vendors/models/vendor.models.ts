/**
 * Wire contracts for the `/vendors` API surface (ACTION_PLAN E5-01…E5-09),
 * verified against the real backend (`SourcingOps.Application.Vendors.VendorDtos`,
 * built concurrently by the backend track).
 *
 * Unlike `customer.models.ts` (which sends/receives bare
 * `serviceTypeId`/`statusId`/`categoryIds`), the vendor list/detail response
 * embeds the resolved `StatusRef`/`CategoryRef[]` objects directly — this is
 * the binding contract's shape, carried faithfully rather than normalised to
 * match the customers convention.
 *
 * `contactPerson`/`phone`/`region` are nullable (`string?` on
 * `VendorListItemDto`) even though the vendor-form dialog treats them as
 * required inputs — a stricter client-side validation than the wire format
 * demands is a safe, deliberate UX choice, not a contract mismatch.
 */

import { CategoryRef, StatusRef } from '../../shared/models/lookup-ref.models';
import { CatalogSection } from '../../catalogs/models/catalog.models';

export interface VendorListItem {
  id: string;
  name: string;
  contactPerson: string | null;
  phone: string | null;
  region: string | null;
  categories: CategoryRef[];
  status: StatusRef;
  moq: string | null;
  leadTime: string | null;
  reliabilityRating: number | null;
  catalogCount: number;
}

/** `GET /vendors/{id}` — embeds `catalogSections[]` directly (E5-05); no second call is needed for a vendor's catalog sections/documents. */
export interface VendorDetail extends VendorListItem {
  email: string | null;
  paymentTerms: string | null;
  notes: string | null;
  createdAt: string;
  catalogSections: CatalogSection[];
}

/**
 * `GET /vendors/{id}/documents` row (ACTION_PLAN E5-07/E5-10, FSD Q5). NOT
 * embedded in `VendorDetail` — a separate list call, unlike `catalogSections`
 * (verified against `VendorDocumentDto` in
 * `SourcingOps.Application.Vendors.VendorDocumentDtos`). The lookup field is
 * `docType`, NOT `documentType` — the backend record names it `DocType`,
 * deliberately different from `ShipmentDocumentDto.DocumentType`, so this is
 * not a copy-paste of `ShipmentDocument`. No `versionLabel`/`isLatest`: FSD Q5
 * scopes these as "filed for reference only", without `CatalogDocument`'s
 * version history.
 */
export interface VendorDocument {
  id: string;
  vendorId: string;
  originalFilename: string;
  sizeBytes: number;
  docType: StatusRef;
  uploadedByUserId: string;
  uploadedByName: string;
  uploadedAt: string;
}

export interface VendorsListResponse {
  items: VendorListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface VendorsListParams {
  search?: string;
  page?: number;
  pageSize?: number;
  categoryId?: string;
  region?: string;
  statusId?: string;
}

/**
 * Body for `POST`/`PUT /vendors` — matches `CreateVendorRequest`/
 * `UpdateVendorRequest` exactly (both records have the same shape; the
 * backend distinguishes create vs. update purely by route/verb, not by
 * request shape). `statusId` is required; `categoryIds` is nullable/optional
 * on the wire, but this client always sends an array (possibly empty).
 */
export interface VendorWriteRequest {
  name: string;
  contactPerson?: string | null;
  phone?: string | null;
  email?: string | null;
  region?: string | null;
  categoryIds?: string[];
  statusId: string;
  moq?: string | null;
  leadTime?: string | null;
  paymentTerms?: string | null;
  reliabilityRating?: number | null;
  notes?: string | null;
}
