import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ShipmentsComponent } from './shipments.component';
import { AuthService } from '../core/services/auth.service';
import { ShipmentListItem, ShipmentStatusCount } from './models/shipment.models';

function shipment(overrides: Partial<ShipmentListItem> = {}): ShipmentListItem {
  return {
    id: 'shp-1',
    reference: 'SHP-2607-014',
    customer: { id: 'cust-1', name: 'Meena Traders' },
    destination: 'Surat',
    serviceType: { id: 'svc-1', code: 'CIF', label: 'CIF' },
    dispatchDate: '2026-07-22T00:00:00Z',
    status: { id: 'st-2', code: 'DISPATCHED', label: 'Dispatched' },
    freightCost: 5000,
    totalValue: 185000,
    mode: 'Sea',
    awbOrBl: null,
    eta: '2026-08-01T00:00:00Z',
    lineCount: 1,
    ...overrides
  };
}

const STATUS_COUNTS: ShipmentStatusCount[] = [
  { statusId: 'st-1', code: 'PACKED', label: 'PACKED', sortOrder: 1, count: 0 },
  { statusId: 'st-2', code: 'DISPATCHED', label: 'DISPATCHED', sortOrder: 2, count: 3 },
  { statusId: 'st-3', code: 'IN TRANSIT', label: 'IN TRANSIT', sortOrder: 3, count: 2 },
  { statusId: 'st-4', code: 'DELIVERED', label: 'DELIVERED', sortOrder: 4, count: 5 }
];

describe('ShipmentsComponent', () => {
  let fixture: ComponentFixture<ShipmentsComponent>;
  let httpMock: HttpTestingController;

  async function configure(permissions: string[] = []): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [ShipmentsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: { hasPermission: (p: string) => permissions.includes(p), currentUser$: of(null) } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ShipmentsComponent);
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => httpMock.verify());

  function flushList(items: ShipmentListItem[], statusCounts: ShipmentStatusCount[] = STATUS_COUNTS, totalCount = items.length): void {
    httpMock.expectOne((r) => r.url === '/api/v1/shipments').flush({ items, page: 1, pageSize: 25, totalCount, statusCounts });
  }

  it('renders one tab per statusCounts row IN SORT ORDER, including a zero-count status, plus "All shipments" summing every count', async () => {
    await configure();
    fixture.detectChanges();
    flushList([shipment()]);
    fixture.detectChanges();

    const tabEls: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.pill-tab'));
    const labels = tabEls.map((el) => el.textContent?.replace(/\s+/g, ' ').trim());

    expect(labels[0]).toContain('All shipments');
    expect(labels[0]).toContain('10'); // 0 + 3 + 2 + 5
    expect(labels[1]).toContain('PACKED');
    expect(labels[1]).toContain('0'); // zero-count status still renders as its own tab
    expect(labels[2]).toContain('DISPATCHED');
    expect(labels[3]).toContain('IN TRANSIT');
    expect(labels[4]).toContain('DELIVERED');
  });

  it('sends the selected tab\'s statusId as a query param and refetches', async () => {
    await configure();
    fixture.detectChanges();
    flushList([]);
    fixture.detectChanges();

    fixture.componentInstance.selectTab('st-3');

    const req = httpMock.expectOne((r) => r.url === '/api/v1/shipments');
    expect(req.request.params.get('statusId')).toBe('st-3');
    req.flush({ items: [], page: 1, pageSize: 25, totalCount: 0, statusCounts: STATUS_COUNTS });
  });

  it('derives Stock Impact from the service type CODE, not the label (D-36)', async () => {
    await configure();
    fixture.detectChanges();
    // A row whose code is FREIGHT_ONLY but whose label is something else entirely —
    // if the component branched on the label this would render the wrong impact text.
    flushList([shipment({ serviceType: { id: 'svc-2', code: 'FREIGHT_ONLY', label: 'Freight (Renamed)' } })]);
    fixture.detectChanges();

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('No stock held');
    expect(text).not.toContain('Decrements inventory');
  });

  it('shows "Decrements inventory" for a CIF row', async () => {
    await configure();
    fixture.detectChanges();
    flushList([shipment({ serviceType: { id: 'svc-1', code: 'CIF', label: 'CIF' } })]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Decrements inventory');
  });

  it('shows a visible error instead of hanging when the list request fails', async () => {
    await configure();
    fixture.detectChanges();
    httpMock
      .expectOne((r) => r.url === '/api/v1/shipments')
      .flush({ title: 'Server error', detail: 'Shipment lookup failed' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(fixture.componentInstance.error()).toBe('Shipment lookup failed');
    expect(fixture.nativeElement.textContent).toContain('Shipment lookup failed');
  });

  it('shows the empty-state message when no shipments match the filters', async () => {
    await configure();
    fixture.detectChanges();
    flushList([]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No shipments match these filters');
  });

  it('hides "+ Record Outbound Shipment" without Shipments.Edit permission', async () => {
    await configure([]);
    fixture.detectChanges();
    flushList([]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('+ Record Outbound Shipment');
  });
});
