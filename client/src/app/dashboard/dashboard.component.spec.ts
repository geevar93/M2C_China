import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { DashboardComponent } from './dashboard.component';
import {
  CategoryMixAnalytics,
  DispatchAnalytics,
  InventoryAnalytics,
  LeadsAnalytics,
  ServiceSplitAnalytics
} from './models/dashboard.models';

const LEADS: LeadsAnalytics = {
  series: [
    { periodStart: '2026-07-01', count: 10 },
    { periodStart: '2026-07-08', count: 25 }
  ],
  bySource: [],
  byStatus: [
    { id: 's-new', code: 'NEW', label: 'New', sortOrder: 1, count: 128 },
    { id: 's-qual', code: 'QUALIFIED', label: 'Qualified', sortOrder: 2, count: 74 },
    { id: 's-active', code: 'ACTIVE', label: 'Active', sortOrder: 3, count: 41 },
    { id: 's-won', code: 'WON', label: 'Won', sortOrder: 4, count: 23 },
    { id: 's-lost', code: 'LOST', label: 'Lost', sortOrder: 5, count: 12 },
    { id: 's-dormant', code: 'DORMANT', label: 'Dormant', sortOrder: 6, count: 0 }
  ],
  totalLeads: 128,
  wonCount: 23,
  conversionRate: 0.18,
  currentPeriodCount: 18,
  priorPeriodCount: 15
};

const SERVICE_SPLIT: ServiceSplitAnalytics = {
  totalCustomers: 96,
  items: [
    { id: 'svc-cif', code: 'CIF', label: 'CIF', sortOrder: 1, customerCount: 60 },
    { id: 'svc-freight', code: 'FREIGHT_ONLY', label: 'Freight-only', sortOrder: 2, customerCount: 36 }
  ]
};

const CATEGORY_MIX: CategoryMixAnalytics = {
  items: [
    { id: 'cat-elec', code: 'ELECTRONICS', label: 'Electronics', sortOrder: 1, customerCount: 41 },
    { id: 'cat-jewel', code: 'JEWELLERY', label: 'Jewellery', sortOrder: 2, customerCount: 34 },
    // Zero-count row — must still render, not be filtered out (every lookup zero-fills).
    { id: 'cat-tools', code: 'TOOLS', label: 'Tools', sortOrder: 3, customerCount: 0 }
  ]
};

const INVENTORY: InventoryAnalytics = {
  onHandValue: 4120000,
  byCategory: [],
  shipmentsByStatus: [
    { id: 'ship-packed', code: 'PACKED', label: 'PACKED', sortOrder: 1, count: 6 },
    { id: 'ship-dispatched', code: 'DISPATCHED', label: 'DISPATCHED', sortOrder: 2, count: 9 },
    { id: 'ship-transit', code: 'IN TRANSIT', label: 'IN TRANSIT', sortOrder: 3, count: 14 },
    { id: 'ship-delivered', code: 'DELIVERED', label: 'DELIVERED', sortOrder: 4, count: 132 }
  ],
  inTransitCount: 14,
  pastEtaCount: 2
};

const DISPATCH: DispatchAnalytics = {
  series: [],
  byStaff: [],
  byKind: [
    { kind: 'CatalogDispatched', count: 180 },
    { kind: 'InvoiceDispatched', count: 34 }
  ],
  totalDispatches: 214
};

