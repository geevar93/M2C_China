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
    { id: 'cat-tools', name: 'Tools', sortOrder: 2, isActive: true }
  ],
  serviceTypes: [],
  leadStatuses: [],
  shipmentStatuses: [],
  invoiceStatuses: [],
  vendorStatuses: [],
  documentTypes: []
};

/** Field-for-field the shape a live `GET /inventory` returned during the contract diff. */
function item(overrides: Partial<InventoryItem> = {}): InventoryItem {
  return {
    id: 'inv-1',
    name: 'Adjustable Wrench 10in',
    sku: 'TLS-WRN-310',
    description: null,
    category: { id: 'cat-tools', name: 'Tools' },
    vendor: { id: 'ven-1', name: 'Ningbo Tools & Hardware' },
    unit: 'pc',
    onHandQty: 145,
    reorderThreshold: 200,
    unitCost: 700,
    stockValue: 101500,
    stockLevel: 'LOW',
    ...overrides
  };
}

function summary(overrides: Partial<InventorySummary> = {}): InventorySummary {
  return { onHandValue: 3450900, itemCount: 9, lowStockCount: 2, negativeStockCount: 1, ...overrides };
}

describe('InventoryComponent', () => {
  let fixture: ComponentFixture<InventoryComponent>;
  let httpMock: HttpTestingController;

  async function configure(permissions: string[] = ['Inventory.View']): Promise<void> {
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

  function flushList(items: InventoryItem[], s: InventorySummary = summary(), totalCount = items.length): void {
    httpMock
      .expectOne((r) => r.url === '/api/v1/inventory')
      .flush({ items, page: 1, pageSize: 25, totalCount, summary: s });
  }

  it('populates the category filter from MasterDataService, not a hard-coded list (DR-6)', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([item()]);
    fixture.detectChanges();

    const options = Array.from(fixture.nativeElement.querySelectorAll('#inv-cat option')).map((o) =>
      (o as HTMLOptionElement).textContent!.trim()
    );
    expect(options).toEqual(['All categories', 'Jewellery', 'Tools']);
  });

  it('offers ONE "Low or negative" stock-level option, matching the approved screen (E7-03)', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    flushList([item()]);
    fixture.detectChanges();

    const options = Array.from(fixture.nativeElement.querySelectorAll('#inv-level option')).map((o) =>
      (o as HTMLOptionElement).textContent!.trim()
    );
    expect(options).toEqual(['All stock levels', 'Low or negative', 'Healthy']);
  });

  describe('stat tiles', () => {
    it('reads the summary block rather than counting the current page (D-39)', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      // One row on the page, but a summary describing nine items across the filtered set.
      flushList([item()], summary({ itemCount: 9, lowStockCount: 2, negativeStockCount: 1 }), 9);
      fixture.detectChanges();

      const values = Array.from(fixture.nativeElement.querySelectorAll('.stat-tile__value')).map((v) =>
        (v as HTMLElement).textContent!.trim()
      );
      expect(values).toEqual(['₹34.5 L', '9', '2', '1']);
    });

    it('does not tint the counts when there is nothing wrong', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      flushList([item({ stockLevel: 'HEALTHY' })], summary({ lowStockCount: 0, negativeStockCount: 0 }));
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.inv-stat--warn')).toBeFalsy();
      expect(fixture.nativeElement.querySelector('.inv-stat--danger')).toBeFalsy();
    });
  });

  describe('stock value', () => {
    it('renders an uncosted item as "—", never as ₹0', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      flushList([item({ unitCost: null, stockValue: null })]);
      fixture.detectChanges();

      const cells: string = fixture.nativeElement.querySelector('tbody tr').textContent;
      expect(cells).toContain('—');
      expect(cells).not.toContain('₹0');
    });

    it('formats a costed item in en-IN rupees', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      flushList([item({ stockValue: 101500 })]);
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('tbody tr').textContent).toContain('₹1,01,500.00');
    });
  });

  describe('level bar', () => {
    it('pins an oversold row full-width and flags the row itself', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      flushList([item({ onHandQty: -40, stockLevel: 'NEGATIVE' })]);
      fixture.detectChanges();

      const fill = fixture.nativeElement.querySelector('.inv-bar-fill') as HTMLElement;
      expect(fill.style.width).toBe('100%');
      expect(fixture.nativeElement.querySelector('.inv-row-negative')).toBeTruthy();
    });

    it('scales the bar by qty / (reorder * 2.5), as the prototype does', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      // 145 / (200 * 2.5) = 29%
      flushList([item({ onHandQty: 145, reorderThreshold: 200 })]);
      fixture.detectChanges();

      expect((fixture.nativeElement.querySelector('.inv-bar-fill') as HTMLElement).style.width).toBe('29%');
    });

    it('does not divide by zero when an item has no reorder threshold', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      flushList([item({ onHandQty: 10, reorderThreshold: 0, stockLevel: 'HEALTHY' })]);
      fixture.detectChanges();

      expect((fixture.nativeElement.querySelector('.inv-bar-fill') as HTMLElement).style.width).toBe('100%');
    });

    it('takes the level from the API rather than re-deriving it from the quantities (E7-04)', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      // Quantities that a client-side classifier would call HEALTHY, but the API says LOW.
      // The screen must show what the API (and therefore the filter) says.
      flushList([item({ onHandQty: 900, reorderThreshold: 200, stockLevel: 'LOW' })]);
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.inv-level .chip').textContent.trim()).toBe('LOW');
    });
  });

  describe('filters', () => {
    it('sends stockLevel=low and hides the banner shortcut once applied', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      flushList([item()], summary({ lowStockCount: 2 }));
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.inv-alert-link')).toBeTruthy();
      fixture.componentInstance.filterLowStock();

      const req = httpMock.expectOne((r) => r.url === '/api/v1/inventory');
      expect(req.request.params.get('stockLevel')).toBe('low');
      req.flush({ items: [item()], page: 1, pageSize: 25, totalCount: 1, summary: summary() });
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.inv-alert-link')).toBeFalsy();
    });

    it('debounces search and resets to page 1', fakeAsync(async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      flushList([item()], summary(), 100);

      fixture.componentInstance.onSearchInput('wren');
      tick(300);

      const req = httpMock.expectOne((r) => r.url === '/api/v1/inventory');
      expect(req.request.params.get('search')).toBe('wren');
      expect(req.request.params.get('page')).toBe('1');
      req.flush({ items: [], page: 1, pageSize: 25, totalCount: 0, summary: summary() });
    }));
  });

  describe('alert banner', () => {
    it('stays hidden when nothing is low or negative', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      flushList([item({ stockLevel: 'HEALTHY' })], summary({ lowStockCount: 0, negativeStockCount: 0 }));
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.inv-alert')).toBeFalsy();
    });

    it('describes both conditions from live counts, not the prototype’s hard-coded copy', async () => {
      await configure();
      fixture.detectChanges();
      flushMasterData();
      flushList([item()], summary({ lowStockCount: 3, negativeStockCount: 1 }));
      fixture.detectChanges();

      const text: string = fixture.nativeElement.querySelector('.inv-alert').textContent;
      expect(text).toContain('3 items below reorder threshold.');
      expect(text).toContain('1 item is oversold');
    });
  });

  it('shows a retryable error instead of hanging when the list request fails', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    httpMock.expectOne((r) => r.url === '/api/v1/inventory').flush('boom', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.state-panel').textContent).toContain('Could not load inventory');
  });
});
