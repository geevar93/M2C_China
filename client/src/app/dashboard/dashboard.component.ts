import { NgClass } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { extractErrorMessage } from '../core/services/problem-details.util';
import { LEAD_FUNNEL_STAGE_CODES } from '../shared/constants/lead-status-codes';
import { SERVICE_TYPE_CIF, SERVICE_TYPE_FREIGHT_ONLY } from '../shared/constants/service-type-codes';
import { StatusStyleService } from '../shared/services/status-style.service';
import { formatInrCompact } from '../shared/utils/money.util';
import { DashboardAnalyticsService } from './services/dashboard-analytics.service';
import {
  AnalyticsLookupCount,
  CategoryMixAnalytics,
  DispatchAnalytics,
  InventoryAnalytics,
  LeadsAnalytics,
  ServiceSplitAnalytics
} from './models/dashboard.models';
import {
  DateRangeOption,
  axisLabelIndices,
  barWidthPct,
  donutDashArray,
  formatAxisDate,
  formatConversionRate,
  linePoints,
  maxOf,
  percentChange,
  pluralize,
  rangeSuffix,
  resolveDateRange,
  toPolylinePoints
} from './utils/dashboard.util';

type LoadStatus = 'loading' | 'loaded' | 'error';

interface LoadState<T> {
  status: LoadStatus;
  data: T | null;
  error: string | null;
}

function loadingState<T>(): LoadState<T> {
  return { status: 'loading', data: null, error: null };
}

/** A KPI stat tile, independently loading/erroring per its own source aggregate (below). */
interface KpiTile {
  label: string;
  status: 'loading' | 'ready' | 'error';
  value: string;
  sub: string | null;
  subClass: 'muted' | 'success' | 'warning' | 'danger';
}

interface FunnelRow {
  label: string;
  n: number;
  pct: string;
  w: number;
}

interface CategoryMixRow {
  label: string;
  n: number;
  w: number;
}

interface ShipmentStatusRow {
  label: string;
  n: number;
  w: number;
  bg: string;
  fg: string;
}

const DATE_RANGE_OPTIONS: DateRangeOption[] = ['Last 7 days', 'Last 30 days', 'This quarter'];

/**
 * Operations Dashboard (ACTION_PLAN E10-09) — ported 1:1 from the approved
 * prototype's `showDash` block (`Source/Sourcing Ops Platform.dc.html`
 * ~line 70-170; view-model ~line 1568-1580). This is the app's post-login
 * landing screen (`app.routes.ts`, `Analytics.View`).
 *
 * There is deliberately no summary endpoint: every stat tile is derived here
 * from the six `/analytics/*` aggregates so a tile can never disagree with
 * the chart printed directly beneath it (a single summary query computed
 * independently could drift out of sync with the detail it summarises).
 *
 * Each of the five source aggregates this screen consumes loads and renders
 * independently (`LoadState<T>` per source) — one slow/failed call greys out
 * only the tiles/panels that depend on it, never the whole page.
 *
 * `GET /analytics/vendors` (the sixth endpoint in the contract) has no
 * consumer on this screen — the prototype's dash view-model never reads a
 * vendors aggregate, only `DashboardAnalyticsService.vendors()` exists for a
 * future Vendors-analytics screen to reuse.
 *
 * The On-Hand Value tile's "N items below reorder" sub-line (N-34) is amber
 * only when `belowReorderCount > 0`; at `0` it renders in the neutral/muted
 * colour rather than amber — amber signals "needs attention", and a
 * permanently-amber tile reading zero (reorder thresholds default to 0 today,
 * so this will read 0 for the foreseeable future) would train the user to
 * ignore the colour. The sub-line is never hidden at zero: showing "0 items
 * below reorder" is what tells the user the feature exists and is working.
 */
