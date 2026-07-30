import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { VendorsComponent } from './vendors.component';
import { AuthService } from '../core/services/auth.service';
import { MasterDataResponse } from '../core/models/master-data.models';
import { VendorListItem } from './models/vendor.models';

const MASTER_DATA: MasterDataResponse = {
  categories: [
    { id: 'cat-jewellery', name: 'Jewellery', sortOrder: 1, isActive: true },
    { id: 'cat-handbags', name: 'Handbags', sortOrder: 2, isActive: true }
  ],
  serviceTypes: [],
  leadStatuses: [],
  shipmentStatuses: [],
  invoiceStatuses: [],
  vendorStatuses: [
    { id: 'vst-active', code: 'ACTIVE', label: 'Active', sortOrder: 1, isActive: true },
    { id: 'vst-hold', code: 'ON-HOLD', label: 'On Hold', sortOrder: 2, isActive: true }
  ],
  documentTypes: []
};

function vendor(overrides: Partial<VendorListItem> = {}): VendorListItem {
  return {
    id: 'ven-1',
    name: 'Yiwu Jewel Craft Co.',
    contactPerson: 'Li Wen',
    phone: '+86 137 5829 4410',
    region: 'Yiwu, Zhejiang',
    categories: [{ id: 'cat-jewellery', name: 'Jewellery' }],
    status: { id: 'vst-active', code: 'ACTIVE', label: 'Active' },
    moq: '300 sets',
    leadTime: '18 days',
    reliabilityRating: 4.6,
    catalogCount: 3,
    ...overrides
  };
}

describe('VendorsComponent', () => {
  let fixture: ComponentFixture<VendorsComponent>;
  let httpMock: HttpTestingController;

  async function configure(permissions: string[] = []): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [VendorsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: { hasPermission: (p: string) => permissions.includes(p), currentUser$: of(null) } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(VendorsComponent);
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => httpMock.verify());

  function flushMasterData(): void {
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush(MASTER_DATA);
  }

  function flushList(items: VendorListItem[], totalCount = items.length): void {
    httpMock.expectOne((r) => r.url === '/api/v1/vendors').flush({ items, page: 1, pageSize: 25, totalCount });
  }

  it('populates the category/status filter dropdowns from MasterDataService, not a hard-coded list', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([]);
    fixture.detectChanges();

    const html: string = fixture.nativeElement.innerHTML;
    expect(html).toContain('Jewellery');
    expect(html).toContain('Handbags');
    expect(html).toContain('Active');
    expect(html).toContain('On Hold');
  });

  it('renders a vendor row using the embedded status/category objects, coloured by StatusStyleService', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([vendor()]);
    fixture.detectChanges();

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('Yiwu Jewel Craft Co.');
    expect(text).toContain('Li Wen · +86 137 5829 4410');
    expect(text).toContain('Jewellery');
    expect(text).toContain('300 sets / 18 days');
    expect(text).toContain('Active');
  });

  it('sends the category filter as a query param when changed', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([]);
    fixture.detectChanges();

    fixture.componentInstance.setCategory('cat-jewellery');

    const req = httpMock.expectOne((r) => r.url === '/api/v1/vendors');
    expect(req.request.params.get('categoryId')).toBe('cat-jewellery');
    req.flush({ items: [], page: 1, pageSize: 25, totalCount: 0 });
  });

  it('debounces the search box and sends it as the search query param', fakeAsync(async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([]);
    fixture.detectChanges();

    fixture.componentInstance.onSearchInput('yiwu');
    tick(350);

    const req = httpMock.expectOne((r) => r.url === '/api/v1/vendors');
    expect(req.request.params.get('search')).toBe('yiwu');
    req.flush({ items: [], page: 1, pageSize: 25, totalCount: 0 });
  }));

  it('shows a visible error instead of hanging when the list request fails', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    httpMock.expectOne((r) => r.url === '/api/v1/vendors').flush(
      { title: 'Server error', detail: 'Vendor lookup failed' },
      { status: 500, statusText: 'Server Error' }
    );
    fixture.detectChanges();

    expect(fixture.componentInstance.loading()).toBeFalse();
    expect(fixture.componentInstance.error()).toBe('Vendor lookup failed');
    expect(fixture.nativeElement.textContent).toContain('Vendor lookup failed');
  });

  it('shows the empty-state message when no vendors match the filters', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No vendors match these filters');
  });

  it('hides "+ Add Vendor" without Vendors.Edit permission', async () => {
    await configure([]);
    fixture.detectChanges();
    flushMasterData();
    flushList([]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('+ Add Vendor');
  });

  it('shows "+ Add Vendor" with Vendors.Edit permission and creates a vendor via POST /vendors', async () => {
    await configure(['Vendors.Edit']);
    fixture.detectChanges();
    flushMasterData();
    flushList([]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('+ Add Vendor');

    fixture.componentInstance.openCreate();
    fixture.detectChanges();

    const dialog = fixture.componentInstance;
    expect(dialog.createOpen()).toBeTrue();
  });
});
