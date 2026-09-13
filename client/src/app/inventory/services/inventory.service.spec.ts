import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { InventoryService } from './inventory.service';
import { InventoryItem } from '../models/inventory.models';

const ITEM: InventoryItem = {
  id: 'inv-1',
  name: 'Silver Chain',
  sku: 'SKU-001',
  description: null,
  category: { id: 'cat-1', name: 'Jewellery' },
  vendor: { id: 'ven-1', name: 'Yiwu Jewel Craft Co.' },
  unit: 'pcs',
  onHandQty: 1840,
  reorderThreshold: 600,
  unitCost: 120,
  sellingPrice: null,
  hsnCode: null,
  gstRate: null,
  stockValue: 220800,
  stockLevel: 'HEALTHY'
};

describe('InventoryService', () => {
  let service: InventoryService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(InventoryService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('GETs the list with all filter/paging params', () => {
    service.list({ search: 'chain', categoryId: 'cat-1', vendorId: 'ven-1', stockLevel: 'LOW', page: 2, pageSize: 25 }).subscribe();

    const req = httpMock.expectOne(
      (r) =>
        r.url === '/api/v1/inventory' &&
        r.params.get('search') === 'chain' &&
        r.params.get('categoryId') === 'cat-1' &&
        r.params.get('vendorId') === 'ven-1' &&
        r.params.get('stockLevel') === 'LOW' &&
        r.params.get('page') === '2' &&
        r.params.get('pageSize') === '25'
    );
    expect(req.request.method).toBe('GET');
    req.flush({ items: [ITEM], page: 2, pageSize: 25, totalCount: 1, summary: { onHandValue: 220800, itemCount: 1, lowStockCount: 0, negativeStockCount: 0 } });
  });

  it('GETs a single item by id', () => {
    service.getById('inv-1').subscribe();
    const req = httpMock.expectOne('/api/v1/inventory/inv-1');
    expect(req.request.method).toBe('GET');
    req.flush(ITEM);
  });

  it('POSTs a create request including the opening onHandQty', () => {
    service
      .create({ name: 'Silver Chain', categoryId: 'cat-1', unit: 'pcs', onHandQty: 500, reorderThreshold: 100 })
      .subscribe();

    const req = httpMock.expectOne('/api/v1/inventory');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.onHandQty).toBe(500);
    req.flush(ITEM, { status: 201, statusText: 'Created' });
  });

  it('PUTs an update request that never includes onHandQty (D-42)', () => {
    service
      .update('inv-1', { name: 'Silver Chain', categoryId: 'cat-1', unit: 'pcs', reorderThreshold: 700 })
      .subscribe();

    const req = httpMock.expectOne('/api/v1/inventory/inv-1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ name: 'Silver Chain', categoryId: 'cat-1', unit: 'pcs', reorderThreshold: 700 });
    expect(req.request.body.onHandQty).toBeUndefined();
    req.flush(ITEM);
  });

  it('DELETEs an item', () => {
    service.delete('inv-1').subscribe();
    const req = httpMock.expectOne('/api/v1/inventory/inv-1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('POSTs an inbound entry and receives both the entry and the recomputed item', () => {
    service.recordInbound('inv-1', { quantity: 200, entryDate: '2026-07-29', reference: 'PO-100' }).subscribe();

    const req = httpMock.expectOne('/api/v1/inventory/inv-1/inbound');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ quantity: 200, entryDate: '2026-07-29', reference: 'PO-100' });
    req.flush({
      entry: {
        id: 'entry-1',
        inventoryItemId: 'inv-1',
        quantity: 200,
        entryDate: '2026-07-29',
        reference: 'PO-100',
        recordedByUserId: 'user-1',
        recordedByName: 'Priya Sharma',
        createdAt: '2026-07-29T10:00:00Z'
      },
      item: { ...ITEM, onHandQty: 2040 }
    });
  });

  it('GETs the inbound entry list for an item', () => {
    service.listInbound('inv-1').subscribe();
    const req = httpMock.expectOne('/api/v1/inventory/inv-1/inbound');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('POSTs an adjustment and receives both the adjustment and the recomputed item (N-38)', () => {
    service.recordAdjustment('inv-1', { countedQty: 1837, reason: 'Physical count', adjustedOn: '2026-07-29' }).subscribe();

    const req = httpMock.expectOne('/api/v1/inventory/inv-1/adjustments');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ countedQty: 1837, reason: 'Physical count', adjustedOn: '2026-07-29' });
    req.flush({
      adjustment: {
        id: 'adj-1',
        countedQty: 1837,
        previousQty: 1840,
        delta: -3,
        reason: 'Physical count',
        adjustedOn: '2026-07-29',
        adjustedAt: '2026-07-29T10:00:00Z',
        adjustedByUserId: 'user-1',
        adjustedByName: 'Priya Sharma'
      },
      item: { ...ITEM, onHandQty: 1837 }
    });
  });

  it('GETs the adjustment history for an item and unwraps the `items` envelope', () => {
    let received: unknown = null;
    service.listAdjustments('inv-1').subscribe((r) => (received = r));
    const req = httpMock.expectOne('/api/v1/inventory/inv-1/adjustments');
    expect(req.request.method).toBe('GET');
    const row = {
      id: 'adj-1',
      countedQty: 1837,
      previousQty: 1840,
      delta: -3,
      reason: 'Physical count',
      adjustedOn: '2026-07-29',
      adjustedAt: '2026-07-29T10:00:00Z',
      adjustedByUserId: 'user-1',
      adjustedByName: 'Priya Sharma'
    };
    req.flush({ items: [row] });
    expect(received).toEqual([row]);
  });
});