@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [RouterLink, NgClass],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent {
  private readonly analytics = inject(DashboardAnalyticsService);
  private readonly statusStyle = inject(StatusStyleService);

  readonly dateRangeOptions = DATE_RANGE_OPTIONS;
  readonly dateRange = signal<DateRangeOption>('Last 30 days');
  readonly subtitle = computed(() => `${this.dateRange()} · all categories · all service types`);

  private readonly leadsState = signal<LoadState<LeadsAnalytics>>(loadingState());
  private readonly serviceSplitState = signal<LoadState<ServiceSplitAnalytics>>(loadingState());
  private readonly categoryMixState = signal<LoadState<CategoryMixAnalytics>>(loadingState());
  private readonly inventoryState = signal<LoadState<InventoryAnalytics>>(loadingState());
  private readonly dispatchState = signal<LoadState<DispatchAnalytics>>(loadingState());

  readonly kpis = computed<KpiTile[]>(() => [
    this.newLeadsTile(),
    this.activeCustomersTile(),
    this.catalogsSentTile(),
    this.onHandValueTile(),
    this.inTransitTile(),
    this.leadConversionTile()
  ]);

  readonly leadsChart = computed(() => {
    const s = this.leadsState();
    if (s.status !== 'loaded' || !s.data) return null;
    const series = s.data.series;
    const points = linePoints(series);
    const axisLabels = axisLabelIndices(series.length).map((i) => formatAxisDate(series[i].periodStart));
    return { polyline: toPolylinePoints(points), axisLabels };
  });
  readonly leadsChartStatus = computed(() => this.leadsState().status);
  readonly leadsChartError = computed(() => this.leadsState().error);

  readonly serviceSplitView = computed(() => {
    const s = this.serviceSplitState();
    if (s.status !== 'loaded' || !s.data) return null;
    const cif = s.data.items.find((i) => i.code === SERVICE_TYPE_CIF);
    const freight = s.data.items.find((i) => i.code === SERVICE_TYPE_FREIGHT_ONLY);
    const total = s.data.totalCustomers;
    const cifCount = cif?.customerCount ?? 0;
    const freightCount = freight?.customerCount ?? 0;
    return {
      total,
      dash: donutDashArray(cifCount, total),
      cifLabel: cif?.label ?? 'CIF',
      cifCount,
      cifPct: barWidthPct(cifCount, total),
      freightLabel: freight?.label ?? 'Freight-only',
      freightCount,
      freightPct: barWidthPct(freightCount, total)
    };
  });
  readonly serviceSplitStatus = computed(() => this.serviceSplitState().status);
  readonly serviceSplitError = computed(() => this.serviceSplitState().error);

  readonly funnelView = computed<{ rows: FunnelRow[]; conversion: string } | null>(() => {
    const s = this.leadsState();
    if (s.status !== 'loaded' || !s.data) return null;
    const byCode = new Map(s.data.byStatus.map((r) => [r.code, r]));
    const stages = LEAD_FUNNEL_STAGE_CODES.map((code) => byCode.get(code)).filter(
      (r): r is AnalyticsLookupCount => !!r
    );
    const max = maxOf(stages, (r) => r.count);
    const rows = stages.map((r) => ({ label: r.label, n: r.count, pct: `${barWidthPct(r.count, max)}%`, w: barWidthPct(r.count, max) }));
    return { rows, conversion: formatConversionRate(s.data.conversionRate) };
  });
  readonly funnelStatus = computed(() => this.leadsState().status);
  readonly funnelError = computed(() => this.leadsState().error);

  readonly categoryMixView = computed<CategoryMixRow[] | null>(() => {
    const s = this.categoryMixState();
    if (s.status !== 'loaded' || !s.data) return null;
    const items = s.data.items;
    const max = maxOf(items, (i) => i.customerCount);
    return items.map((i) => ({ label: i.label, n: i.customerCount, w: barWidthPct(i.customerCount, max) }));
  });
  readonly categoryMixStatus = computed(() => this.categoryMixState().status);
  readonly categoryMixError = computed(() => this.categoryMixState().error);

  readonly shipmentsByStatusView = computed<{ rows: ShipmentStatusRow[]; overdueLabel: string } | null>(() => {
    const s = this.inventoryState();
    if (s.status !== 'loaded' || !s.data) return null;
    const items = s.data.shipmentsByStatus;
    const max = maxOf(items, (i) => i.count);
    const rows = items.map((i) => {
      const color = this.statusStyle.status(i.code);
      return { label: i.label, n: i.count, w: barWidthPct(i.count, max), bg: color.bg, fg: color.fg };
    });
    return { rows, overdueLabel: `${pluralize(s.data.pastEtaCount, 'shipment')} overdue on ETA` };
  });
  readonly shipmentsByStatusStatus = computed(() => this.inventoryState().status);
  readonly shipmentsByStatusError = computed(() => this.inventoryState().error);

  constructor() {
    this.fetchAll();
  }

  setRange(value: string): void {
    this.dateRange.set(value as DateRangeOption);
    this.fetchAll();
  }

  retryLeads(): void {
    this.fetchLeads();
  }

  retryServiceSplit(): void {
    this.fetchServiceSplit();
  }

  retryCategoryMix(): void {
    this.fetchCategoryMix();
  }

  retryInventory(): void {
    this.fetchInventory();
  }

  retryDispatch(): void {
    this.fetchDispatch();
  }

  private fetchAll(): void {
    this.fetchLeads();
    this.fetchServiceSplit();
    this.fetchCategoryMix();
    this.fetchInventory();
    this.fetchDispatch();
  }

  private get params() {
    return resolveDateRange(this.dateRange());
  }

  private fetchLeads(): void {
    this.leadsState.set(loadingState());
    this.analytics.leads(this.params).subscribe({
      next: (data) => this.leadsState.set({ status: 'loaded', data, error: null }),
      error: (err: unknown) =>
        this.leadsState.set({ status: 'error', data: null, error: extractErrorMessage(err, 'Could not load lead analytics.') })
    });
  }

  private fetchServiceSplit(): void {
    this.serviceSplitState.set(loadingState());
    this.analytics.serviceSplit(this.params).subscribe({
      next: (data) => this.serviceSplitState.set({ status: 'loaded', data, error: null }),
      error: (err: unknown) =>
        this.serviceSplitState.set({
          status: 'error',
          data: null,
          error: extractErrorMessage(err, 'Could not load service-type split.')
        })
    });
  }

  private fetchCategoryMix(): void {
    this.categoryMixState.set(loadingState());
    this.analytics.categoryMix(this.params).subscribe({
      next: (data) => this.categoryMixState.set({ status: 'loaded', data, error: null }),
      error: (err: unknown) =>
        this.categoryMixState.set({
          status: 'error',
          data: null,
          error: extractErrorMessage(err, 'Could not load category mix.')
        })
    });
  }

  private fetchInventory(): void {
    this.inventoryState.set(loadingState());
    this.analytics.inventory(this.params).subscribe({
      next: (data) => this.inventoryState.set({ status: 'loaded', data, error: null }),
      error: (err: unknown) =>
        this.inventoryState.set({
          status: 'error',
          data: null,
          error: extractErrorMessage(err, 'Could not load inventory analytics.')
        })
    });
  }

  private fetchDispatch(): void {
    this.dispatchState.set(loadingState());
    this.analytics.dispatch(this.params).subscribe({
      next: (data) => this.dispatchState.set({ status: 'loaded', data, error: null }),
      error: (err: unknown) =>
        this.dispatchState.set({
          status: 'error',
          data: null,
          error: extractErrorMessage(err, 'Could not load dispatch analytics.')
        })
    });
  }

  // ---- KPI tile derivations ------------------------------------------------

  private newLeadsTile(): KpiTile {
    const label = `New Leads · ${rangeSuffix(this.dateRange())}`;
    const s = this.leadsState();
    if (s.status === 'loading') return { label, status: 'loading', value: '', sub: null, subClass: 'muted' };
    if (s.status === 'error' || !s.data) {
      return { label, status: 'error', value: '—', sub: s.error ?? 'Could not load', subClass: 'danger' };
    }
    const { currentPeriodCount, priorPeriodCount } = s.data;
    const change = percentChange(currentPeriodCount, priorPeriodCount);
    let sub: string;
    let subClass: KpiTile['subClass'];
    if (change === null) {
      sub = currentPeriodCount === 0 ? 'No leads yet' : 'New this period';
      subClass = 'muted';
    } else {
      sub = `${change >= 0 ? '+' : ''}${change}% vs prior period`;
      subClass = change >= 0 ? 'success' : 'danger';
    }
    return { label, status: 'ready', value: String(currentPeriodCount), sub, subClass };
  }

  private activeCustomersTile(): KpiTile {
    const label = 'Active Customers';
    const s = this.serviceSplitState();
    if (s.status === 'loading') return { label, status: 'loading', value: '', sub: null, subClass: 'muted' };
    if (s.status === 'error' || !s.data) {
      return { label, status: 'error', value: '—', sub: s.error ?? 'Could not load', subClass: 'danger' };
    }
    const cif = s.data.items.find((i) => i.code === SERVICE_TYPE_CIF)?.customerCount ?? 0;
    const freight = s.data.items.find((i) => i.code === SERVICE_TYPE_FREIGHT_ONLY)?.customerCount ?? 0;
    return {
      label,
      status: 'ready',
      value: String(s.data.totalCustomers),
      sub: `${cif} CIF · ${freight} freight-only`,
      subClass: 'muted'
    };
  }

  private catalogsSentTile(): KpiTile {
    const label = `Catalogs Sent · ${rangeSuffix(this.dateRange())}`;
    const s = this.dispatchState();
    if (s.status === 'loading') return { label, status: 'loading', value: '', sub: null, subClass: 'muted' };
    if (s.status === 'error' || !s.data) {
      return { label, status: 'error', value: '—', sub: s.error ?? 'Could not load', subClass: 'danger' };
    }
    const catalogCount = s.data.byKind.find((k) => k.kind === 'CatalogDispatched')?.count ?? 0;
    const sub = s.data.totalDispatches === 0 ? 'No dispatches yet' : `${catalogCount} of ${s.data.totalDispatches} catalog sends`;
    return { label, status: 'ready', value: String(s.data.totalDispatches), sub, subClass: 'muted' };
  }

  private onHandValueTile(): KpiTile {
    const label = 'On-Hand Value';
    const s = this.inventoryState();
    if (s.status === 'loading') return { label, status: 'loading', value: '', sub: null, subClass: 'muted' };
    if (s.status === 'error' || !s.data) {
      return { label, status: 'error', value: '—', sub: s.error ?? 'Could not load', subClass: 'danger' };
    }
    const { belowReorderCount } = s.data;
    return {
      label,
      status: 'ready',
      value: formatInrCompact(s.data.onHandValue),
      sub: pluralize(belowReorderCount, 'item') + ' below reorder',
      subClass: belowReorderCount > 0 ? 'warning' : 'muted'
    };
  }

  private inTransitTile(): KpiTile {
    const label = 'In Transit';
    const s = this.inventoryState();
    if (s.status === 'loading') return { label, status: 'loading', value: '', sub: null, subClass: 'muted' };
    if (s.status === 'error' || !s.data) {
      return { label, status: 'error', value: '—', sub: s.error ?? 'Could not load', subClass: 'danger' };
    }
    const { inTransitCount, pastEtaCount } = s.data;
    return {
      label,
      status: 'ready',
      value: String(inTransitCount),
      sub: `${pastEtaCount} past ETA`,
      subClass: pastEtaCount > 0 ? 'danger' : 'muted'
    };
  }

  private leadConversionTile(): KpiTile {
    const label = 'Lead Conversion';
    const s = this.leadsState();
    if (s.status === 'loading') return { label, status: 'loading', value: '', sub: null, subClass: 'muted' };
    if (s.status === 'error' || !s.data) {
      return { label, status: 'error', value: '—', sub: s.error ?? 'Could not load', subClass: 'danger' };
    }
    const { wonCount, totalLeads, conversionRate } = s.data;
    return {
      label,
      status: 'ready',
      value: formatConversionRate(conversionRate),
      sub: `${wonCount} won of ${totalLeads} leads`,
      subClass: 'muted'
    };
  }
}
