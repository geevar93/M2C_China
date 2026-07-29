/**
 * Wire contracts for the `/catalog-sections` and `/catalog-documents` API
 * surface (ACTION_PLAN E6-01…E6-08), verified against the real backend
 * (`SourcingOps.Application.Catalog.CatalogDtos`, built concurrently by the
 * backend track). There is deliberately **no `filePath`** anywhere here: the
 * server never exposes where a document lives on disk (TECH_SPEC §8).
 */

import { CategoryRef } from '../../shared/models/lookup-ref.models';

export interface CatalogDocument {
  id: string;
  catalogSectionId: string;
  originalFilename: string;
  sizeBytes: number;
  versionLabel: string | null;
  isLatest: boolean;
  uploadedByUserId: string;
  uploadedByName: string;
  uploadedAt: string;
}

/**
 * CONFIRMED DISAGREEMENT WITH THE PROTOTYPE (see M4 report): the prototype's
 * upload dialog labels its vendor field "Vendor (optional — editable later)"
 * (Source/Sourcing Ops Platform.dc.html ~line 1091), implying a section can
 * exist unassigned and be reassigned afterwards. The real, binding backend
 * contract (`CatalogSectionDto`) makes `VendorId`/`VendorName` **non-nullable
 * `Guid`/`string`**, and `UpdateCatalogSectionRequest` has no `VendorId`
 * parameter at all — a section always belongs to exactly the vendor it was
 * created under, permanently. The frontend follows the real contract: every
 * section requires a vendor at creation and the vendor is read-only
 * thereafter (see `upload-dialog.component.ts`).
 */
export interface CatalogSection {
  id: string;
  vendorId: string;
  vendorName: string;
  title: string;
  category: CategoryRef;
  tags: string[];
  createdAt: string;
  documents: CatalogDocument[];
}

export interface CatalogSectionsListResponse {
  items: CatalogSection[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface CatalogSectionsListParams {
  search?: string;
  page?: number;
  pageSize?: number;
  categoryId?: string;
  vendorId?: string;
  tag?: string;
}

/** Body for `POST /catalog-sections` — `vendorId` is required and immutable once set. */
export interface CreateCatalogSectionRequest {
  vendorId: string;
  title: string;
  categoryId: string;
  tags?: string[];
}

/** Body for `PUT /catalog-sections/{id}` — deliberately has no `vendorId`; the vendor cannot be changed after creation. */
export interface UpdateCatalogSectionRequest {
  title: string;
  categoryId: string;
  tags?: string[];
}
