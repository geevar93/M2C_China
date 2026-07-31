import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ShipmentDetailComponent } from './shipment-detail.component';
import { AuthService } from '../../core/services/auth.service';
import { MasterDataResponse } from '../../core/models/master-data.models';
import { ShipmentDetail } from '../models/shipment.models';

const MASTER_DATA: MasterDataResponse = {
  categories: [],
  serviceTypes: [],
  leadStatuses: [],
  shipmentStatuses: [
    { id: 'st-packed', code: 'PACKED', label: 'Packed', sortOrder: 1, isActive: true },
    { id: 'st-dispatched', code: 'DISPATCHED', label: 'Dispatched', sortOrder: 2, isActive: true },
    { id: 'st-transit', code: 'IN TRANSIT', label: 'In Transit', sortOrder: 3, isActive: true },
    { id: 'st-delivered', code: 'DELIVERED', label: 'Delivered', sortOrder: 4, isActive: true }
  ],
  invoiceStatuses: [],
  vendorStatuses: [],
  documentTypes: [
    { id: 'dt-licence', code: 'BUSINESS_LICENCE', label: 'Business Licence', sortOrder: 1, isActive: true, scope: 'Vendor' },
    { id: 'dt-packing', code: 'PACKING_LIST', label: 'Packing List', sortOrder: 5, isActive: true, scope: 'Shipment' },
    { id: 'dt-bl', code: 'BILL_OF_LADING', label: 'Bill of Lading', sortOrder: 6, isActive: true, scope: 'Shipment' }
  ]
};

function detail(overrides: Partial<ShipmentDetail> = {}): ShipmentDetail {
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
    createdAt: '2026-07-20T09:00:00Z',
    recordedByName: 'Super Admin',
    lines: [
      {
        id: 'ln-1',
        inventoryItemId: 'inv-1',
        inventoryItemName: 'Imitation Kundan Set (Gold Tone)',
        inventoryItemSku: 'JWL-KUN-118',
        unit: 'set',
        quantity: 500,
        unitCost: 500,
        lineTotal: 250000
      }
    ],
    statusHistory: [
      {
        id: 'h-1',
        status: { id: 'st-packed', code: 'PACKED', label: 'Packed' },
        changedByUserId: 'u-1',
        changedByName: 'Super Admin',
        changedAt: '2026-07-20T09:00:00Z',
        note: 'Shipment created.'
      },
      {
        id: 'h-2',
        status: { id: 'st-transit', code: 'IN TRANSIT', label: 'In Transit' },
        changedByUserId: 'u-1',
        changedByName: 'Super Admin',
        changedAt: '2026-07-22T10:00:00Z',
        note: null
      }
    ],
    documents: [],
    ...overrides
  };
}

