import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { InventoryComponent } from './inventory.component';
import { AuthService } from '../core/services/auth.service';
import { MasterDataResponse } from '../core/models/master-data.models';
import { InventoryItem, InventorySummary } from './models/inventory.models';

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

function item(overrides: Partial<InventoryItem> = {}): InventoryItem {
  return {
    id: 'inv-1',
    name: 'Silver Chain',
    sku: 'SKU-001',
    description: null,
    sellingPrice: null,
    hsnCode: null,
    gstRate: null,
    category: { id: 'cat-jewellery', name: 'Jewellery' },
    vendor: { id: 'ven-1', name: 'Yiwu Jewel Craft Co.' },
    unit: 'pcs',
    onHandQty: 1840,
    reorderThreshold: 600,
    unitCost: 120,
    stockValue: 220800,
    stockLevel: 'HEALTHY',
    hasImage: false,
    thumbnailDataUrl: null,
    ...overrides
  };
}

const SUMMARY: InventorySummary = { onHandValue: 220800, itemCount: 1, lowStockCount: 0, negativeStockCount: 0 };

describe('InventoryComponent', () => {
  let fixture: ComponentFixture<InventoryComponent>;
  let httpMock: HttpTestingController;

  async function configure(permissions: string[] = []): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [InventoryComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: { hasPermission: (p: string) => permissions.includes(p), currentUser$: of(null) } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(InventoryComponent);
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => httpMock.verify());

  function flushMasterData(): void {
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush(MASTER_DATA);
  }

  function flushList(items: InventoryItem[], summary: InventorySummary = SUMMARY, totalCount = items.length): void {
    httpMock.expectOne((r) => r.url === '/api/v1/inventory').flush({ items, page: 1, pageSize: 25, totalCount, summary });
  }

  it('renders a row from the embedded category/vendor objects', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([item()]);
    fixture.detectChanges();

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('Silver Chain');
    expect(text).toContain('SKU-001');
    expect(text).toContain('Jewellery');
    expect(text).toContain('Yiwu Jewel Craft Co.');
    expect(text).toContain('HEALTHY');
  });

  it('renders "—" for a vendor-less item and for stockValue: null (D-30 — not costed, not worth zero)', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([item({ vendor: null, stockValue: null })]);
    fixture.detectChanges();

    const rows = fixture.componentInstance.rows();
    expect(rows[0].vendorName).toBe('—');
    expect(rows[0].valueLabel).toBe('—');
  });

  it('reads the four stat tiles from the response summary, not from the page of items', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([item()], { onHandValue: 4120000, itemCount: 9, lowStockCount: 3, negativeStockCount: 1 }, 50);
    fixture.detectChanges();

    const c = fixture.componentInstance;
    expect(c.itemCountLabel()).toBe('9');
    expect(c.belowReorderLabel()).toBe('3');
    expect(c.negativeStockLabel()).toBe('1');
    // Abbreviated, not full precision (D-61): the approved screen's tile reads
    // '₹41.2 L'. Asserted as the exact string rather than a substring — the whole
    // point of the change is the *form* of the number, so a loose match would pass
    // against the full-precision rendering this replaced.
    expect(c.onHandValueLabel()).toBe('₹41.2 L');
  });

  it('shows the low-stock banner only when the summary has non-zero counts, stating the real counts', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([item()], { onHandValue: 0, itemCount: 1, lowStockCount: 3, negativeStockCount: 1 });
    fixture.detectChanges();

    expect(fixture.componentInstance.showLowStockBanner()).toBeTrue();
    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('3 items below reorder threshold');
    expect(text).toContain('1 item with negative stock');
    expect(text).toContain('Show only low stock');
  });

  it('hides the low-stock banner when both counts are zero', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([item()], { onHandValue: 0, itemCount: 1, lowStockCount: 0, negativeStockCount: 0 });
    fixture.detectChanges();

    expect(fixture.componentInstance.showLowStockBanner()).toBeFalse();
  });

  it('"Show only low stock" sets the stock-level filter to LOW and refetches', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([item()], { onHandValue: 0, itemCount: 1, lowStockCount: 1, negativeStockCount: 0 });
    fixture.detectChanges();

    fixture.componentInstance.filterLowStock();

    const req = httpMock.expectOne((r) => r.url === '/api/v1/inventory');
    expect(req.request.params.get('stockLevel')).toBe('LOW');
    req.flush({ items: [], page: 1, pageSize: 25, totalCount: 0, summary: SUMMARY });
  });

  it('sends the category and stock-level filters as query params', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([]);
    fixture.detectChanges();

    fixture.componentInstance.setCategory('cat-stationery');
    let req = httpMock.expectOne((r) => r.url === '/api/v1/inventory');
    expect(req.request.params.get('categoryId')).toBe('cat-stationery');
    req.flush({ items: [], page: 1, pageSize: 25, totalCount: 0, summary: SUMMARY });

    fixture.componentInstance.setStockLevel('HEALTHY');
    req = httpMock.expectOne((r) => r.url === '/api/v1/inventory');
    expect(req.request.params.get('stockLevel')).toBe('HEALTHY');
    req.flush({ items: [], page: 1, pageSize: 25, totalCount: 0, summary: SUMMARY });
  });

  it('debounces the search box and sends it as the search query param', fakeAsync(async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([]);
    fixture.detectChanges();

    fixture.componentInstance.onSearchInput('chain');
    tick(350);

    const req = httpMock.expectOne((r) => r.url === '/api/v1/inventory');
    expect(req.request.params.get('search')).toBe('chain');
    req.flush({ items: [], page: 1, pageSize: 25, totalCount: 0, summary: SUMMARY });
  }));

  it('shows a visible error instead of hanging when the list request fails', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    httpMock
      .expectOne((r) => r.url === '/api/v1/inventory')
      .flush({ title: 'Server error', detail: 'Inventory lookup failed' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(fixture.componentInstance.loading()).toBeFalse();
    expect(fixture.componentInstance.error()).toBe('Inventory lookup failed');
    expect(fixture.nativeElement.textContent).toContain('Inventory lookup failed');
  });

  it('shows the empty-state message when no items match the filters', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No inventory items match these filters');
  });

  it('hides mutating actions without Inventory.Edit permission', async () => {
    await configure([]);
    fixture.detectChanges();
    flushMasterData();
    flushList([item()]);
    fixture.detectChanges();

    const text: string = fixture.nativeElement.textContent;
    expect(text).not.toContain('+ Add Item');
    expect(text).not.toContain('Edit');
  });

  it('shows Add Item and Edit with Inventory.Edit permission, and no inbound / adjust / shipment actions', async () => {
    await configure(['Inventory.Edit', 'Inventory.Adjust']);
    fixture.detectChanges();
    flushMasterData();
    flushList([item()]);
    fixture.detectChanges();

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('+ Add Item');
    expect(text).toContain('Edit');
    expect(text).not.toContain('Inbound');
    expect(text).not.toContain('Adjust Stock');
    expect(text).not.toContain('Record Shipment');
    expect(text).not.toContain('per pcs');
  });

  it('shows the current stock with its unit', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([item()]);
    fixture.detectChanges();

    const header: string = fixture.nativeElement.querySelector('thead').textContent;
    expect(header).toContain('Current Stock');
    expect(fixture.componentInstance.rows()[0].qtyLabel).toBe('1,840');
    expect(fixture.componentInstance.rows()[0].unit).toBe('pcs');
  });

  it('renders the inline thumbnail in front of the item, or a placeholder when there is none', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([item({ hasImage: true, thumbnailDataUrl: 'data:image/jpeg;base64,AAAA' }), item({ id: 'inv-2', name: 'No Image' })]);
    fixture.detectChanges();

    const thumbs = fixture.nativeElement.querySelectorAll('.item-thumb');
    expect(thumbs.length).toBe(2);
    expect(thumbs[0].querySelector('img')?.getAttribute('src')).toBe('data:image/jpeg;base64,AAAA');
    expect(thumbs[1].querySelector('img')).toBeNull();
  });
});
