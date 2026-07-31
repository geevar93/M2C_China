import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { AuthService } from '../core/services/auth.service';
import { MasterDataService } from '../core/services/master-data.service';
import { extractErrorMessage } from '../core/services/problem-details.util';
import { InboundDialogComponent, InboundTargetItem } from './inbound-dialog/inbound-dialog.component';
import { ItemFormDialogComponent } from './item-form-dialog/item-form-dialog.component';
import { InventoryService } from './services/inventory.service';
import { InventoryItem, InventorySummary, RecordInboundResult, StockLevel } from './models/inventory.models';
import { formatQty, formatStockValue } from './utils/format.util';
import { formatInrCompact } from '../shared/utils/money.util';
import { stockBarWidth, stockLevelColor, stockLevelTextColor } from './utils/stock-level.util';

interface InventoryRow {
  id: string;
  name: string;
  skuUnitLabel: string;
  categoryName: string;
  vendorName: string;
  qtyLabel: string;
  qtyColor: string;
  reorderLabel: string;
  level: StockLevel;
  chipBg: string;
  chipFg: string;
  barColor: string;
  barWidth: number;
  valueLabel: string;
  rowBg: string;
  inboundTarget: InboundTargetItem;
}

const PAGE_SIZE = 25;
const ALL = '';
const EMPTY_SUMMARY: InventorySummary = { onHandValue: 0, itemCount: 0, lowStockCount: 0, negativeStockCount: 0 };

/**
 * Inventory list (ACTION_PLAN E7-11) — ported from the prototype's
 * `showInventory` block (`Source/Sourcing Ops Platform.dc.html` ~line
 * 670-748) and its `invRows()`/`invStats` logic (~line 1401 / ~1773).
 * Search + category/stock-level filters call `GET /inventory` (§15.3);
 * the four stat tiles and the low-stock banner read the response's
 * `summary` block, which is computed over the whole filtered set (D-39),
 * not just the current page.
 */
@Component({
  selector: 'app-inventory',
  standalone: true,
  imports: [RouterLink, ItemFormDialogComponent, InboundDialogComponent],
  templateUrl: './inventory.component.html',
  styleUrl: './inventory.component.scss'
})
export class InventoryComponent {
  private readonly inventoryService = inject(InventoryService);
  private readonly masterDataService = inject(MasterDataService);
  private readonly auth = inject(AuthService);

  readonly categoryOptions = toSignal(this.masterDataService.categoryOptions(), { initialValue: [] });

  readonly canEdit = computed(() => this.auth.hasPermission('Inventory.Edit'));

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  private readonly items = signal<InventoryItem[]>([]);
  readonly totalCount = signal(0);
  readonly summary = signal<InventorySummary>(EMPTY_SUMMARY);

