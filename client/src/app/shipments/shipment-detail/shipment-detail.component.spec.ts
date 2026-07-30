import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ShipmentDetailComponent } from './shipment-detail.component';
import { AuthService } from '../../core/services/auth.service';
import { MasterDataResponse } from '../../core/models/master-data.models';
import { ShipmentDetail } from '../models/shipment.models';

const SHIPMENT_STATUSES = [
  { id: 'st-1', code: 'PACKED', label: 'Packed', sortOrder: 1, isActive: true },
  { id: 'st-2', code: 'DISPATCHED', label: 'Dispatched', sortOrder: 2, isActive: true },
  { id: 'st-3', code: 'IN TRANSIT', label: 'In Transit', sortOrder: 3, isActive: true },
  { id: 'st-4', code: 'DELIVERED', label: 'Delivered', sortOrder: 4, isActive: true }
];

const MASTER_DATA: MasterDataResponse = {
  categories: [],
  serviceTypes: [{ id: 'svc-1', code: 'CIF', label: 'CIF', sortOrder: 1, isActive: true }],
  leadStatuses: [],
  shipmentStatuses: SHIPMENT_STATUSES,
  invoiceStatuses: [],
  vendorStatuses: [],
  documentTypes: [
    { id: 'dt-1', code: 'PACKING_LIST', label: 'Packing List', sortOrder: 1, isActive: true, scope: 'Shipment' },
    { id: 'dt-2', code: 'AWB', label: 'Airway Bill', sortOrder: 2, isActive: true, scope: 'Shipment' },
    { id: 'dt-3', code: 'CATALOG_SHEET', label: 'Catalog Sheet', sortOrder: 1, isActive: true, scope: 'Vendor' }
  ]
};

function detail(overrides: Partial<ShipmentDetail> = {}): ShipmentDetail {
  return {
    id: 'shp-1',
    reference: 'SHP-2607-014',
    customer: { id: 'cust-1', name: 'Meena Traders' },
    destination: 'Surat',
    serviceType: { id: 'svc-1', code: 'CIF', label: 'CIF' },
    dispatchDate: '2026-07-22T00:00:00Z',
    status: { id: 'st-2', code: 'DISPATCHED', label: 'Dispatched' },
    freightCost: 5000,
    totalValue: 60000,
    mode: 'Sea',
    awbOrBl: null,
    eta: '2026-08-01T00:00:00Z',
    lineCount: 1,
    createdAt: '2026-07-20T09:00:00Z',
    recordedByName: 'Vikram Nair',
    lines: [
      {
        id: 'line-1',
        inventoryItemId: 'inv-1',
        inventoryItemName: 'Silver Chain',
        inventoryItemSku: 'SKU-001',
        unit: 'pcs',
        quantity: 500,
        unitCost: 120,
        lineTotal: 60000
      }
    ],
    statusHistory: [
      { id: 'hist-1', status: { id: 'st-1', code: 'PACKED', label: 'Packed' }, changedByUserId: 'u1', changedByName: 'Priya Sharma', changedAt: '2026-07-20T09:00:00Z', note: 'Shipment created.' },
      { id: 'hist-2', status: { id: 'st-2', code: 'DISPATCHED', label: 'Dispatched' }, changedByUserId: 'u1', changedByName: 'Priya Sharma', changedAt: '2026-07-22T10:00:00Z', note: null }
    ],
    documents: [],
    ...overrides
  };
}

