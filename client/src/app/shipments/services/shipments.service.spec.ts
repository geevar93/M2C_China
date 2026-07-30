import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ShipmentsService } from './shipments.service';
import { ShipmentDetail } from '../models/shipment.models';

const DETAIL: ShipmentDetail = {
  id: 'shp-1',
  reference: 'SHP-2607-014',
  customer: { id: 'cust-1', name: 'Meena Traders' },
  destination: 'Surat',
  serviceType: { id: 'svc-1', code: 'CIF', label: 'CIF' },
  dispatchDate: '2026-07-22T00:00:00Z',
  status: { id: 'st-1', code: 'PACKED', label: 'Packed' },
  freightCost: 5000,
  totalValue: 185000,
  mode: 'Sea',
  awbOrBl: null,
  eta: '2026-08-01T00:00:00Z',
  lineCount: 1,
  createdAt: '2026-07-20T09:00:00Z',
  recordedByName: 'Priya Sharma',
  lines: [
    { id: 'line-1', inventoryItemId: 'inv-1', inventoryItemName: 'Silver Chain', inventoryItemSku: 'SKU-001', unit: 'pcs', quantity: 500, unitCost: 120, lineTotal: 60000 }
  ],
  statusHistory: [
    { id: 'hist-1', status: { id: 'st-1', code: 'PACKED', label: 'Packed' }, changedByUserId: 'user-1', changedByName: 'Priya Sharma', changedAt: '2026-07-20T09:00:00Z', note: 'Shipment created.' }
  ],
  documents: []
};

describe('ShipmentsService', () => {
  let service: ShipmentsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(ShipmentsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('GETs the list with all filter/paging params', () => {
    service.list({ statusId: 'st-1', customerId: 'cust-1', from: '2026-07-01', to: '2026-07-31', search: 'SHP-014', page: 1, pageSize: 25 }).subscribe();

    const req = httpMock.expectOne(
      (r) =>
        r.url === '/api/v1/shipments' &&
        r.params.get('statusId') === 'st-1' &&
        r.params.get('customerId') === 'cust-1' &&
        r.params.get('from') === '2026-07-01' &&
        r.params.get('to') === '2026-07-31' &&
        r.params.get('search') === 'SHP-014' &&
        r.params.get('page') === '1' &&
        r.params.get('pageSize') === '25'
    );
    expect(req.request.method).toBe('GET');
    req.flush({ items: [DETAIL], page: 1, pageSize: 25, totalCount: 1, statusCounts: [] });
  });

  it('GETs the full detail shape by id', () => {
    service.getById('shp-1').subscribe();
    const req = httpMock.expectOne('/api/v1/shipments/shp-1');
    expect(req.request.method).toBe('GET');
    req.flush(DETAIL);
  });

  it('POSTs a create request and returns the full detail shape', () => {
    service
      .create({
        customerId: 'cust-1',
        serviceTypeId: 'svc-1',
        statusId: 'st-1',
        lines: [{ inventoryItemId: 'inv-1', quantity: 500 }]
      })
      .subscribe();

    const req = httpMock.expectOne('/api/v1/shipments');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.statusId).toBe('st-1');
    req.flush(DETAIL, { status: 201, statusText: 'Created' });
  });

  it('surfaces the 409 insufficientStock ProblemDetails extension verbatim on create', () => {
    let caught: unknown;
    service
      .create({ customerId: 'cust-1', serviceTypeId: 'svc-1', statusId: 'st-1', lines: [{ inventoryItemId: 'inv-1', quantity: 99999 }] })
      .subscribe({ error: (err) => (caught = err) });

    const req = httpMock.expectOne('/api/v1/shipments');
    req.flush(
      {
        title: 'Insufficient stock.',
        status: 409,
        detail: 'Recording this shipment would drive on-hand quantity negative for one or more items. Retry with allowNegativeStock: true to override deliberately.',
        insufficientStock: [{ inventoryItemId: 'inv-1', itemName: 'Silver Chain', sku: 'SKU-001', requestedQty: 99999, availableQty: 1840 }]
      },
      { status: 409, statusText: 'Conflict' }
    );

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    expect((caught as any).error.insufficientStock[0].itemName).toBe('Silver Chain');
  });

  it('PUTs an update request that never includes statusId (D-43)', () => {
    service
      .update('shp-1', {
        customerId: 'cust-1',
        serviceTypeId: 'svc-1',
        lines: [{ inventoryItemId: 'inv-1', quantity: 500 }]
      })
      .subscribe();

    const req = httpMock.expectOne('/api/v1/shipments/shp-1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.statusId).toBeUndefined();
    req.flush(DETAIL);
  });

  it('DELETEs a shipment', () => {
    service.delete('shp-1').subscribe();
    const req = httpMock.expectOne('/api/v1/shipments/shp-1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('PUTs the status change to the dedicated /status path — the only path that writes history', () => {
    service.changeStatus('shp-1', { statusId: 'st-2', note: 'Dispatched today' }).subscribe();

    const req = httpMock.expectOne('/api/v1/shipments/shp-1/status');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ statusId: 'st-2', note: 'Dispatched today' });
    req.flush(DETAIL);
  });

  it('GETs the document list for a shipment', () => {
    service.listDocuments('shp-1').subscribe();
    const req = httpMock.expectOne('/api/v1/shipments/shp-1/documents');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('uploads a document as multipart form data', () => {
    const file = new File(['content'], 'awb.pdf', { type: 'application/pdf' });
    service.uploadDocument('shp-1', file, 'doc-type-1').subscribe();

    const req = httpMock.expectOne('/api/v1/shipments/shp-1/documents');
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBeTrue();
    req.flush({
      id: 'doc-1',
      shipmentId: 'shp-1',
      originalFilename: 'awb.pdf',
      sizeBytes: 1024,
      documentType: { id: 'doc-type-1', code: 'AWB', label: 'Airway Bill' },
      uploadedByUserId: 'user-1',
      uploadedByName: 'Priya Sharma',
      uploadedAt: '2026-07-29T10:00:00Z'
    });
  });

  it('downloads a document from the NOT-nested /shipment-documents path', () => {
    service.downloadDocument('doc-1').subscribe();
    const req = httpMock.expectOne('/api/v1/shipment-documents/doc-1/download');
    expect(req.request.method).toBe('GET');
    req.flush(new Blob());
  });

  it('deletes a document from the NOT-nested /shipment-documents path', () => {
    service.deleteDocument('doc-1').subscribe();
    const req = httpMock.expectOne('/api/v1/shipment-documents/doc-1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
