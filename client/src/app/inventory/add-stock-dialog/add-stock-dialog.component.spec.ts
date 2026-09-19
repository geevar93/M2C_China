import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AddStockDialogComponent } from './add-stock-dialog.component';
import { InventoryItem } from '../models/inventory.models';

const ITEM: InventoryItem = {
  id: 'inv-1',
  name: 'Silver Chain',
  sku: 'SKU-001',
  description: null,
  category: { id: 'cat-jewellery', name: 'Jewellery' },
  vendor: null,
  unit: 'pcs',
  onHandQty: 120,
  reorderThreshold: 600,
  unitCost: 120,
  sellingPrice: null,
  hsnCode: null,
  gstRate: null,
  stockValue: 14400,
  stockLevel: 'LOW',
  hasImage: false,
  thumbnailDataUrl: null
};

describe('AddStockDialogComponent', () => {
  let fixture: ComponentFixture<AddStockDialogComponent>;
  let component: AddStockDialogComponent;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AddStockDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    fixture = TestBed.createComponent(AddStockDialogComponent);
    component = fixture.componentInstance;
    component.item = ITEM;
    httpMock = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => httpMock.verify());

  it('previews the balance after adding the entered quantity', () => {
    expect(component.newTotalLabel()).toBeNull();
    component.quantity.set('80');
    expect(component.newTotalLabel()).toBe(component.currentLabel().replace('120', '200'));
  });

  it('does not save a zero, negative or empty quantity', () => {
    component.save();
    component.quantity.set('0');
    component.save();
    component.quantity.set('-5');
    component.save();
    httpMock.expectNone(() => true);
    expect(component.canSave()).toBeFalse();
  });

  it('posts only the delta to /inbound and emits the updated item', () => {
    let saved: InventoryItem | undefined;
    component.saved.subscribe((i) => (saved = i));

    component.quantity.set('80');
    component.reference.set('  PO-42 ');
    component.save();

    const req = httpMock.expectOne((r) => r.method === 'POST' && r.url === '/api/v1/inventory/inv-1/inbound');
    expect(req.request.body.quantity).toBe(80);
    expect(req.request.body.reference).toBe('PO-42');
    expect(req.request.body.entryDate).toMatch(/^\d{4}-\d{2}-\d{2}$/);

    const updated = { ...ITEM, onHandQty: 200 };
    req.flush({ entry: {}, item: updated });
    expect(saved).toEqual(updated);
  });

  it('shows the API error and stays open when the save fails', () => {
    let closed = false;
    component.closed.subscribe(() => (closed = true));

    component.quantity.set('10');
    component.save();
    httpMock
      .expectOne((r) => r.url === '/api/v1/inventory/inv-1/inbound')
      .flush({ title: 'Nope' }, { status: 500, statusText: 'Server Error' });

    expect(component.error()).toBeTruthy();
    expect(component.saving()).toBeFalse();
    expect(closed).toBeFalse();
  });
});
