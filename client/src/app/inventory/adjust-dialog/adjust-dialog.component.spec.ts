import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AdjustDialogComponent } from './adjust-dialog.component';

const ITEM = { id: 'inv-1', name: 'Silver Chain', sku: 'SKU-001', unit: 'pcs', onHandQty: 40 };

describe('AdjustDialogComponent', () => {
  let fixture: ComponentFixture<AdjustDialogComponent>;
  let httpMock: HttpTestingController;

  async function configure(item = ITEM): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [AdjustDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    fixture = TestBed.createComponent(AdjustDialogComponent);
    fixture.componentInstance.item = item;
    httpMock = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    httpMock.expectOne('/api/v1/inventory/inv-1/adjustments').flush({ items: [] });
  }

  afterEach(() => httpMock.verify());

  it('computes the live delta upward, downward, and as "No change" at zero, against the item\'s current onHandQty', async () => {
    await configure();
    const c = fixture.componentInstance;

    c.countedQty.set('45');
    expect(c.delta()).toBe(5);
    expect(c.deltaLabel()).toBe('+5');

    c.countedQty.set('37');
    expect(c.delta()).toBe(-3);
    expect(c.deltaLabel()).toBe('−3');

    c.countedQty.set('40');
    expect(c.delta()).toBe(0);
    expect(c.deltaLabel()).toBe('No change');
  });

  it('computes the delta correctly when adjusting from a negative current quantity', async () => {
    await configure({ ...ITEM, onHandQty: -5 });
    const c = fixture.componentInstance;

    c.countedQty.set('0');
    expect(c.delta()).toBe(5);
    expect(c.deltaLabel()).toBe('+5');
  });

  it('hides the delta preview while the counted quantity is blank or not a valid non-negative number', async () => {
    await configure();
    const c = fixture.componentInstance;

    expect(c.delta()).toBeNull();
    c.countedQty.set('abc');
    expect(c.delta()).toBeNull();
    c.countedQty.set('-1');
    expect(c.delta()).toBeNull();
  });

  it('blocks submit on a blank reason and shows the field-level message, without sending a request', async () => {
    await configure();
    const c = fixture.componentInstance;
    c.countedQty.set('37');
    c.reason.set('   ');
    c.save();

    fixture.detectChanges();
    expect(c.reasonTouched()).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('Reason is required.');
    httpMock.expectNone((r) => r.url.includes('/adjustments') && r.method === 'POST');
  });

  it('rejects a negative counted quantity client-side without sending a request', async () => {
    await configure();
    const c = fixture.componentInstance;
    c.reason.set('Physical count');
    c.countedQty.set('-2');
    c.save();

    expect(c.error()).toContain('non-negative');
    httpMock.expectNone((r) => r.url.includes('/adjustments') && r.method === 'POST');
  });

  it('sends the right payload on a valid submit, including the count date, and emits the response', async () => {
    await configure();
    const c = fixture.componentInstance;
    c.countedQty.set('37');
    c.reason.set('Physical count found 3 short');
    c.adjustedOn.set('2026-07-29');

    let emitted: unknown = null;
    c.adjusted.subscribe((r) => (emitted = r));
    c.save();

    const req = httpMock.expectOne((r) => r.method === 'POST' && r.url === '/api/v1/inventory/inv-1/adjustments');
    expect(req.request.body).toEqual({ countedQty: 37, reason: 'Physical count found 3 short', adjustedOn: '2026-07-29' });

    const result = {
      adjustment: {
        id: 'adj-1',
        countedQty: 37,
        previousQty: 40,
        delta: -3,
        reason: 'Physical count found 3 short',
        adjustedOn: '2026-07-29',
        adjustedAt: '2026-07-29T10:00:00Z',
        adjustedByUserId: 'user-1',
        adjustedByName: 'Priya Sharma'
      },
      item: { id: 'inv-1', onHandQty: 37 }
    };
    req.flush(result);

    expect(emitted).toEqual(result);
    expect(c.saving()).toBeFalse();
  });

  it('surfaces a server 400 on the `reason` field rather than the general banner', async () => {
    await configure();
    const c = fixture.componentInstance;
    c.countedQty.set('37');
    c.reason.set('x');
    c.save();

    const req = httpMock.expectOne((r) => r.method === 'POST' && r.url === '/api/v1/inventory/inv-1/adjustments');
    req.flush({ errors: { reason: ['Reason must be at least 3 characters.'] } }, { status: 400, statusText: 'Bad Request' });

    expect(c.reasonServerError()).toBe('Reason must be at least 3 characters.');
    expect(c.error()).toBeNull();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Reason must be at least 3 characters.');
  });
});
