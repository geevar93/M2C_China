import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { InboundDialogComponent } from './inbound-dialog.component';

describe('InboundDialogComponent', () => {
  let fixture: ComponentFixture<InboundDialogComponent>;
  let httpMock: HttpTestingController;

  async function configure(): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [InboundDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    fixture = TestBed.createComponent(InboundDialogComponent);
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => httpMock.verify());

  it('records inbound stock for a preset item without showing an item picker', async () => {
    await configure();
    fixture.componentInstance.presetItem = { id: 'inv-1', name: 'Silver Chain', sku: 'SKU-001', unit: 'pcs' };
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#ibd-item')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Silver Chain');

    const c = fixture.componentInstance;
    c.quantity.set('200');
    c.entryDate.set('2026-07-29');
    c.reference.set('PO-100');
    c.save();

    const req = httpMock.expectOne((r) => r.method === 'POST' && r.url === '/api/v1/inventory/inv-1/inbound');
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
      item: { id: 'inv-1' }
    });
  });

  it('shows an item picker and requires a selection when there is no preset item', async () => {
    await configure();
    fixture.componentInstance.candidateItems = [{ id: 'inv-1', name: 'Silver Chain', sku: 'SKU-001', unit: 'pcs' }];
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#ibd-item')).not.toBeNull();

    fixture.componentInstance.quantity.set('50');
    fixture.componentInstance.entryDate.set('2026-07-29');
    fixture.componentInstance.save();

    expect(fixture.componentInstance.error()).toContain('Choose an item');
    httpMock.expectNone((r) => r.url.includes('/inbound'));
  });

  it('rejects a non-positive quantity', async () => {
    await configure();
    fixture.componentInstance.presetItem = { id: 'inv-1', name: 'Silver Chain', sku: 'SKU-001', unit: 'pcs' };
    fixture.detectChanges();

    fixture.componentInstance.quantity.set('0');
    fixture.componentInstance.save();

    expect(fixture.componentInstance.error()).toContain('positive');
    httpMock.expectNone((r) => r.url.includes('/inbound'));
  });
});