describe('DashboardComponent', () => {
  let fixture: ComponentFixture<DashboardComponent>;
  let httpMock: HttpTestingController;

  async function configure(): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    }).compileComponents();

    fixture = TestBed.createComponent(DashboardComponent);
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => httpMock.verify());

  function flushLeads(body: LeadsAnalytics = LEADS): void {
    httpMock.expectOne((r) => r.url === '/api/v1/analytics/leads').flush(body);
  }

  function flushServiceSplit(body: ServiceSplitAnalytics = SERVICE_SPLIT): void {
    httpMock.expectOne((r) => r.url === '/api/v1/analytics/service-split').flush(body);
  }

  function flushCategoryMix(body: CategoryMixAnalytics = CATEGORY_MIX): void {
    httpMock.expectOne((r) => r.url === '/api/v1/analytics/category-mix').flush(body);
  }

  function flushInventory(body: InventoryAnalytics = INVENTORY): void {
    httpMock.expectOne((r) => r.url === '/api/v1/analytics/inventory').flush(body);
  }

  function flushDispatch(body: DispatchAnalytics = DISPATCH): void {
    httpMock.expectOne((r) => r.url === '/api/v1/analytics/dispatch').flush(body);
  }

  function flushAll(): void {
    flushLeads();
    flushServiceSplit();
    flushCategoryMix();
    flushInventory();
    flushDispatch();
  }

  it('derives all six KPI tiles from the mocked aggregates', async () => {
    await configure();
    fixture.detectChanges();
    flushAll();
    fixture.detectChanges();

    const tiles = fixture.componentInstance.kpis();
    expect(tiles.map((t) => t.value)).toEqual(['18', '96', '214', '₹41.2 L', '14', '18.0%']);
    expect(tiles[0].sub).toBe('+20% vs prior period');
    expect(tiles[1].sub).toBe('60 CIF · 36 freight-only');
    expect(tiles[2].sub).toBe('180 of 214 catalog sends');
    expect(tiles[3].sub).toBeNull(); // On-Hand Value: no reorder-level field in the schema — the known gap.
    expect(tiles[4].sub).toBe('2 past ETA');
    expect(tiles[5].sub).toBe('23 won of 128 leads');
  });

  it('guards the New Leads tile against a priorPeriodCount of 0 (no Infinity/NaN)', async () => {
    await configure();
    fixture.detectChanges();
    flushLeads({ ...LEADS, currentPeriodCount: 5, priorPeriodCount: 0 });
    flushServiceSplit();
    flushCategoryMix();
    flushInventory();
    flushDispatch();
    fixture.detectChanges();

    const tiles = fixture.componentInstance.kpis();
    expect(tiles[0].sub).toBe('New this period');
    expect(tiles[0].sub).not.toContain('Infinity');
    expect(tiles[0].sub).not.toContain('NaN');
  });

  it('guards the New Leads tile when both current and prior are 0', async () => {
    await configure();
    fixture.detectChanges();
    flushLeads({ ...LEADS, currentPeriodCount: 0, priorPeriodCount: 0 });
    flushServiceSplit();
    flushCategoryMix();
    flushInventory();
    flushDispatch();
    fixture.detectChanges();

    expect(fixture.componentInstance.kpis()[0].sub).toBe('No leads yet');
  });

  it('renders conversionRate (0..1) as a one-decimal percentage, not double-converted', async () => {
    await configure();
    fixture.detectChanges();
    flushLeads({ ...LEADS, conversionRate: 0.5 });
    flushServiceSplit();
    flushCategoryMix();
    flushInventory();
    flushDispatch();
    fixture.detectChanges();

    expect(fixture.componentInstance.kpis()[5].value).toBe('50.0%');
  });

  it('computes lead funnel bar widths as a percentage of the largest stage, excluding LOST/DORMANT', async () => {
    await configure();
    fixture.detectChanges();
    flushAll();
    fixture.detectChanges();

    const funnel = fixture.componentInstance.funnelView();
    expect(funnel?.rows.map((r) => r.label)).toEqual(['New', 'Qualified', 'Active', 'Won']);
    // max = 128 (New) -> Qualified 74/128=58%, Active 41/128=32%, Won 23/128=18%.
    expect(funnel?.rows.map((r) => r.w)).toEqual([100, 58, 32, 18]);
    expect(funnel?.conversion).toBe('18.0%');
  });

  it('guards bar widths to 0 across an all-zero series, not NaN/Infinity', async () => {
    await configure();
    fixture.detectChanges();
    flushLeads();
    flushServiceSplit();
    flushCategoryMix({
      items: [
        { id: 'a', code: 'A', label: 'A', sortOrder: 1, customerCount: 0 },
        { id: 'b', code: 'B', label: 'B', sortOrder: 2, customerCount: 0 }
      ]
    });
    flushInventory();
    flushDispatch();
    fixture.detectChanges();

    const rows = fixture.componentInstance.categoryMixView();
    expect(rows).toEqual([
      { label: 'A', n: 0, w: 0 },
      { label: 'B', n: 0, w: 0 }
    ]);
  });

  it('still renders a zero-count lookup row rather than filtering it out', async () => {
    await configure();
    fixture.detectChanges();
    flushAll();
    fixture.detectChanges();

    const rows = fixture.componentInstance.categoryMixView();
    expect(rows?.length).toBe(3);
    expect(rows?.[2]).toEqual({ label: 'Tools', n: 0, w: 0 });
  });

  it('renders the Shipments by Status panel from inventory.shipmentsByStatus with per-status colours', async () => {
    await configure();
    fixture.detectChanges();
    flushAll();
    fixture.detectChanges();

    const ship = fixture.componentInstance.shipmentsByStatusView();
    expect(ship?.rows.map((r) => r.label)).toEqual(['PACKED', 'DISPATCHED', 'IN TRANSIT', 'DELIVERED']);
    // max = 132 (DELIVERED) -> PACKED 6/132=5%, DISPATCHED 9/132=7%, IN TRANSIT 14/132=11%.
    expect(ship?.rows.map((r) => r.w)).toEqual([5, 7, 11, 100]);
    expect(ship?.overdueLabel).toBe('2 shipments overdue on ETA');
  });

  it('one failed aggregate does not blank the cards backed by the other four', async () => {
    await configure();
    fixture.detectChanges();

    flushLeads();
    httpMock.expectOne((r) => r.url === '/api/v1/analytics/service-split').flush(
      { title: 'Server error' },
      { status: 500, statusText: 'Internal Server Error' }
    );
    flushCategoryMix();
    flushInventory();
    flushDispatch();
    fixture.detectChanges();

    const c = fixture.componentInstance;
    // The failed card (service split -> Active Customers tile) is in error state...
    expect(c.serviceSplitStatus()).toBe('error');
    expect(c.kpis()[1].status).toBe('error');
    // ...but every other card, sourced from aggregates that succeeded, still renders.
    expect(c.funnelView()).not.toBeNull();
    expect(c.categoryMixView()).not.toBeNull();
    expect(c.shipmentsByStatusView()).not.toBeNull();
    expect(c.kpis()[0].status).toBe('ready'); // New Leads
    expect(c.kpis()[2].status).toBe('ready'); // Catalogs Sent
    expect(c.kpis()[3].status).toBe('ready'); // On-Hand Value
    expect(c.kpis()[4].status).toBe('ready'); // In Transit
    expect(c.kpis()[5].status).toBe('ready'); // Lead Conversion
  });

  it('re-queries all five aggregates when the date range changes', async () => {
    await configure();
    fixture.detectChanges();
    flushAll();
    fixture.detectChanges();

    fixture.componentInstance.setRange('Last 7 days');

    httpMock.expectOne((r) => r.url === '/api/v1/analytics/leads').flush(LEADS);
    httpMock.expectOne((r) => r.url === '/api/v1/analytics/service-split').flush(SERVICE_SPLIT);
    httpMock.expectOne((r) => r.url === '/api/v1/analytics/category-mix').flush(CATEGORY_MIX);
    httpMock.expectOne((r) => r.url === '/api/v1/analytics/inventory').flush(INVENTORY);
    httpMock.expectOne((r) => r.url === '/api/v1/analytics/dispatch').flush(DISPATCH);
  });
});
