import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ShipmentsComponent } from './shipments.component';
import { AuthService } from '../core/services/auth.service';
import { ShipmentListItem, ShipmentStatusCount } from './models/shipment.models';

/** The four seeded shipment statuses, with the codes the live API actually returns. */
const STATUS_COUNTS: ShipmentStatusCount[] = [
  { statusId: 'st-packed', code: 'PACKED', label: 'Packed', sortOrder: 1, count: 2 },
  { statusId: 'st-dispatched', code: 'DISPATCHED', label: 'Dispatched', sortOrder: 2, count: 0 },
  { statusId: 'st-transit', code: 'IN TRANSIT', label: 'In Transit', sortOrder: 3, count: 2 },
  { statusId: 'st-delivered', code: 'DELIVERED', label: 'Delivered', sortOrder: 4, count: 1 }
];

function shipment(overrides: Partial<ShipmentListItem> = {}): ShipmentListItem {
  return {
    id: 'shp-1',
    reference: 'SHP-2607-001',
    customer: { id: 'cus-1', name: 'Meena Traders' },
    destination: 'Surat, Gujarat',
    serviceType: { id: 'svc-cif', code: 'CIF', label: 'CIF' },
    dispatchDate: '2026-07-22T00:00:00Z',
    status: { id: 'st-transit', code: 'IN TRANSIT', label: 'In Transit' },
    freightCost: 48000,
    totalValue: 250000,
    mode: 'Sea LCL - Nhava Sheva',
    awbOrBl: 'BL SNKO4471192',
    eta: '2026-08-04T00:00:00Z',
    lineCount: 1,
    ...overrides
  };
}

describe('ShipmentsComponent', () => {
  let fixture: ComponentFixture<ShipmentsComponent>;
  let httpMock: HttpTestingController;

  async function configure(permissions: string[] = ['Shipments.View']): Promise<void> {
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

  function flushList(items: ShipmentListItem[], statusCounts = STATUS_COUNTS, totalCount = items.length): void {
    httpMock
      .expectOne((r) => r.url === '/api/v1/shipments')
      .flush({ items, page: 1, pageSize: 25, totalCount, statusCounts });
  }

  describe('status tabs', () => {
    it('builds tabs from statusCounts in sortOrder, never a hard-coded status list (DR-6)', async () => {
      await configure();
      fixture.detectChanges();
      flushList([shipment()]);
      fixture.detectChanges();

      const labels = Array.from(fixture.nativeElement.querySelectorAll('.shp-tab')).map((t) =>
        (t as HTMLElement).textContent!.replace(/\s+/g, ' ').trim()
      );
      expect(labels).toEqual(['All shipments 5', 'Packed 2', 'Dispatched 0', 'In Transit 2', 'Delivered 1']);
    });

    it('keeps every tab count stable after selecting one, because the server excludes the status filter (E7-08)', async () => {
      await configure();
      fixture.detectChanges();
      flushList([shipment()], STATUS_COUNTS, 5);
      fixture.detectChanges();

      fixture.componentInstance.selectTab('st-delivered');
      const req = httpMock.expectOne((r) => r.url === '/api/v1/shipments');
      expect(req.request.params.get('statusId')).toBe('st-delivered');
      // totalCount is now the FILTERED count, but statusCounts still spans all statuses.
      req.flush({ items: [shipment()], page: 1, pageSize: 25, totalCount: 1, statusCounts: STATUS_COUNTS });
      fixture.detectChanges();

      const labels = Array.from(fixture.nativeElement.querySelectorAll('.shp-tab')).map((t) =>
        (t as HTMLElement).textContent!.replace(/\s+/g, ' ').trim()
      );
      // "All shipments" must still read 5, not collapse to the filtered 1.
      expect(labels[0]).toBe('All shipments 5');
      expect(labels[4]).toBe('Delivered 1');
    });

    it('marks the selected tab active and the All tab active by default', async () => {
      await configure();
      fixture.detectChanges();
      flushList([shipment()]);
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.shp-tab--active').textContent).toContain('All shipments');
    });
  });

  describe('rows', () => {
    it('renders a CIF shipment as decrementing inventory', async () => {
      await configure();
      fixture.detectChanges();
      flushList([shipment()]);
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('tbody tr').textContent).toContain('Decrements inventory');
    });

    it('renders a freight-only shipment as holding no stock, keyed off the code (FSD A8)', async () => {
      await configure();
      fixture.detectChanges();
      flushList([shipment({ serviceType: { id: 'svc-fo', code: 'FREIGHT_ONLY', label: 'Freight-only' } })]);
      fixture.detectChanges();

      const row: string = fixture.nativeElement.querySelector('tbody tr').textContent;
      expect(row).toContain('No stock held');
      // And the chip resolves through StatusStyleService rather than falling through.
      expect(row).toContain('FREIGHT-ONLY');
    });

    it('renders a shipment with no total value as "—", not ₹0', async () => {
      await configure();
      fixture.detectChanges();
      flushList([shipment({ totalValue: null })]);
      fixture.detectChanges();

      const row: string = fixture.nativeElement.querySelector('tbody tr').textContent;
      expect(row).toContain('—');
      expect(row).not.toContain('₹0');
    });

    it('still renders a clickable row when the reference is null', async () => {
      await configure();
      fixture.detectChanges();
      flushList([shipment({ reference: null })]);
      fixture.detectChanges();

      const link = fixture.nativeElement.querySelector('tbody tr a') as HTMLAnchorElement;
      expect(link.textContent!.trim()).toBe('(no reference)');
      expect(link.getAttribute('href')).toContain('/shipments/shp-1');
    });
  });

  it('shows a retryable error instead of hanging when the list request fails', async () => {
    await configure();
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url === '/api/v1/shipments').flush('boom', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.state-panel').textContent).toContain('Could not load shipments');
  });

  it('hides the record button without Shipments.Edit', async () => {
    await configure(['Shipments.View']);
    fixture.detectChanges();
    flushList([shipment()]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.page-header button')).toBeFalsy();
  });
});
