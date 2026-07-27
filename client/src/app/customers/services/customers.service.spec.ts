import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CustomersService } from './customers.service';
import { CustomerDetail, CustomerListItem } from '../models/customer.models';

describe('CustomersService', () => {
  let service: CustomersService;
  let httpMock: HttpTestingController;

  const listItem: CustomerListItem = {
    id: 'cust-1',
    name: 'Meena Shah',
    businessName: 'Meena Traders',
    phone: '+91 98250 41122',
    city: 'Surat',
    region: null,
    sourceChannel: 'WhatsApp',
    serviceTypeId: 'svc-cif',
    statusId: 'lead-active',
    categoryIds: ['cat-1'],
    ownerUserId: 'user-1',
    ownerName: 'Priya Sharma',
    tags: [],
    createdAt: '2026-02-11T00:00:00Z'
  };

  const detail: CustomerDetail = {
    ...listItem,
    email: null,
    notes: null,
    externalMarketplace: null,
    externalOrderRef: null,
    externalSupplierName: null,
    externalOrderValue: null,
    externalOrderCurrency: null,
    externalOrderDate: null
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(CustomersService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('sends filters as query params on GET /customers', () => {
    service
      .list({ search: 'meena', page: 2, pageSize: 25, statusId: 'lead-active', serviceTypeId: 'svc-cif', categoryId: 'cat-1' })
      .subscribe();

    const req = httpMock.expectOne(
      (r) =>
        r.url === '/api/v1/customers' &&
        r.params.get('search') === 'meena' &&
        r.params.get('page') === '2' &&
        r.params.get('pageSize') === '25' &&
        r.params.get('statusId') === 'lead-active' &&
        r.params.get('serviceTypeId') === 'svc-cif' &&
        r.params.get('categoryId') === 'cat-1'
    );
    expect(req.request.method).toBe('GET');
    req.flush({ items: [listItem], page: 2, pageSize: 25, totalCount: 1 });
  });

  it('omits undefined filters from the query string', () => {
    service.list({ page: 1, pageSize: 25 }).subscribe();
    const req = httpMock.expectOne((r) => r.url === '/api/v1/customers');
    expect(req.request.params.has('search')).toBeFalse();
    expect(req.request.params.has('statusId')).toBeFalse();
    req.flush({ items: [], page: 1, pageSize: 25, totalCount: 0 });
  });

  it('gets a single customer by id', () => {
    let result: CustomerDetail | undefined;
    service.getById('cust-1').subscribe((r) => (result = r));
    const req = httpMock.expectOne('/api/v1/customers/cust-1');
    expect(req.request.method).toBe('GET');
    req.flush(detail);
    expect(result?.id).toBe('cust-1');
  });

  it('POSTs a create request without confirmDuplicate by default', () => {
    service
      .create({
        name: 'Meena Shah',
        businessName: 'Meena Traders',
        phone: '+91 9825041122',
        sourceChannel: 'WhatsApp',
        serviceTypeId: 'svc-cif',
        statusId: 'lead-new',
        categoryIds: []
      })
      .subscribe();

    const req = httpMock.expectOne('/api/v1/customers');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.confirmDuplicate).toBeUndefined();
    req.flush(detail, { status: 201, statusText: 'Created' });
  });

  it('surfaces a 409 duplicate-phone error with the existing customer summary intact for the caller to inspect', () => {
    let caught: unknown;
    service
      .create({
        name: 'Meena Shah',
        businessName: 'Meena Traders',
        phone: '+91 9825041122',
        sourceChannel: 'WhatsApp',
        serviceTypeId: 'svc-cif',
        statusId: 'lead-new',
        categoryIds: []
      })
      .subscribe({ error: (err) => (caught = err) });

    const req = httpMock.expectOne('/api/v1/customers');
    req.flush(
      { title: 'Conflict', detail: 'Phone already in use', existingCustomer: listItem },
      { status: 409, statusText: 'Conflict' }
    );

    expect(caught).toBeTruthy();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    expect((caught as any).error.existingCustomer.id).toBe('cust-1');
  });

  it('retries with confirmDuplicate: true when the caller asks for it', () => {
    service
      .create({
        name: 'Meena Shah',
        businessName: 'Meena Traders',
        phone: '+91 9825041122',
        sourceChannel: 'WhatsApp',
        serviceTypeId: 'svc-cif',
        statusId: 'lead-new',
        categoryIds: [],
        confirmDuplicate: true
      })
      .subscribe();

    const req = httpMock.expectOne('/api/v1/customers');
    expect(req.request.body.confirmDuplicate).toBeTrue();
    req.flush(detail, { status: 201, statusText: 'Created' });
  });

  it('gets the timeline for a customer', () => {
    service.getTimeline('cust-1').subscribe();
    const req = httpMock.expectOne('/api/v1/customers/cust-1/timeline');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('posts a new interaction', () => {
    service.addInteraction('cust-1', { type: 'Note', text: 'Called back' }).subscribe();
    const req = httpMock.expectOne('/api/v1/customers/cust-1/interactions');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ type: 'Note', text: 'Called back' });
    req.flush({
      id: 'int-1',
      customerId: 'cust-1',
      type: 'Note',
      text: 'Called back',
      followUpDate: null,
      authorUserId: 'user-1',
      authorName: 'Priya Sharma',
      createdAtUtc: '2026-07-27T10:00:00Z'
    });
  });
});
