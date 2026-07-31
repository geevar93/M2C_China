import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ShipmentFormDialogComponent } from './shipment-form-dialog.component';

describe('ShipmentFormDialogComponent', () => {
  let fixture: ComponentFixture<ShipmentFormDialogComponent>;
  let httpMock: HttpTestingController;

  async function configure(): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [ShipmentFormDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    fixture = TestBed.createComponent(ShipmentFormDialogComponent);
    httpMock = TestBed.inject(HttpTestingController);
    fixture.detectChanges();

    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush({
      categories: [],
      serviceTypes: [
        { id: 'svc-1', code: 'CIF', label: 'CIF', sortOrder: 1, isActive: true },
        { id: 'svc-2', code: 'FREIGHT_ONLY', label: 'Freight-only', sortOrder: 2, isActive: true }
      ],
      leadStatuses: [],
      shipmentStatuses: [{ id: 'st-1', code: 'PACKED', label: 'Packed', sortOrder: 1, isActive: true }],
      invoiceStatuses: [],
      vendorStatuses: [],
      documentTypes: []
    });
    httpMock.expectOne((r) => r.url === '/api/v1/customers').flush({
      items: [{ id: 'cust-1', name: 'Meena', businessName: 'Meena Traders', phone: '9', city: null, region: null, sourceChannel: 'WhatsApp', serviceTypeId: 'svc-1', statusId: 'st-1', categoryIds: [], ownerUserId: null, ownerName: null, tags: [], createdAt: '2026-01-01T00:00:00Z' }],
      page: 1,
      pageSize: 200,
      totalCount: 1
    });
    httpMock.expectOne((r) => r.url === '/api/v1/inventory').flush({
      items: [{ id: 'inv-1', name: 'Silver Chain', sku: 'SKU-001', description: null, category: { id: 'cat-1', name: 'Jewellery' }, vendor: null, unit: 'pcs', onHandQty: 1000, reorderThreshold: 100, unitCost: 120, stockValue: 120000, stockLevel: 'HEALTHY' }],
      page: 1,
      pageSize: 200,
      totalCount: 1,
      summary: { onHandValue: 120000, itemCount: 1, lowStockCount: 0, negativeStockCount: 0 }
    });
    fixture.detectChanges();
  }

  afterEach(() => httpMock.verify());

  it('renders no status dropdown when editing (D-43) but shows read-only status text', async () => {
    // Set via componentRef.setInput BEFORE the first detectChanges(), mirroring
    // how a real template binding ([shipment]="s") assigns the @Input before
    // ngOnInit/the first render — isEdit() is a computed() over a plain @Input
    // field, so it only reflects reality if read after the input lands.
    await TestBed.configureTestingModule({
      imports: [ShipmentFormDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
    fixture = TestBed.createComponent(ShipmentFormDialogComponent);
    httpMock = TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('shipment', {
      id: 'shp-1',
      reference: 'SHP-2607-014',
      customer: { id: 'cust-1', name: 'Meena Traders' },
      destination: 'Surat',
      serviceType: { id: 'svc-1', code: 'CIF', label: 'CIF' },
      dispatchDate: '2026-07-22T00:00:00Z',
      status: { id: 'st-1', code: 'PACKED', label: 'Packed' },
      freightCost: 5000,
      totalValue: 60000,
      mode: 'Sea',
      awbOrBl: null,
      eta: '2026-08-01T00:00:00Z',
      lineCount: 1,
      createdAt: '2026-07-20T09:00:00Z',
      recordedByName: 'Vikram Nair',
      lines: [{ id: 'l1', inventoryItemId: 'inv-1', inventoryItemName: 'Silver Chain', inventoryItemSku: 'SKU-001', unit: 'pcs', quantity: 500, unitCost: 120, lineTotal: 60000 }],
      statusHistory: [],
      documents: []
    });
    fixture.detectChanges();

    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush({
      categories: [],
      serviceTypes: [],
      leadStatuses: [],
      shipmentStatuses: [],
      invoiceStatuses: [],
      vendorStatuses: [],
      documentTypes: []
    });
    httpMock.expectOne((r) => r.url === '/api/v1/customers').flush({ items: [], page: 1, pageSize: 200, totalCount: 0 });
    httpMock.expectOne((r) => r.url === '/api/v1/inventory').flush({
      items: [],
      page: 1,
      pageSize: 200,
      totalCount: 0,
      summary: { onHandValue: 0, itemCount: 0, lowStockCount: 0, negativeStockCount: 0 }
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#sf-status')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Packed');
  });

  it('on a 409 insufficient-stock response, surfaces the named items and retries with allowNegativeStock: true when "Record Anyway" is clicked', async () => {
    await configure();
    fixture.componentInstance.customerId.set('cust-1');
    fixture.componentInstance.serviceTypeId.set('svc-1');
    fixture.componentInstance.statusId.set('st-1');
    fixture.componentInstance.lines.set([{ inventoryItemId: 'inv-1', quantity: '99999' }]);

    fixture.componentInstance.save();

    const createReq = httpMock.expectOne((r) => r.url === '/api/v1/shipments');
    createReq.flush(
      {
        title: 'Insufficient stock.',
        status: 409,
        detail: 'Recording this shipment would drive on-hand quantity negative for one or more items.',
        insufficientStock: [{ inventoryItemId: 'inv-1', itemName: 'Silver Chain', sku: 'SKU-001', requestedQty: 99999, availableQty: 1000 }]
      },
      { status: 409, statusText: 'Conflict' }
    );
    fixture.detectChanges();

    expect(fixture.componentInstance.insufficientStock()).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Silver Chain');
    expect(fixture.nativeElement.textContent).toContain('requested 99999');

    fixture.componentInstance.confirmOverride();
    const retryReq = httpMock.expectOne((r) => r.url === '/api/v1/shipments');
    expect(retryReq.request.body.allowNegativeStock).toBeTrue();
    retryReq.flush({
      id: 'shp-1',
      reference: 'SHP-2607-014',
      customer: { id: 'cust-1', name: 'Meena Traders' },
      destination: null,
      serviceType: { id: 'svc-1', code: 'CIF', label: 'CIF' },
      dispatchDate: null,
      status: { id: 'st-1', code: 'PACKED', label: 'Packed' },
      freightCost: null,
      totalValue: null,
      mode: null,
      awbOrBl: null,
      eta: null,
      lineCount: 1,
      createdAt: '2026-07-29T00:00:00Z',
      recordedByName: null,
      lines: [],
      statusHistory: [],
      documents: []
    });
  });

  it('hides the lines section entirely once FREIGHT_ONLY is selected (D-36) and sends an empty lines array', async () => {
    await configure();
    fixture.componentInstance.customerId.set('cust-1');
    fixture.componentInstance.serviceTypeId.set('svc-2'); // FREIGHT_ONLY
    fixture.componentInstance.statusId.set('st-1');
    fixture.detectChanges();

    expect(fixture.componentInstance.showsLines()).toBeFalse();

    fixture.componentInstance.save();
    const req = httpMock.expectOne((r) => r.url === '/api/v1/shipments');
    expect(req.request.body.lines).toEqual([]);
    req.flush({
      id: 'shp-1',
      reference: 'SHP-2607-015',
      customer: { id: 'cust-1', name: 'Meena Traders' },
      destination: null,
      serviceType: { id: 'svc-2', code: 'FREIGHT_ONLY', label: 'Freight-only' },
      dispatchDate: null,
      status: { id: 'st-1', code: 'PACKED', label: 'Packed' },
      freightCost: null,
      totalValue: null,
      mode: null,
      awbOrBl: null,
      eta: null,
      lineCount: 0,
      createdAt: '2026-07-29T00:00:00Z',
      recordedByName: null,
      lines: [],
      statusHistory: [],
      documents: []
    });
  });
});
