import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { CustomerDetailComponent } from './customer-detail.component';
import { AuthService } from '../../core/services/auth.service';
import { MasterDataResponse } from '../../core/models/master-data.models';
import { CustomerDetail, TimelineEvent } from '../models/customer.models';

const MASTER_DATA: MasterDataResponse = {
  categories: [{ id: 'cat-jewellery', name: 'Jewellery', sortOrder: 1, isActive: true }],
  serviceTypes: [{ id: 'svc-cif', code: 'CIF', label: 'CIF', sortOrder: 1, isActive: true }],
  leadStatuses: [
    { id: 'lead-active', code: 'ACTIVE', label: 'Active', sortOrder: 1, isActive: true },
    { id: 'lead-retired', code: 'DORMANT', label: 'Dormant (legacy)', sortOrder: 2, isActive: false }
  ],
  shipmentStatuses: [],
  invoiceStatuses: [],
  vendorStatuses: [],
  documentTypes: []
};

const detail: CustomerDetail = {
  id: 'cust-1',
  name: 'Meena Shah',
  businessName: 'Meena Traders',
  phone: '+91 98250 41122',
  city: 'Surat',
  region: null,
  sourceChannel: 'WhatsApp',
  serviceTypeId: 'svc-cif',
  statusId: 'lead-retired',
  categoryIds: ['cat-jewellery'],
  ownerUserId: 'user-1',
  ownerName: 'Priya Sharma',
  tags: [],
  createdAt: '2026-02-11T00:00:00Z',
  email: null,
  notes: null,
  externalMarketplace: null,
  externalOrderRef: null,
  externalSupplierName: null,
  externalOrderValue: null,
  externalOrderCurrency: null,
  externalOrderDate: null
};

const timeline: TimelineEvent[] = [
  {
    kind: 'CatalogDispatched',
    occurredAtUtc: '2026-07-18T14:22:00Z',
    title: 'Catalog dispatched',
    body: 'yiwu-jewel-craft-aw26-v3.pdf sent over WhatsApp by Priya Sharma',
    actorUserId: 'user-1',
    actorName: 'Priya Sharma',
    refType: 'CatalogDocument',
    refId: 'doc-1'
  },
  {
    kind: 'StatusChanged',
    occurredAtUtc: '2026-06-02T09:40:00Z',
    title: 'Status changed',
    body: 'QUALIFIED → ACTIVE by Priya Sharma',
    actorUserId: 'user-1',
    actorName: 'Priya Sharma',
    refType: null,
    refId: null
  }
];

