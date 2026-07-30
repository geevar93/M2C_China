import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CatalogUploadDialogComponent } from './upload-dialog.component';
import { MasterDataResponse } from '../../core/models/master-data.models';
import { CatalogSection } from '../models/catalog.models';

const MASTER_DATA: MasterDataResponse = {
  categories: [{ id: 'cat-jewellery', name: 'Jewellery', sortOrder: 1, isActive: true }],
  serviceTypes: [],
  leadStatuses: [],
  shipmentStatuses: [],
  invoiceStatuses: [],
  vendorStatuses: [],
  documentTypes: []
};

function section(overrides: Partial<CatalogSection> = {}): CatalogSection {
  return {
    id: 'sec-1',
    vendorId: 'ven-1',
    vendorName: 'Yiwu Jewel Craft Co.',
    title: 'Yiwu Jewel Craft — Jewellery — AW26 Catalog',
    category: { id: 'cat-jewellery', name: 'Jewellery' },
    tags: ['NEW ARRIVALS'],
    createdAt: '2026-04-01T00:00:00Z',
    documents: [],
    ...overrides
  };
}

function pdfFile(name = 'catalog.pdf'): File {
  return new File(['%PDF-1.4'], name, { type: 'application/pdf' });
}

describe('CatalogUploadDialogComponent', () => {
  let fixture: ComponentFixture<CatalogUploadDialogComponent>;
  let httpMock: HttpTestingController;

  async function configure(): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [CatalogUploadDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    fixture = TestBed.createComponent(CatalogUploadDialogComponent);
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => httpMock.verify());

  function flushMasterData(): void {
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush(MASTER_DATA);
  }

  describe('vendor-locked (opened from a vendor detail screen)', () => {
    it('loads only that vendor\'s sections and defaults to "existing section" mode', async () => {
      await configure();
      fixture.componentInstance.vendorLock = { id: 'ven-1', name: 'Yiwu Jewel Craft Co.' };
      fixture.detectChanges();
      flushMasterData();

      const req = httpMock.expectOne((r) => r.url === '/api/v1/catalog-sections');
      expect(req.request.params.get('vendorId')).toBe('ven-1');
      req.flush({ items: [section()], page: 1, pageSize: 100, totalCount: 1 });
      fixture.detectChanges();

      expect(fixture.componentInstance.mode()).toBe('existing');
      expect(fixture.componentInstance.selectedSectionId()).toBe('sec-1');
    });

    it('falls back to "new section" mode when the vendor has no existing sections', async () => {
      await configure();
      fixture.componentInstance.vendorLock = { id: 'ven-1', name: 'Yiwu Jewel Craft Co.' };
      fixture.detectChanges();
      flushMasterData();
      httpMock.expectOne((r) => r.url === '/api/v1/catalog-sections').flush({ items: [], page: 1, pageSize: 100, totalCount: 0 });
      fixture.detectChanges();

      expect(fixture.componentInstance.mode()).toBe('new');
    });

    it('requires a PDF file before creating a new section', async () => {
      await configure();
      fixture.componentInstance.vendorLock = { id: 'ven-1', name: 'Yiwu Jewel Craft Co.' };
      fixture.detectChanges();
      flushMasterData();
      httpMock.expectOne((r) => r.url === '/api/v1/catalog-sections').flush({ items: [], page: 1, pageSize: 100, totalCount: 0 });
      fixture.detectChanges();

      const c = fixture.componentInstance;
      c.titleInput.set('New Range 2026');
      c.categoryId.set('cat-jewellery');
      c.save();

      expect(c.error()).toContain('PDF');
      httpMock.expectNone((r) => r.url === '/api/v1/catalog-sections' && r.method === 'POST');
    });

    it('creates a new section then uploads the file to it, vendor-locked', async () => {
      await configure();
      fixture.componentInstance.vendorLock = { id: 'ven-1', name: 'Yiwu Jewel Craft Co.' };
      fixture.detectChanges();
      flushMasterData();
      httpMock.expectOne((r) => r.url === '/api/v1/catalog-sections').flush({ items: [], page: 1, pageSize: 100, totalCount: 0 });
      fixture.detectChanges();

      const c = fixture.componentInstance;
      c.titleInput.set('New Range 2026');
      c.categoryId.set('cat-jewellery');
      c.selectedFile.set(pdfFile());
      c.save();

      const createReq = httpMock.expectOne((r) => r.method === 'POST' && r.url === '/api/v1/catalog-sections');
      expect(createReq.request.body.vendorId).toBe('ven-1');
      expect(createReq.request.body.title).toBe('New Range 2026');
      createReq.flush(section({ id: 'sec-2', title: 'New Range 2026' }));

      const uploadReq = httpMock.expectOne(
        (r) => r.method === 'POST' && r.url === '/api/v1/catalog-sections/sec-2/documents'
      );
      expect(uploadReq.request.body instanceof FormData).toBeTrue();
      uploadReq.flush({
        id: 'doc-1',
        catalogSectionId: 'sec-2',
        originalFilename: 'catalog.pdf',
        sizeBytes: 100,
        versionLabel: 'v1',
        isLatest: true,
        uploadedByUserId: 'user-1',
        uploadedByName: 'Priya S.',
        uploadedAt: '2026-07-28T00:00:00Z'
      });

      const refetchReq = httpMock.expectOne((r) => r.method === 'GET' && r.url === '/api/v1/catalog-sections/sec-2');
      const savedSection = section({ id: 'sec-2', title: 'New Range 2026' });
      refetchReq.flush(savedSection);

      expect(c.saving()).toBeFalse();
    });
  });

  describe('unlocked (opened from the Catalogs screen)', () => {
    it('loads a vendor list for the required vendor picker', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      httpMock.expectOne((r) => r.url === '/api/v1/catalog-sections').flush({ items: [], page: 1, pageSize: 100, totalCount: 0 });
      httpMock
        .expectOne((r) => r.url === '/api/v1/vendors')
        .flush({ items: [{ id: 'ven-1', name: 'Yiwu Jewel Craft Co.' }], page: 1, pageSize: 200, totalCount: 1 });
      fixture.detectChanges();

      expect(fixture.componentInstance.vendorOptions()).toEqual([{ id: 'ven-1', name: 'Yiwu Jewel Craft Co.' }]);
    });

    it('requires a vendor before creating a new section (CatalogSectionDto.VendorId is non-nullable)', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      httpMock.expectOne((r) => r.url === '/api/v1/catalog-sections').flush({ items: [], page: 1, pageSize: 100, totalCount: 0 });
      httpMock.expectOne((r) => r.url === '/api/v1/vendors').flush({ items: [], page: 1, pageSize: 200, totalCount: 0 });
      fixture.detectChanges();

      const c = fixture.componentInstance;
      c.titleInput.set('New Range 2026');
      c.categoryId.set('cat-jewellery');
      c.selectedFile.set(pdfFile());
      c.save();

      expect(c.error()).toContain('vendor');
      httpMock.expectNone((r) => r.method === 'POST' && r.url === '/api/v1/catalog-sections');
    });
  });

  describe('editing an existing section', () => {
    it('prefills title/category/tags/vendor and makes the file optional', async () => {
      await configure();
      fixture.componentInstance.editSection = section();
      fixture.detectChanges();
      flushMasterData();
      // No vendor list load in edit mode — the vendor is immutable once a
      // section exists (UpdateCatalogSectionRequest has no vendorId field).
      fixture.detectChanges();

      const c = fixture.componentInstance;
      expect(c.titleInput()).toBe('Yiwu Jewel Craft — Jewellery — AW26 Catalog');
      expect(c.categoryId()).toBe('cat-jewellery');
      expect(c.tagsInput()).toBe('NEW ARRIVALS');
      expect(c.lockedVendorName()).toBe('Yiwu Jewel Craft Co.');

      c.titleInput.set('Renamed Section');
      c.save();

      const req = httpMock.expectOne((r) => r.method === 'PUT' && r.url === '/api/v1/catalog-sections/sec-1');
      expect(req.request.body.title).toBe('Renamed Section');
      expect(req.request.body.vendorId).toBeUndefined();
      req.flush(section({ title: 'Renamed Section' }));

      expect(c.saving()).toBeFalse();
    });
  });
});