  readonly search = signal('');
  readonly categoryId = signal(ALL);
  /** `''` | `'LOW'` | `'HEALTHY'` — the API's `stockLevel=LOW` already means "below reorder OR negative" (E7-03), so there is deliberately no separate NEGATIVE filter option; negative items surface via their row badge instead. */
  readonly stockLevel = signal<'' | StockLevel>(ALL);
  readonly page = signal(1);

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)));

  readonly rows = computed<InventoryRow[]>(() => this.items().map((item) => this.toRow(item)));
  readonly noResults = computed(() => !this.loading() && !this.error() && this.rows().length === 0);

  readonly showLowStockBanner = computed(() => this.summary().lowStockCount > 0 || this.summary().negativeStockCount > 0);
  readonly bannerText = computed(() => {
    const s = this.summary();
    const parts: string[] = [];
    if (s.lowStockCount > 0) parts.push(`${s.lowStockCount} item${s.lowStockCount === 1 ? '' : 's'} below reorder threshold`);
    if (s.negativeStockCount > 0) parts.push(`${s.negativeStockCount} item${s.negativeStockCount === 1 ? '' : 's'} with negative stock`);
    return parts.join(' · ') + '.';
  });

  // Abbreviated (`₹41.2 L`), not full precision: the approved screen's tile reads
  // that way and E7-11's criterion is a 1:1 port. The per-row Stock Value column
  // below still uses `formatStockValue`, where exactness is what's wanted.
  readonly onHandValueLabel = computed(() => formatInrCompact(this.summary().onHandValue));
  readonly itemCountLabel = computed(() => this.summary().itemCount.toLocaleString('en-IN'));
  readonly belowReorderLabel = computed(() => this.summary().lowStockCount.toLocaleString('en-IN'));
  readonly negativeStockLabel = computed(() => this.summary().negativeStockCount.toLocaleString('en-IN'));

  readonly createOpen = signal(false);
  readonly editItem = signal<InventoryItem | null>(null);

  readonly inboundOpen = signal(false);
  readonly inboundPresetItem = signal<InboundTargetItem | null>(null);
  readonly inboundCandidates = computed<InboundTargetItem[]>(() =>
    this.items().map((i) => ({ id: i.id, name: i.name, sku: i.sku, unit: i.unit }))
  );

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
    this.stockLevel.set(value as '' | StockLevel);
    this.page.set(1);
    this.fetch();
  }

  filterLowStock(): void {
    this.setStockLevel('LOW');
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

  openCreate(): void {
    this.createOpen.set(true);
  }

  cancelCreate(): void {
    this.createOpen.set(false);
  }

  onItemCreated(_item: InventoryItem): void {
    this.createOpen.set(false);
    this.fetch();
  }

  openEdit(row: InventoryRow): void {
    const item = this.items().find((i) => i.id === row.id);
    if (item) this.editItem.set(item);
  }

  cancelEdit(): void {
    this.editItem.set(null);
  }

  onItemSaved(_item: InventoryItem): void {
    this.editItem.set(null);
    this.fetch();
  }

  openInboundForRow(row: InventoryRow): void {
    this.inboundPresetItem.set(row.inboundTarget);
    this.inboundOpen.set(true);
  }

  openInboundFromHeader(): void {
    this.inboundPresetItem.set(null);
    this.inboundOpen.set(true);
  }

  cancelInbound(): void {
    this.inboundOpen.set(false);
    this.inboundPresetItem.set(null);
  }

  /** The response carries both the new entry and the re-computed item (§15.3) — patch the one row from it rather than refetching the whole list. */
  onInboundRecorded(result: RecordInboundResult): void {
    this.inboundOpen.set(false);
    this.inboundPresetItem.set(null);
    this.items.update((items) => items.map((i) => (i.id === result.item.id ? result.item : i)));
  }

  private fetch(): void {
    this.loading.set(true);
    this.error.set(null);
    this.inventoryService
      .list({
        search: this.search() || undefined,
        page: this.page(),
        pageSize: PAGE_SIZE,
        categoryId: this.categoryId() || undefined,
        stockLevel: (this.stockLevel() || undefined) as StockLevel | undefined
      })
      .subscribe({
        next: (res) => {
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
    const color = stockLevelColor(item.stockLevel);
    return {
      id: item.id,
      name: item.name,
      skuUnitLabel: `${item.sku ?? '—'} · per ${item.unit}`,
      categoryName: item.category.name,
      vendorName: item.vendor?.name ?? '—',
      qtyLabel: formatQty(item.onHandQty),
      qtyColor: stockLevelTextColor(item.stockLevel),
      reorderLabel: formatQty(item.reorderThreshold),
      level: item.stockLevel,
      chipBg: color.bg,
      chipFg: color.fg,
      barColor: color.fg,
      barWidth: stockBarWidth(item.onHandQty, item.reorderThreshold),
      valueLabel: formatStockValue(item.stockValue),
      // Negative rows get the prototype's tinted row background; no dedicated
      // token exists for its literal `#fff8f8` shade, so this reuses the
      // closest existing semantic token (`--color-danger-bg`) rather than
      // hand-inventing a new hex (flagged to the coordinator).
      rowBg: item.stockLevel === 'NEGATIVE' ? 'var(--color-danger-bg-subtle)' : 'var(--color-surface)',
      inboundTarget: { id: item.id, name: item.name, sku: item.sku, unit: item.unit }
    };
  }
}