describe('CustomerDetailComponent', () => {
  let fixture: ComponentFixture<CustomerDetailComponent>;
  let httpMock: HttpTestingController;

  function configure(id = 'cust-1', permissions: string[] = []): void {
    TestBed.configureTestingModule({
      imports: [CustomerDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: of(convertToParamMap({ id })),
            snapshot: { paramMap: convertToParamMap({ id }) }
          }
        },
        { provide: AuthService, useValue: { hasPermission: (p: string) => permissions.includes(p), currentUser$: of(null) } }
      ]
    });
    fixture = TestBed.createComponent(CustomerDetailComponent);
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => httpMock.verify());

  function flushAll(): void {
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush(MASTER_DATA);
    httpMock.expectOne((r) => r.url === '/api/v1/customers/cust-1').flush(detail);
    httpMock.expectOne((r) => r.url === '/api/v1/customers/cust-1/timeline').flush(timeline);
  }

  it('renders the label of a retired status via MasterDataService (not a hard-coded map)', () => {
    configure();
    fixture.detectChanges();
    flushAll();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Dormant (legacy)');
  });

  it('colours each timeline entry by kind and formats occurredAtUtc for display', () => {
    configure();
    fixture.detectChanges();
    flushAll();
    fixture.detectChanges();

    const rows = fixture.componentInstance.timeline();
    expect(rows[0].dot).toBe('#2d5be3'); // CatalogDispatched
    expect(rows[1].dot).toBe('#2e7d32'); // StatusChanged

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('18 Jul 2026');
    expect(text).toContain('Catalog dispatched');
  });

  it('shows a visible error instead of hanging when the customer fails to load', () => {
    configure();
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush(MASTER_DATA);
    // Flush the sibling timeline request first (forkJoin still errors overall
    // once the customer request below fails) — flushing it after the error
    // races forkJoin's teardown unsubscribe of the sibling subscription.
    httpMock.expectOne((r) => r.url === '/api/v1/customers/cust-1/timeline').flush([]);
    httpMock
      .expectOne((r) => r.url === '/api/v1/customers/cust-1')
      .flush({ title: 'Not Found', detail: 'Customer not found' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(fixture.componentInstance.loading()).toBeFalse();
    expect(fixture.componentInstance.error()).toBe('Customer not found');
    expect(fixture.nativeElement.textContent).toContain('Customer not found');
  });

  it('renders the customer\'s tags in the profile card (E4-11)', () => {
    configure();
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush(MASTER_DATA);
    httpMock.expectOne((r) => r.url === '/api/v1/customers/cust-1').flush({ ...detail, tags: ['VIP', 'Repeat Buyer'] });
    httpMock.expectOne((r) => r.url === '/api/v1/customers/cust-1/timeline').flush(timeline);
    fixture.detectChanges();

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('Tags');
    expect(text).toContain('VIP, Repeat Buyer');
  });

  it('posts a note and reloads the timeline on save', () => {
    configure();
    fixture.detectChanges();
    flushAll();
    fixture.detectChanges();

    fixture.componentInstance.openNote();
    fixture.componentInstance.noteText.set('Called back, wants pricing');
    fixture.componentInstance.saveNote();

    const postReq = httpMock.expectOne((r) => r.url === '/api/v1/customers/cust-1/interactions');
    expect(postReq.request.body).toEqual({ type: 'Note', text: 'Called back, wants pricing' });
    postReq.flush({
      id: 'int-1',
      customerId: 'cust-1',
      type: 'Note',
      text: 'Called back, wants pricing',
      followUpDate: null,
      authorUserId: 'user-1',
      authorName: 'Priya Sharma',
      createdAtUtc: '2026-07-27T10:00:00Z'
    });

    httpMock.expectOne((r) => r.url === '/api/v1/customers/cust-1/timeline').flush(timeline);

    expect(fixture.componentInstance.noteOpen()).toBeFalse();
  });

  it('disables "Send Catalog via WhatsApp" without Dispatch.Send (E9-03)', () => {
    configure('cust-1', []);
    fixture.detectChanges();
    flushAll();
    fixture.detectChanges();

    const btn: HTMLButtonElement = fixture.nativeElement.querySelector('.detail-actions .btn-primary');
    expect(btn.disabled).toBeTrue();
  });

  it('opens the dispatch dialog, customer-locked, with Dispatch.Send and reloads the timeline once logged', () => {
    configure('cust-1', ['Dispatch.Send']);
    fixture.detectChanges();
    flushAll();
    fixture.detectChanges();

    const btn: HTMLButtonElement = fixture.nativeElement.querySelector('.detail-actions .btn-primary');
    expect(btn.disabled).toBeFalse();

    fixture.componentInstance.openDispatch();
    fixture.detectChanges();

    expect(fixture.componentInstance.dispatchCustomerLock()).toEqual({
      id: 'cust-1',
      businessName: 'Meena Traders',
      subline: 'Meena Shah · +91 98250 41122',
      serviceTypeCode: 'CIF'
    });

    // The dialog's own child-load requests (catalog sections, since no
    // documentLock was supplied) — not under test here, just drained so
    // httpMock.verify() doesn't fail the outer spec.
    httpMock.expectOne((r) => r.url === '/api/v1/catalog-sections').flush({ items: [], page: 1, pageSize: 100, totalCount: 0 });
    fixture.detectChanges();

    fixture.componentInstance.onDispatchLogged();
    expect(fixture.componentInstance.dispatchOpen()).toBeFalse();

    // Reloads the timeline (the dispatch's own history surface — no second, parallel dispatch list).
    httpMock.expectOne((r) => r.url === '/api/v1/customers/cust-1/timeline').flush(timeline);
  });
});
