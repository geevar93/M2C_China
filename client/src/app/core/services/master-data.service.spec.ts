import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MasterDataService } from './master-data.service';
import { MasterDataResponse } from '../models/master-data.models';

/**
 * Unit tests for MasterDataService (ACTION_PLAN E3-10). Uses HttpTestingController
 * against the fixed API contract in the E3-10 task brief — the real backend
 * endpoint does not exist yet, so these mock the response shape verbatim.
 */
describe('MasterDataService', () => {
  let service: MasterDataService;
  let httpMock: HttpTestingController;

  const mockResponse: MasterDataResponse = {
    categories: [
      { id: 'cat-active', name: 'Jewellery', sortOrder: 1, isActive: true },
      { id: 'cat-retired', name: 'Discontinued Line', sortOrder: 2, isActive: false }
    ],
    serviceTypes: [{ id: 'svc-cif', code: 'CIF', label: 'CIF', sortOrder: 1, isActive: true }],
    leadStatuses: [
      { id: 'lead-qualified', code: 'QUALIFIED', label: 'Qualified', sortOrder: 2, isActive: true },
      { id: 'lead-new', code: 'NEW', label: 'New', sortOrder: 1, isActive: true },
      { id: 'lead-retired', code: 'ARCHIVED', label: 'Archived', sortOrder: 3, isActive: false }
    ],
    shipmentStatuses: [{ id: 'ship-transit', code: 'IN TRANSIT', label: 'In Transit', sortOrder: 3, isActive: true }],
    invoiceStatuses: [{ id: 'inv-draft', code: 'DRAFT', label: 'Draft', sortOrder: 1, isActive: true }],
    vendorStatuses: [{ id: 'ven-active', code: 'ACTIVE', label: 'Active', sortOrder: 1, isActive: true }],
    documentTypes: [
      { id: 'doc-licence', code: 'BUSINESS_LICENCE', label: 'Business Licence', sortOrder: 1, isActive: true, scope: 'Vendor' },
      { id: 'doc-packing-list', code: 'PACKING_LIST', label: 'Packing List', sortOrder: 1, isActive: true, scope: 'Shipment' }
    ]
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(MasterDataService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function flushOnce(response: MasterDataResponse = mockResponse): void {
    const req = httpMock.expectOne((r) => r.url === '/api/v1/master-data');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('includeRetired')).toBe('true');
    req.flush(response);
  }

  it('serves multiple concurrent consumers from a single HTTP call', () => {
    let categories: unknown;
    let leadStatuses: unknown;
    let vendorStatuses: unknown;

    service.categoryOptions().subscribe((r) => (categories = r));
    service.leadStatusOptions().subscribe((r) => (leadStatuses = r));
    service.vendorStatusOptions().subscribe((r) => (vendorStatuses = r));

    // httpMock.expectOne throws if more than one matching request was made —
    // this is the assertion that three consumers triggered exactly one call.
    flushOnce();

    expect(categories).toBeTruthy();
    expect(leadStatuses).toBeTruthy();
    expect(vendorStatuses).toBeTruthy();
  });

  it('resolves a retired row by id so an existing record can still render its label', () => {
    let result: { id: string; name: string; isActive: boolean } | undefined;
    service.categoryById('cat-retired').subscribe((r) => (result = r));

    flushOnce();

    expect(result).toBeDefined();
    expect(result?.name).toBe('Discontinued Line');
    expect(result?.isActive).toBeFalse();
  });

  it('returns undefined for an unknown id instead of throwing', () => {
    let result: unknown = 'not-yet-set';
    service.categoryById('does-not-exist').subscribe((r) => (result = r));

    flushOnce();

    expect(result).toBeUndefined();
  });

  it('resolves undefined synchronously for a null/undefined id without making a request', () => {
    let result: unknown = 'not-yet-set';
    service.categoryById(undefined).subscribe((r) => (result = r));
    expect(result).toBeUndefined();
    httpMock.expectNone((r) => r.url === '/api/v1/master-data');
  });

  it('excludes retired rows from dropdown options', () => {
    let result: Array<{ isActive: boolean }> = [];
    service.categoryOptions().subscribe((r) => (result = r));

    flushOnce();

    expect(result.length).toBe(1);
    expect(result.every((r) => r.isActive)).toBeTrue();
  });

  describe('documentTypeOptions (N-20(a)/(c))', () => {
    it('filters to the given scope, excluding rows for the other scope', () => {
      let vendorResult: Array<{ code: string }> = [];
      let shipmentResult: Array<{ code: string }> = [];
      service.documentTypeOptions('Vendor').subscribe((r) => (vendorResult = r));
      service.documentTypeOptions('Shipment').subscribe((r) => (shipmentResult = r));

      flushOnce();

      expect(vendorResult.map((r) => r.code)).toEqual(['BUSINESS_LICENCE']);
      expect(shipmentResult.map((r) => r.code)).toEqual(['PACKING_LIST']);
    });
  });

  it('orders dropdown options by sortOrder, retired rows excluded', () => {
    let result: Array<{ code: string }> = [];
    service.leadStatusOptions().subscribe((r) => (result = r));

    flushOnce();

    // Source order is [QUALIFIED(2), NEW(1), ARCHIVED(3, retired)] —
    // expect [NEW, QUALIFIED], ARCHIVED dropped entirely.
    expect(result.map((r) => r.code)).toEqual(['NEW', 'QUALIFIED']);
  });

  it('surfaces an API error via error$/loading$ instead of hanging', () => {
    const loadingStates: boolean[] = [];
    // Definite-assignment assertion (no initializer) rather than `= null` —
    // TS's control-flow narrowing otherwise freezes this at the literal type
    // of the initializer and never widens back through the subscribe closure.
    let errorMessage!: string | null;
    service.loading$.subscribe((l) => loadingStates.push(l));
    service.error$.subscribe((e) => (errorMessage = e));

    let errored = false;
    service.ensureLoaded().subscribe({
      next: () => fail('expected an error, not a value'),
      error: () => (errored = true)
    });

    const req = httpMock.expectOne((r) => r.url === '/api/v1/master-data');
    req.flush(
      { title: 'Internal Server Error', detail: 'Lookup service unavailable' },
      { status: 500, statusText: 'Server Error' }
    );

    expect(errored).toBeTrue();
    expect(errorMessage).toBe('Lookup service unavailable');
    // loading toggled true then false — never stuck in the loading state.
    expect(loadingStates).toEqual([false, true, false]);
  });

  it('does not leave dropdown/byId accessors hanging forever on error', () => {
    let emitted = false;
    service.categoryOptions().subscribe(() => (emitted = true));

    const req = httpMock.expectOne((r) => r.url === '/api/v1/master-data');
    req.flush({ title: 'Server error' }, { status: 500, statusText: 'Server Error' });

    // The accessor swallows the error (state$/error$ already carries it) and
    // simply never emits a value rather than throwing an unhandled error.
    expect(emitted).toBeFalse();
  });

  it('reload() issues a fresh HTTP request and pushes new data to existing subscribers', () => {
    let latest: Array<{ id: string }> = [];
    service.categoryOptions().subscribe((r) => (latest = r));
    flushOnce();
    expect(latest[0].id).toBe('cat-active');

    service.reload().subscribe();
    const reloadedResponse: MasterDataResponse = {
      ...mockResponse,
      categories: [{ id: 'cat-new', name: 'Newly Added', sortOrder: 1, isActive: true }]
    };
    const req2 = httpMock.expectOne((r) => r.url === '/api/v1/master-data');
    req2.flush(reloadedResponse);

    expect(latest[0].id).toBe('cat-new');
  });
});