describe('ShipmentDetailComponent', () => {
  let fixture: ComponentFixture<ShipmentDetailComponent>;
  let httpMock: HttpTestingController;

  async function configure(permissions: string[] = ['Shipments.View', 'Shipments.Edit']): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [ShipmentDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: { hasPermission: (p: string) => permissions.includes(p), currentUser$: of(null) } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'shp-1' } } } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ShipmentDetailComponent);
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => httpMock.verify());

  function flushMasterData(): void {
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush(MASTER_DATA);
  }

  function flushDetail(d: ShipmentDetail = detail()): void {
    httpMock.expectOne('/api/v1/shipments/shp-1').flush(d);
  }

  describe('stepper', () => {
    it('builds steps from master data, not a hard-coded status ladder (DR-6)', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      flushDetail();
      fixture.detectChanges();

      const labels = Array.from(fixture.nativeElement.querySelectorAll('.sd-step-label')).map((e) =>
        (e as HTMLElement).textContent!.trim()
      );
      expect(labels).toEqual(['Packed', 'Dispatched', 'In Transit', 'Delivered']);
    });

    it('takes each step’s "when" from statusHistory, and shows "—" for stages not yet reached (D-33)', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      flushDetail();
      fixture.detectChanges();

      const whens = Array.from(fixture.nativeElement.querySelectorAll('.sd-step-when')).map((e) =>
        (e as HTMLElement).textContent!.trim()
      );
      // Packed reached, Dispatched skipped (no history row), In Transit reached, Delivered future.
      expect(whens[0]).toBe('20 Jul 2026');
      expect(whens[1]).toBe('—');
      expect(whens[2]).toBe('22 Jul 2026');
      expect(whens[3]).toBe('—');
    });

    it('marks stages before the current one done and the current one current', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      flushDetail();
      fixture.detectChanges();

      const steps = Array.from(fixture.nativeElement.querySelectorAll('.sd-step'));
      expect((steps[0] as HTMLElement).classList).toContain('sd-step--done');
      expect((steps[2] as HTMLElement).classList).toContain('sd-step--current');
      expect((steps[3] as HTMLElement).classList).not.toContain('sd-step--done');
    });
  });

  describe('status advance', () => {
    it('advances through PUT /status — the only path that writes history (D-43)', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      flushDetail();
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('.sd-actions button') as HTMLButtonElement;
      expect(button.textContent!.trim()).toBe('Mark Delivered');
      button.click();

      const req = httpMock.expectOne('/api/v1/shipments/shp-1/status');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ statusId: 'st-delivered' });
      req.flush(detail({ status: { id: 'st-delivered', code: 'DELIVERED', label: 'Delivered' } }));
    });

    it('offers no advance action at the final status', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      flushDetail(detail({ status: { id: 'st-delivered', code: 'DELIVERED', label: 'Delivered' } }));
      fixture.detectChanges();

      const buttons = Array.from(fixture.nativeElement.querySelectorAll('.sd-actions button')).map((b) =>
        (b as HTMLElement).textContent!.trim()
      );
      expect(buttons.some((b) => b.startsWith('Mark'))).toBeFalse();
    });

    it('surfaces a failed transition without wiping the screen', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      flushDetail();
      fixture.detectChanges();

      fixture.componentInstance.advanceStatus();
      httpMock
        .expectOne('/api/v1/shipments/shp-1/status')
        .flush({ title: 'Already in that status.' }, { status: 400, statusText: 'Bad Request' });
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.field-error').textContent).toContain('Already in that status.');
      expect(fixture.nativeElement.querySelector('.sd-stepper')).toBeTruthy();
    });
  });

  describe('document upload (N-20c)', () => {
    it('offers ONLY shipment-scoped document types', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      flushDetail();
      fixture.detectChanges();

      fixture.componentInstance.openUpload();
      fixture.detectChanges();

      const options = Array.from(fixture.nativeElement.querySelectorAll('#sd-doc-type option')).map((o) =>
        (o as HTMLOptionElement).textContent!.trim()
      );
      expect(options).toEqual(['Choose…', 'Packing List', 'Bill of Lading']);
      // The vendor-scoped type must never be offered here.
      expect(options).not.toContain('Business Licence');
    });

    it('refuses to upload without a type, and issues no request', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      flushDetail();
      fixture.detectChanges();

      const comp = fixture.componentInstance;
      comp.openUpload();
      comp.uploadFile.set(new File(['x'], 'packing.pdf', { type: 'application/pdf' }));
      comp.submitUpload();

      expect(comp.uploadError()).toContain('document type');
      httpMock.expectNone('/api/v1/shipments/shp-1/documents');
    });

    it('posts the file and the chosen type as multipart', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      flushDetail();
      fixture.detectChanges();

      const comp = fixture.componentInstance;
      comp.openUpload();
      comp.uploadFile.set(new File(['x'], 'packing.pdf', { type: 'application/pdf' }));
      comp.uploadTypeId.set('dt-packing');
      comp.submitUpload();

      const req = httpMock.expectOne('/api/v1/shipments/shp-1/documents');
      const body = req.request.body as FormData;
      expect(body.get('documentTypeId')).toBe('dt-packing');
      expect((body.get('file') as File).name).toBe('packing.pdf');
      req.flush({});
      // Re-reads the shipment so the rest of the screen stays consistent.
      flushDetail();
    });
  });

  describe('details panel', () => {
    it('shows "Recorded by" from recordedByName (D-47), not a hard-coded person', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      flushDetail(detail({ recordedByName: 'Vikram Nair' }));
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.sd-side').textContent).toContain('Vikram Nair');
    });

    it('reports no firm-held stock for a freight-only shipment (FSD A8)', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      flushDetail(
        detail({ serviceType: { id: 'svc-fo', code: 'FREIGHT_ONLY', label: 'Freight-only' }, lines: [] })
      );
      fixture.detectChanges();

      const side: string = fixture.nativeElement.querySelector('.sd-side').textContent;
      expect(side).toContain('No firm-held stock');
      expect(fixture.nativeElement.textContent).toContain('No lines on this shipment.');
    });
  });

  it('shows a retryable error instead of hanging when the shipment fails to load', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    httpMock.expectOne('/api/v1/shipments/shp-1').flush('boom', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.state-panel').textContent).toContain('Could not load this shipment');
  });
});
