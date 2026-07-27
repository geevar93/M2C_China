import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminMasterDataService } from './admin-master-data.service';

describe('AdminMasterDataService', () => {
  let service: AdminMasterDataService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(AdminMasterDataService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('requests the aggregate with includeRetired=true by default', () => {
    service.getAggregate().subscribe();
    const req = httpMock.expectOne((r) => r.url === '/api/v1/master-data' && r.params.get('includeRetired') === 'true');
    expect(req.request.method).toBe('GET');
    req.flush({ categories: [], serviceTypes: [], leadStatuses: [], shipmentStatuses: [], invoiceStatuses: [], vendorStatuses: [] });
  });

  it('POSTs a category create to the categories collection with only a name', () => {
    service.create('categories', { name: 'Handbags' }).subscribe();
    const req = httpMock.expectOne('/api/v1/master-data/categories');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Handbags' });
    req.flush({ id: 'cat-1', name: 'Handbags', sortOrder: 3, isActive: true, isSystemDefault: false }, { status: 201, statusText: 'Created' });
  });

  it('POSTs a lookup create to the kebab-case collection segment with code and label', () => {
    service.create('leadStatuses', { code: 'NURTURING', label: 'Nurturing' }).subscribe();
    const req = httpMock.expectOne('/api/v1/master-data/lead-statuses');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ code: 'NURTURING', label: 'Nurturing' });
    req.flush(
      { id: 'ls-1', code: 'NURTURING', label: 'Nurturing', sortOrder: 5, isActive: true, isSystemDefault: false },
      { status: 201, statusText: 'Created' }
    );
  });

  it('PUTs an update to the right collection/id', () => {
    service.update('serviceTypes', 'svc-1', { code: 'CIF', label: 'CIF renamed' }).subscribe();
    const req = httpMock.expectOne('/api/v1/master-data/service-types/svc-1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ code: 'CIF', label: 'CIF renamed' });
    req.flush({ id: 'svc-1', code: 'CIF', label: 'CIF renamed', sortOrder: 1, isActive: true, isSystemDefault: true });
  });

  it('POSTs a retire and a restore to the expected paths', () => {
    service.retire('vendorStatuses', 'vs-1').subscribe();
    httpMock.expectOne((r) => r.url === '/api/v1/master-data/vendor-statuses/vs-1/retire' && r.method === 'POST').flush({});

    service.restore('vendorStatuses', 'vs-1').subscribe();
    httpMock.expectOne((r) => r.url === '/api/v1/master-data/vendor-statuses/vs-1/restore' && r.method === 'POST').flush({});
  });

  it('PUTs the exact [{ id, sortOrder }] payload on reorder', () => {
    service
      .reorder('shipmentStatuses', [
        { id: 'ss-1', sortOrder: 2 },
        { id: 'ss-2', sortOrder: 1 }
      ])
      .subscribe();

    const req = httpMock.expectOne('/api/v1/master-data/shipment-statuses/reorder');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual([
      { id: 'ss-1', sortOrder: 2 },
      { id: 'ss-2', sortOrder: 1 }
    ]);
    req.flush([]);
  });

  it('DELETEs a row and surfaces a 409 ProblemDetails detail verbatim on failure', () => {
    let caught: unknown;
    service.delete('categories', 'cat-1').subscribe({ error: (err) => (caught = err) });

    const req = httpMock.expectOne('/api/v1/master-data/categories/cat-1');
    expect(req.request.method).toBe('DELETE');
    req.flush(
      { title: 'Cannot delete a referenced master-data row.', detail: 'This category is referenced by customers. Retire it instead of deleting.' },
      { status: 409, statusText: 'Conflict' }
    );

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    expect((caught as any).error.detail).toBe('This category is referenced by customers. Retire it instead of deleting.');
  });
});
