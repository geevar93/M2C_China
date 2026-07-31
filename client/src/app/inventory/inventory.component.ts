import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { AuthService } from '../core/services/auth.service';
import { MasterDataService } from '../core/services/master-data.service';
import { extractErrorMessage } from '../core/services/problem-details.util';
import { StatusStyleService } from '../shared/services/status-style.service';
import { formatInr, formatInrCompact, formatQty } from '../shared/utils/currency.util';
import { InventoryService } from './services/inventory.service';
import { InventoryItem, InventoryListResponse, InventorySummary } from './models/inventory.models';

interface InventoryRow {
  id: string;
  name: string;
  subLabel: string;
  categoryName: string;
  vendorName: string;
  qtyLabel: string;
  qtyColor: string;
  reorderLabel: string;
  levelLabel: string;
  levelBg: string;
  levelFg: string;
  barWidth: number;
  barColor: string;
  valueLabel: string;
  negative: boolean;
}

interface StatTile {
  label: string;
  value: string;
  emphasis: 'normal' | 'warn' | 'danger';
}

const PAGE_SIZE = 25;

/**
 * Inventory list (ACTION_PLAN E7-11) — ported from Source/Sourcing Ops
 * Platform.dc.html `showInventory` (~line 670) against the `/inventory`
 * contract, which was diffed against a live response before this was wired (D-22).
 *
 * Two things the API deliberately owns, so this component must not recompute them:
 *  - `stockLevel` is classified server-side (E7-04). The raw quantities come
 *    alongside it purely so the visual bar's ratio maths can stay here, which is
 *    presentation. Re-deriving the *level* would put a second classifier in the
 *    app, free to disagree with the one the filter uses.
 *  - `summary` is aggregated over the whole filtered set, not the page (D-39).
 *    The four stat tiles read it directly; counting `rows()` would silently show
 *    per-page numbers.
 *
 * `stockValue: null` means **not costed**, not zero — rendered as "—" so it can
 * never be misread as a real ₹0 valuation.
 */
@Component({
  selector: 'app-inventory',
  standalone: true,
  imports: [],
  templateUrl: './inventory.component.html',
  styleUrl: './inventory.component.scss'
})
export class InventoryComponent {
  private readonly inventoryService = inject(InventoryService);
  private readonly masterDataService = inject(MasterDataService);
  private readonly styles = inject(StatusStyleService);
  private readonly auth = inject(AuthService);

  readonly categoryOptions = toSignal(this.masterDataService.categoryOptions(), { initialValue: [] });

  readonly canEdit = computed(() => this.auth.hasPermission('Inventory.Edit'));

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  private readonly items = signal<InventoryItem[]>([]);
  readonly totalCount = signal(0);
  private readonly summary = signal<InventorySummary | null>(null);

