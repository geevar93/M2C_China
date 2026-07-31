import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CustomersComponent } from './customers.component';
import { MasterDataResponse } from '../core/models/master-data.models';
import { CustomerListItem } from './models/customer.models';

const MASTER_DATA: MasterDataResponse = {
  categories: [
    { id: 'cat-jewellery', name: 'Jewellery', sortOrder: 1, isActive: true },
    { id: 'cat-handbags', name: 'Handbags', sortOrder: 2, isActive: true }
  ],
  serviceTypes: [
    { id: 'svc-cif', code: 'CIF', label: 'CIF', sortOrder: 1, isActive: true },
    { id: 'svc-freight', code: 'FREIGHT_ONLY', label: 'Freight-only', sortOrder: 2, isActive: true }
  ],
  // A retired lead status — DoD requires a customer referencing it still renders its label.
  leadStatuses: [
    { id: 'lead-new', code: 'NEW', label: 'New', sortOrder: 1, isActive: true },
    { id: 'lead-dormant-old', code: 'DORMANT', label: 'Dormant (legacy)', sortOrder: 2, isActive: false }
  ],
  shipmentStatuses: [],
  invoiceStatuses: [],
  vendorStatuses: [],
  documentTypes: []
};

function customer(overrides: Partial<CustomerListItem> = {}): CustomerListItem {
  return {
    id: 'cust-1',
    name: 'Meena Shah',
    businessName: 'Meena Traders',
    phone: '+91 98250 41122',
    city: 'Surat',
    region: null,
    sourceChannel: 'WhatsApp',
    serviceTypeId: 'svc-cif',
    statusId: 'lead-new',
    categoryIds: ['cat-jewellery'],
    ownerUserId: 'user-1',
    ownerName: 'Priya Sharma',
    tags: [],
    createdAt: '2026-02-11T00:00:00Z',
    ...overrides
  };
}

describe('CustomersComponent', () => {
  let fixture: ComponentFixture<CustomersComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CustomersComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    }).compileComponents();

    fixture = TestBed.createComponent(CustomersComponent);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function flushMasterData(): void {
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush(MASTER_DATA);
  }

  function flushList(items: CustomerListItem[], totalCount = items.length): void {
    httpMock.expectOne((r) => r.url === '/api/v1/customers').flush({ items, page: 1, pageSize: 25, totalCount });
  }

  it('populates the service-type/status/category filter dropdowns from MasterDataService, not a hard-coded list', () => {
    fixture.detectChanges();
    flushMasterData();
    flushList([]);
    fixture.detectChanges();

    const html: string = fixture.nativeElement.innerHTML;
    expect(html).toContain('Jewellery');
    expect(html).toContain('Handbags');
    expect(html).toContain('CIF');
    expect(html).toContain('Freight-only');
    expect(html).toContain('New');
  });

  it('renders the label of a customer whose statusId points at a retired status', () => {
    fixture.detectChanges();
    flushMasterData();
    flushList([customer({ statusId: 'lead-dormant-old' })]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Dormant (legacy)');
  });

  it('sends the service-type filter as a query param when changed', () => {
    fixture.detectChanges();
    flushMasterData();
    flushList([]);
    fixture.detectChanges();

    fixture.componentInstance.setServiceType('svc-freight');

    const req = httpMock.expectOne((r) => r.url === '/api/v1/customers');
    expect(req.request.params.get('serviceTypeId')).toBe('svc-freight');
    req.flush({ items: [], page: 1, pageSize: 25, totalCount: 0 });
  });

  it('debounces the search box and sends it as the search query param', fakeAsync(() => {
    fixture.detectChanges();
    flushMasterData();
    flushList([]);
    fixture.detectChanges();

    fixture.componentInstance.onSearchInput('meena');
    tick(350);

    const req = httpMock.expectOne((r) => r.url === '/api/v1/customers');
    expect(req.request.params.get('search')).toBe('meena');
    req.flush({ items: [], page: 1, pageSize: 25, totalCount: 0 });
  }));

  it('shows a visible error instead of hanging when the list request fails', () => {
    fixture.detectChanges();
    flushMasterData();
    httpMock.expectOne((r) => r.url === '/api/v1/customers').flush(
      { title: 'Server error', detail: 'Lookup failed' },
      { status: 500, statusText: 'Server Error' }
    );
    fixture.detectChanges();

    expect(fixture.componentInstance.loading()).toBeFalse();
    expect(fixture.componentInstance.error()).toBe('Lookup failed');
    expect(fixture.nativeElement.textContent).toContain('Lookup failed');
  });

  it('shows the empty-state message when no customers match the filters', () => {
    fixture.detectChanges();
    flushMasterData();
    flushList([]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No customers match these filters');
  });

  describe('tags (E4-11)', () => {
    it('renders each customer\'s tags as chips in the list', () => {
      fixture.detectChanges();
      flushMasterData();
      flushList([customer({ tags: ['VIP', 'Repeat Buyer'] })]);
      fixture.detectChanges();

      const text: string = fixture.nativeElement.textContent;
      expect(text).toContain('VIP');
      expect(text).toContain('Repeat Buyer');
    });

    it('sends the tag filter as a query param when a tag chip is clicked', () => {
      fixture.detectChanges();
      flushMasterData();
      flushList([customer({ tags: ['VIP'] })]);
      fixture.detectChanges();

      fixture.componentInstance.setTag('VIP');

      const req = httpMock.expectOne((r) => r.url === '/api/v1/customers');
      expect(req.request.params.get('tag')).toBe('VIP');
      req.flush({ items: [], page: 1, pageSize: 25, totalCount: 0 });
    });

    it('debounces free-text tag input and sends it as the tag query param', fakeAsync(() => {
      fixture.detectChanges();
      flushMasterData();
      flushList([]);
      fixture.detectChanges();

      fixture.componentInstance.onTagInput('vip');
      tick(350);

      const req = httpMock.expectOne((r) => r.url === '/api/v1/customers');
      expect(req.request.params.get('tag')).toBe('vip');
      req.flush({ items: [], page: 1, pageSize: 25, totalCount: 0 });
    }));

    it('clears the tag filter and refetches without it', () => {
      fixture.detectChanges();
      flushMasterData();
      flushList([]);
      fixture.detectChanges();

      fixture.componentInstance.setTag('VIP');
      httpMock.expectOne((r) => r.url === '/api/v1/customers').flush({ items: [], page: 1, pageSize: 25, totalCount: 0 });

      fixture.componentInstance.clearTag();

      const req = httpMock.expectOne((r) => r.url === '/api/v1/customers');
      expect(req.request.params.has('tag')).toBeFalse();
      req.flush({ items: [], page: 1, pageSize: 25, totalCount: 0 });
    });
  });
});
