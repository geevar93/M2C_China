import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ItemFormDialogComponent } from './item-form-dialog.component';
import { MasterDataResponse } from '../../core/models/master-data.models';
import { InventoryItem } from '../models/inventory.models';

const MASTER_DATA: MasterDataResponse = {
  categories: [
    { id: 'cat-jewellery', name: 'Jewellery', sortOrder: 1, isActive: true },
    { id: 'cat-stationery', name: 'Stationery', sortOrder: 2, isActive: true }
  ],
  serviceTypes: [],
  leadStatuses: [],
  shipmentStatuses: [],
  invoiceStatuses: [],
  vendorStatuses: [],
  documentTypes: []
};

const EXISTING_ITEM: InventoryItem = {
  id: 'inv-1',
  name: 'Silver Chain',
  sku: 'SKU-001',
  description: null,
  category: { id: 'cat-jewellery', name: 'Jewellery' },
  vendor: { id: 'ven-1', name: 'Yiwu Jewel Craft Co.' },
  unit: 'pcs',
  onHandQty: 1840,
  reorderThreshold: 600,
  unitCost: 120,
  sellingPrice: null,
  hsnCode: null,
  gstRate: null,
  stockValue: 220800,
  stockLevel: 'HEALTHY',
  hasImage: false,
  thumbnailDataUrl: null
};