  readonly search = signal('');
  readonly categoryId = signal('');
  readonly stockLevel = signal<'' | 'low' | 'healthy'>('');
  readonly page = signal(1);

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)));

  readonly rows = computed<InventoryRow[]>(() => this.items().map((item) => this.toRow(item)));
  readonly noResults = computed(() => !this.loading() && !this.error() && this.rows().length === 0);

  /** The four tiles, in the approved screen's order and wording. */
  readonly tiles = computed<StatTile[]>(() => {
    const s = this.summary();
    if (!s) return [];
    return [
      { label: 'On-Hand Value', value: formatInrCompact(s.onHandValue), emphasis: 'normal' },
      { label: 'Distinct Items', value: String(s.itemCount), emphasis: 'normal' },
      { label: 'Below Reorder', value: String(s.lowStockCount), emphasis: s.lowStockCount > 0 ? 'warn' : 'normal' },
      {
        label: 'Negative Stock',
        value: String(s.negativeStockCount),
        emphasis: s.negativeStockCount > 0 ? 'danger' : 'normal'
      }
    ];
  });

  /**
   * The prototype's amber banner. Its copy is hard-coded there ("3 items below
   * reorder threshold. Ballpoint Pen Bulk Pack is oversold by 40 units") because it
   * is a static mock; here it is driven by the live summary and shown only when
   * there is something to say. The oversold sentence needs a specific item name,
   * which `summary` does not carry — so it degrades to a count rather than
   * inventing one or firing a second request to find it.
   */
  readonly alert = computed(() => {
    const s = this.summary();
    if (!s || (s.lowStockCount === 0 && s.negativeStockCount === 0)) return null;

    const parts: string[] = [];
    if (s.lowStockCount > 0) {
      parts.push(`${s.lowStockCount} item${s.lowStockCount === 1 ? '' : 's'} below reorder threshold.`);
    }
    if (s.negativeStockCount > 0) {
      parts.push(
        `${s.negativeStockCount} item${s.negativeStockCount === 1 ? ' is' : 's are'} oversold — an outbound shipment took on-hand negative.`
      );
    }
    return parts.join(' ');
  });

  /** True once the "Show only low stock" shortcut is applied, so the banner stops offering it. */
  readonly lowStockFilterActive = computed(() => this.stockLevel() === 'low');

  private readonly search$ = new Subject<string>();

  constructor() {
    this.masterDataService.ensureLoaded().subscribe({ error: () => {} });

    this.search$.pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed()).subscribe((value) => {
      this.search.set(value);
      this.page.set(1);
      this.fetch();
    });

    this.fetch();
  }

  onSearchInput(value: string): void {
    this.search$.next(value);
  }

  setCategory(value: string): void {
    this.categoryId.set(value);
    this.page.set(1);
    this.fetch();
  }

  setStockLevel(value: string): void {
    this.stockLevel.set(value === 'low' || value === 'healthy' ? value : '');
    this.page.set(1);
    this.fetch();
  }

  /** The banner's "Show only low stock" shortcut — the prototype's `filterLowStock`. */
  filterLowStock(): void {
    this.setStockLevel('low');
  }

  prevPage(): void {
    if (this.page() <= 1) return;
    this.page.update((p) => p - 1);
    this.fetch();
  }

  nextPage(): void {
    if (this.page() >= this.totalPages()) return;
    this.page.update((p) => p + 1);
    this.fetch();
  }

  retry(): void {
    this.fetch();
  }

  private fetch(): void {
    this.loading.set(true);
    this.error.set(null);
    this.inventoryService
      .list({
        search: this.search() || undefined,
        categoryId: this.categoryId() || undefined,
        stockLevel: this.stockLevel() || undefined,
        page: this.page(),
        pageSize: PAGE_SIZE
      })
      .subscribe({
        next: (res: InventoryListResponse) => {
          this.items.set(res.items);
          this.totalCount.set(res.totalCount);
          this.summary.set(res.summary);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.error.set(extractErrorMessage(err, 'Could not load inventory. Please try again.'));
        }
      });
  }

  private toRow(item: InventoryItem): InventoryRow {
    const level = this.styles.stockLevel(item.stockLevel);

    return {
      id: item.id,
      name: item.name,
      subLabel: [item.sku, `per ${item.unit}`].filter(Boolean).join(' · '),
      categoryName: item.category.name,
      vendorName: item.vendor?.name ?? '—',
      qtyLabel: formatQty(item.onHandQty),
      // HEALTHY quantities keep the default ink — only LOW/NEGATIVE are tinted,
      // matching the prototype's `qtyColor` (which uses the plain body colour).
      qtyColor: item.stockLevel === 'HEALTHY' ? '' : level.fg,
      reorderLabel: formatQty(item.reorderThreshold),
      levelLabel: item.stockLevel,
      levelBg: level.bg,
      levelFg: level.fg,
      barWidth: this.barWidth(item),
      barColor: level.fg,
      valueLabel: item.stockValue === null ? '—' : formatInr(item.stockValue),
      negative: item.stockLevel === 'NEGATIVE'
    };
  }

  /**
   * The prototype's bar ratio: `qty / (reorder * 2.5)` as a percentage, clamped to
   * 0–100, with negative stock pinned full-width in red so an oversold row reads as
   * a hard problem rather than an empty bar.
   *
   * A zero threshold would divide by zero. The prototype never hits that because its
   * seed data has none, but real master data can — treated as "no threshold to
   * measure against", so any non-negative quantity shows a full bar.
   */
  private barWidth(item: InventoryItem): number {
    if (item.stockLevel === 'NEGATIVE') return 100;
    if (item.reorderThreshold <= 0) return 100;
    const ratio = Math.round((item.onHandQty / (item.reorderThreshold * 2.5)) * 100);
    return Math.max(0, Math.min(100, ratio));
  }
}