describe('ShipmentDetailComponent', () => {
  let fixture: ComponentFixture<ShipmentDetailComponent>;
  let httpMock: HttpTestingController;

  async function configure(id = 'shp-1', permissions: string[] = ['Shipments.Edit']): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [ShipmentDetailComponent],
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
    }).compileComponents();

    fixture = TestBed.createComponent(ShipmentDetailComponent);
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => httpMock.verify());

  function flushMasterData(data: MasterDataResponse = MASTER_DATA): void {
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush(data);
  }

  function flushShipment(payload: ShipmentDetail = detail()): void {
    httpMock.expectOne((r) => r.url === '/api/v1/shipments/shp-1').flush(payload);
  }

  it('builds the stepper from the live shipmentStatuses lookup, marking "when" from statusHistory and an em-dash for unreached steps', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushShipment();
    fixture.detectChanges();

    const steps: StepView[] = fixture.componentInstance.steps();
    expect(steps.map((s) => s.label)).toEqual(['Packed', 'Dispatched', 'In Transit', 'Delivered']);
    expect(steps[0].when).toContain('2026'); // reached, has a real history row
    expect(steps[1].when).toContain('2026'); // current step, has a real history row
    expect(steps[2].when).toBe('—'); // not yet reached — no history row, no invented timestamp
    expect(steps[3].when).toBe('—');
    expect(steps[0].mark).toBe('✓');
    expect(steps[1].mark).toBe('●');
    expect(steps[2].mark).toBe('3');
  });

  it('renders a clear empty state for a freight-only shipment with no lines, instead of a broken table', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushShipment(detail({ serviceType: { id: 'svc-2', code: 'FREIGHT_ONLY', label: 'Freight-only' }, lines: [] }));
    fixture.detectChanges();

    expect(fixture.componentInstance.noLines()).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('Freight-only shipment — no inventory lines, no stock movement.');
  });

  it('renders an em-dash instead of ₹0 for null unitCost/lineTotal on a line', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushShipment(
      detail({
        lines: [
          { id: 'line-1', inventoryItemId: 'inv-1', inventoryItemName: 'Customer-owned goods', inventoryItemSku: null, unit: 'cartons', quantity: 3, unitCost: null, lineTotal: null }
        ]
      })
    );
    fixture.detectChanges();

    const row = fixture.componentInstance.lines()[0];
    expect(row.unitCost).toBe('—');
    expect(row.lineTotal).toBe('—');
  });

  it('derives the advance-status button\'s next status from the lookup\'s sortOrder, not a hard-coded array', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushShipment(); // currently DISPATCHED (st-2)
    fixture.detectChanges();

    expect(fixture.componentInstance.advanceLabel()).toBe('Mark In Transit');
    expect(fixture.componentInstance.canAdvance()).toBeTrue();

    fixture.componentInstance.advanceStatus();
    const req = httpMock.expectOne((r) => r.url === '/api/v1/shipments/shp-1/status');
    expect(req.request.body).toEqual({ statusId: 'st-3' });
    req.flush(detail({ status: { id: 'st-3', code: 'IN TRANSIT', label: 'In Transit' } }));
  });

  it('disables the advance-status button once at the last status', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushShipment(detail({ status: { id: 'st-4', code: 'DELIVERED', label: 'Delivered' } }));
    fixture.detectChanges();

    expect(fixture.componentInstance.canAdvance()).toBeFalse();
  });

  it('the edit dialog exposes NO status dropdown (D-43) — status only ever changes via changeStatus()', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushShipment();
    fixture.detectChanges();

    fixture.componentInstance.openEdit();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#sf-status')).toBeNull();
    // The dialog still shows the shipment's status, just as read-only text, not a bound <select>.
    expect(fixture.nativeElement.textContent).toContain('cannot be changed here');

    // The edit dialog's own child-load requests (customer/inventory pickers
    // for its line-item editor) — drained so httpMock.verify() doesn't fail
    // the outer spec.
    httpMock.expectOne((r) => r.url === '/api/v1/customers').flush({ items: [], page: 1, pageSize: 200, totalCount: 0 });
    httpMock.expectOne((r) => r.url === '/api/v1/inventory').flush({
      items: [],
      page: 1,
      pageSize: 200,
      totalCount: 0,
      summary: { onHandValue: 0, itemCount: 0, lowStockCount: 0, negativeStockCount: 0 }
    });
  });

  it('the upload dialog\'s document-type dropdown only offers Shipment-scoped types', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushShipment();
    fixture.detectChanges();

    fixture.componentInstance.openUpload();
    fixture.detectChanges();

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('Packing List');
    expect(text).toContain('Airway Bill');
    expect(text).not.toContain('Catalog Sheet');
  });

  it('shows a visible error instead of hanging when the shipment fails to load', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    httpMock
      .expectOne((r) => r.url === '/api/v1/shipments/shp-1')
      .flush({ title: 'Not Found', detail: 'Shipment not found' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(fixture.componentInstance.error()).toBe('Shipment not found');
    expect(fixture.nativeElement.textContent).toContain('Shipment not found');
  });

  it('hides the mutating action buttons without Shipments.Edit', async () => {
    await configure('shp-1', []);
    fixture.detectChanges();
    flushMasterData();
    flushShipment();
    fixture.detectChanges();

    const text: string = fixture.nativeElement.textContent;
    expect(text).not.toContain('Attach Document');
    expect(fixture.nativeElement.querySelector('.sd-actions .btn-primary')).toBeNull();
  });
});

interface StepView {
  label: string;
  when: string;
  mark: string;
}