describe('ItemFormDialogComponent', () => {
  let fixture: ComponentFixture<ItemFormDialogComponent>;
  let httpMock: HttpTestingController;

  async function configure(): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [ItemFormDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    fixture = TestBed.createComponent(ItemFormDialogComponent);
    httpMock = TestBed.inject(HttpTestingController);
  }

  function flushLookups(): void {
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush(MASTER_DATA);
    httpMock.expectOne((r) => r.url === '/api/v1/vendors').flush({ items: [], page: 1, pageSize: 100, totalCount: 0 });
  }

  afterEach(() => httpMock.verify());

  it('strips digits and symbols from the unit as it is typed', async () => {
    await configure();
    fixture.detectChanges();
    flushLookups();

    const input = { value: 'pcs 12/' } as HTMLInputElement;
    fixture.componentInstance.onUnitInput({ target: input } as unknown as Event);

    expect(input.value).toBe('pcs ');
    expect(fixture.componentInstance.unit()).toBe('pcs ');
  });

  it('refuses to save a unit that is not letters only', async () => {
    await configure();
    fixture.detectChanges();
    flushLookups();

    const c = fixture.componentInstance;
    c.name.set('Pen');
    c.categoryId.set('cat-stationery');
    c.unit.set('50');
    c.reorderThreshold.set('10');
    c.save();

    expect(c.error()).toContain('letters only');
    httpMock.expectNone((r) => r.method === 'POST');
  });

  it('creates an item via POST /inventory including the opening on-hand quantity', async () => {
    await configure();
    fixture.detectChanges();
    flushLookups();
    fixture.detectChanges();

    const c = fixture.componentInstance;
    c.name.set('Ballpoint Pen Bulk Pack');
    c.categoryId.set('cat-stationery');
    c.unit.set('box');
    c.reorderThreshold.set('100');
    c.onHandQty.set('500');
    c.save();

    const req = httpMock.expectOne((r) => r.method === 'POST' && r.url === '/api/v1/inventory');
    expect(req.request.body.onHandQty).toBe(500);
    expect(req.request.body.categoryId).toBe('cat-stationery');
    req.flush({ ...EXISTING_ITEM, id: 'inv-2' });
  });

  it('blocks save with a validation message when required fields are missing', async () => {
    await configure();
    fixture.detectChanges();
    flushLookups();
    fixture.detectChanges();

    fixture.componentInstance.save();

    expect(fixture.componentInstance.error()).toContain('required');
    httpMock.expectNone('/api/v1/inventory');
  });

  it('prefills every field from the item input in edit mode', async () => {
    await configure();
    fixture.componentInstance.item = EXISTING_ITEM;
    fixture.detectChanges();
    flushLookups();
    fixture.detectChanges();

    const c = fixture.componentInstance;
    expect(c.name()).toBe('Silver Chain');
    expect(c.sku()).toBe('SKU-001');
    expect(c.categoryId()).toBe('cat-jewellery');
    expect(c.reorderThreshold()).toBe('600');
    expect(c.unitCost()).toBe('120');
  });

  it('the edit form shows an editable current-stock input prefilled with the on-hand quantity', async () => {
    await configure();
    fixture.componentInstance.item = EXISTING_ITEM;
    fixture.detectChanges();
    flushLookups();
    fixture.detectChanges();

    const onHandInput: HTMLInputElement | null = fixture.nativeElement.querySelector('#if-onhand');
    expect(onHandInput).not.toBeNull();
    expect(onHandInput!.value).toBe('1840');
  });

  it('sends onHandQty on update only when the count was edited', async () => {
    await configure();
    fixture.componentInstance.item = EXISTING_ITEM;
    fixture.detectChanges();
    flushLookups();
    fixture.detectChanges();

    const c = fixture.componentInstance;
    c.onHandQty.set('1800');
    c.save();

    const req = httpMock.expectOne((r) => r.method === 'PUT' && r.url === '/api/v1/inventory/inv-1');
    expect(req.request.body.onHandQty).toBe(1800);
    req.flush({ ...EXISTING_ITEM, onHandQty: 1800 });
  });

  it('does not render HSN/SAC or GST rate fields, and passes stored values through on update', async () => {
    await configure();
    fixture.componentInstance.item = { ...EXISTING_ITEM, hsnCode: '7117', gstRate: 3 };
    fixture.detectChanges();
    flushLookups();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#if-hsn')).toBeNull();
    expect(fixture.nativeElement.querySelector('#if-gst')).toBeNull();

    fixture.componentInstance.save();
    const req = httpMock.expectOne((r) => r.method === 'PUT' && r.url === '/api/v1/inventory/inv-1');
    expect(req.request.body.hsnCode).toBe('7117');
    expect(req.request.body.gstRate).toBe(3);
    req.flush(EXISTING_ITEM);
  });

  it('shows the existing thumbnail as the image preview in edit mode', async () => {
    await configure();
    fixture.componentInstance.item = { ...EXISTING_ITEM, hasImage: true, thumbnailDataUrl: 'data:image/jpeg;base64,AAAA' };
    fixture.detectChanges();
    flushLookups();
    fixture.detectChanges();

    const img: HTMLImageElement | null = fixture.nativeElement.querySelector('.ifd-image-preview img');
    expect(img?.getAttribute('src')).toBe('data:image/jpeg;base64,AAAA');
  });

  it('removing an existing image calls DELETE /inventory/{id}/image after saving', async () => {
    await configure();
    fixture.componentInstance.item = { ...EXISTING_ITEM, hasImage: true, thumbnailDataUrl: 'data:image/jpeg;base64,AAAA' };
    fixture.detectChanges();
    flushLookups();
    fixture.detectChanges();

    const c = fixture.componentInstance;
    c.clearImage();
    c.save();

    httpMock.expectOne((r) => r.method === 'PUT' && r.url === '/api/v1/inventory/inv-1')
      .flush({ ...EXISTING_ITEM, hasImage: true });
    httpMock.expectOne((r) => r.method === 'DELETE' && r.url === '/api/v1/inventory/inv-1/image')
      .flush({ ...EXISTING_ITEM, hasImage: false, thumbnailDataUrl: null });
  });

  it('does not send onHandQty on update when the count is unchanged', async () => {
    await configure();
    fixture.componentInstance.item = EXISTING_ITEM;
    fixture.detectChanges();
    flushLookups();
    fixture.detectChanges();

    const c = fixture.componentInstance;
    c.reorderThreshold.set('700');
    c.save();

    const req = httpMock.expectOne((r) => r.method === 'PUT' && r.url === '/api/v1/inventory/inv-1');
    expect(req.request.body.reorderThreshold).toBe(700);
    expect(req.request.body.onHandQty).toBeUndefined();
    req.flush({ ...EXISTING_ITEM, reorderThreshold: 700 });
  });

  it('the create form does show an editable opening on-hand-quantity input', async () => {
    await configure();
    fixture.detectChanges();
    flushLookups();
    fixture.detectChanges();

    const onHandInput = fixture.nativeElement.querySelector('#if-onhand');
    expect(onHandInput).not.toBeNull();
  });

  it('surfaces the ProblemDetails message when the save fails', async () => {
    await configure();
    fixture.detectChanges();
    flushLookups();
    fixture.detectChanges();

    const c = fixture.componentInstance;
    c.name.set('X');
    c.categoryId.set('cat-jewellery');
    c.unit.set('pcs');
    c.reorderThreshold.set('10');
    c.save();

    httpMock
      .expectOne((r) => r.url === '/api/v1/inventory')
      .flush({ title: 'Conflict', detail: 'An item with this SKU already exists' }, { status: 409, statusText: 'Conflict' });

    expect(fixture.componentInstance.saving()).toBeFalse();
    expect(fixture.componentInstance.error()).toBe('An item with this SKU already exists');
  });
});
