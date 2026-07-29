import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { CatalogsComponent } from './catalogs.component';
import { AuthService } from '../core/services/auth.service';
import { MasterDataResponse } from '../core/models/master-data.models';
import { CatalogSection } from './models/catalog.models';

const MASTER_DATA: MasterDataResponse = {
  categories: [
    { id: 'cat-jewellery', name: 'Jewellery', sortOrder: 1, isActive: true },
    { id: 'cat-electronics', name: 'Electronics', sortOrder: 2, isActive: true }
  ],
  serviceTypes: [],
  leadStatuses: [],
  shipmentStatuses: [],
  invoiceStatuses: [],
  vendorStatuses: []
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
    documents: [
      {
        id: 'doc-1',
        catalogSectionId: 'sec-1',
        originalFilename: 'yiwu-jewel-craft-aw26-v3.pdf',
        sizeBytes: 8800000,
        versionLabel: 'v3',
        isLatest: true,
        uploadedByUserId: 'user-1',
        uploadedByName: 'Priya S.',
        uploadedAt: '2026-07-14T00:00:00Z'
      }
    ],
    ...overrides
  };
}

describe('CatalogsComponent', () => {
  let fixture: ComponentFixture<CatalogsComponent>;
  let httpMock: HttpTestingController;

  async function configure(permissions: string[] = []): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [CatalogsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: { hasPermission: (p: string) => permissions.includes(p), currentUser$: of(null) } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(CatalogsComponent);
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => httpMock.verify());

  function flushMasterData(): void {
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush(MASTER_DATA);
  }

  function flushList(items: CatalogSection[], totalCount = items.length): void {
    httpMock.expectOne((r) => r.url === '/api/v1/catalog-sections').flush({ items, page: 1, pageSize: 24, totalCount });
  }

  it('populates the category filter dropdown from MasterDataService, not a hard-coded list', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([]);
    fixture.detectChanges();

    const html: string = fixture.nativeElement.innerHTML;
    expect(html).toContain('Jewellery');
    expect(html).toContain('Electronics');
  });

  it('renders a card with a linked vendor, category, doc count and the latest document', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([section()]);
    fixture.detectChanges();

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('Yiwu Jewel Craft — Jewellery — AW26 Catalog');
    expect(text).toContain('Yiwu Jewel Craft Co.');
    expect(text).toContain('Jewellery');
    expect(text).toContain('1 doc');
    expect(text).toContain('yiwu-jewel-craft-aw26-v3.pdf');

    const link: HTMLAnchorElement = fixture.nativeElement.querySelector('a[href="/vendors/ven-1"]');
    expect(link).toBeTruthy();
  });

  it('renders a neutral cover placeholder instead of an image (no coverImage field in the API)', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([section()]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('img')).toBeFalsy();
    expect(fixture.nativeElement.querySelector('.catalog-cover-placeholder')).toBeTruthy();
  });

  it('sends the category filter as a query param when changed', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([]);
    fixture.detectChanges();

    fixture.componentInstance.setCategory('cat-jewellery');

    const req = httpMock.expectOne((r) => r.url === '/api/v1/catalog-sections');
    expect(req.request.params.get('categoryId')).toBe('cat-jewellery');
    req.flush({ items: [], page: 1, pageSize: 24, totalCount: 0 });
  });

  it('debounces the search box and sends it as the search query param', fakeAsync(async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([]);
    fixture.detectChanges();

    fixture.componentInstance.onSearchInput('yiwu');
    tick(350);

    const req = httpMock.expectOne((r) => r.url === '/api/v1/catalog-sections');
    expect(req.request.params.get('search')).toBe('yiwu');
    req.flush({ items: [], page: 1, pageSize: 24, totalCount: 0 });
  }));

  it('shows a visible error instead of hanging when the list request fails', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    httpMock.expectOne((r) => r.url === '/api/v1/catalog-sections').flush(
      { title: 'Server error', detail: 'Catalog lookup failed' },
      { status: 500, statusText: 'Server Error' }
    );
    fixture.detectChanges();

    expect(fixture.componentInstance.loading()).toBeFalse();
    expect(fixture.componentInstance.error()).toBe('Catalog lookup failed');
    expect(fixture.nativeElement.textContent).toContain('Catalog lookup failed');
  });

  it('shows the empty-state message when nothing matches the filters', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No catalog sections match these filters');
  });

  it('renders the "Send via WhatsApp" control as inert (E9 is out of scope)', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([section()]);
    fixture.detectChanges();

    const btn: HTMLButtonElement = fixture.nativeElement.querySelector('[aria-label="Send via WhatsApp"]');
    expect(btn.disabled).toBeTrue();
  });

  it('previews the latest document through the authenticated download endpoint', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([section()]);
    fixture.detectChanges();

    spyOn(window, 'open').and.stub();
    spyOn(URL, 'createObjectURL').and.returnValue('blob:mock-url');

    fixture.componentInstance.previewDocument('doc-1');

    const req = httpMock.expectOne((r) => r.url === '/api/v1/catalog-documents/doc-1/download');
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['%PDF-1.4'], { type: 'application/pdf' }));
  });

  it('hides "+ Upload Catalog PDF" and per-card "Edit" without Catalogs.Edit', async () => {
    await configure([]);
    fixture.detectChanges();
    flushMasterData();
    flushList([section()]);
    fixture.detectChanges();

    const text: string = fixture.nativeElement.textContent;
    expect(text).not.toContain('+ Upload Catalog PDF');
    expect(text).not.toContain('Edit');
  });

  it('opens the upload dialog (no vendor lock) with Catalogs.Edit, fetching sections and vendors for its pickers', async () => {
    await configure(['Catalogs.Edit']);
    fixture.detectChanges();
    flushMasterData();
    flushList([]);
    fixture.detectChanges();

    fixture.componentInstance.openUpload();
    fixture.detectChanges();

    httpMock
      .expectOne((r) => r.url === '/api/v1/catalog-sections')
      .flush({ items: [], page: 1, pageSize: 100, totalCount: 0 });
    httpMock.expectOne((r) => r.url === '/api/v1/vendors').flush({ items: [], page: 1, pageSize: 200, totalCount: 0 });
    fixture.detectChanges();

    expect(fixture.componentInstance.uploadOpen()).toBeTrue();
    expect(fixture.componentInstance.editSection()).toBeNull();
  });

  it('opens the upload dialog pre-locked to a section via "Edit"', async () => {
    await configure(['Catalogs.Edit']);
    fixture.detectChanges();
    flushMasterData();
    const s = section();
    flushList([s]);
    fixture.detectChanges();

    fixture.componentInstance.openEdit(s);
    fixture.detectChanges();

    // Edit mode never loads a vendor list — the vendor is immutable once a
    // section exists (UpdateCatalogSectionRequest has no vendorId field), so
    // there is nothing to pick from.

    expect(fixture.componentInstance.uploadOpen()).toBeTrue();
    expect(fixture.componentInstance.editSection()).toEqual(s);
  });
});
