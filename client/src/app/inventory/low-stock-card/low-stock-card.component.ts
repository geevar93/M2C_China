import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../core/services/problem-details.util';
import { RefreshService } from '../../core/services/refresh.service';
import { InventoryService } from '../services/inventory.service';
import { InventoryItem } from '../models/inventory.models';
import { formatQty } from '../utils/format.util';
import { stockLevelColor } from '../utils/stock-level.util';

const MAX_ROWS = 6;

/**
 * "Below Reorder Threshold" card for the main dashboard. Uses the inventory list
 * endpoint's `stockLevel=LOW` filter (below reorder OR negative), so it shows exactly
 * what the Inventory screen's "Show only low stock" filter shows. The host decides
 * whether to render it (it needs `Inventory.View`).
 */
@Component({
  selector: 'app-low-stock-card',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="card low-stock-card">
      <div class="ls-header">
        <div class="ls-title">Below Reorder Threshold</div>
        <a class="ls-link" routerLink="/inventory" [queryParams]="{ level: 'LOW' }">View in Inventory →</a>
      </div>

      <div class="ls-body">
      <a class="ls-stat" routerLink="/inventory" [queryParams]="{ level: 'LOW' }" [class.ls-stat--alert]="lowCount() + negativeCount() > 0">
        <span class="stat-tile__label">Items below reorder</span>
        <span class="ls-stat__value">{{ status() === 'loaded' ? lowCount() : '—' }}</span>
        <span class="ls-stat__sub" [class.ls-stat__sub--danger]="negativeCount() > 0">
          {{ negativeCount() }} with negative stock
        </span>
      </a>

      <div class="ls-detail">
      @if (status() === 'loading') {
        <div class="state-panel ls-state"><span class="spinner"></span><span>Loading…</span></div>
      } @else if (status() === 'error') {
        <div class="state-panel ls-state">
          <span>{{ error() }}</span>
          <button type="button" class="btn btn-secondary btn-sm" (click)="load()">Retry</button>
        </div>
      } @else if (items().length === 0) {
        <div class="state-panel ls-state">All items are at or above their reorder threshold.</div>
      } @else {
        <ul class="ls-list">
          @for (i of items(); track i.id) {
            <li class="ls-row">
              <span class="ls-thumb">
                @if (i.thumbnailDataUrl) {
                  <img [src]="i.thumbnailDataUrl" alt="" loading="lazy" />
                } @else {
                  <span aria-hidden="true">📦</span>
                }
              </span>
              <span class="ls-name">
                <span class="ls-item">{{ i.name }}</span>
                <span class="muted ls-sub">Reorder at {{ qty(i.reorderThreshold) }} {{ i.unit }}</span>
              </span>
              <span class="chip" [style.background]="color(i).bg" [style.color]="color(i).fg">
                {{ qty(i.onHandQty) }} {{ i.unit }}
              </span>
            </li>
          }
        </ul>
        @if (total() > items().length) {
          <div class="ls-footer muted">+ {{ total() - items().length }} more</div>
        }
      }
      </div>
      </div>
    </div>
  `,
  styles: [
    `
      .ls-header { display: flex; justify-content: space-between; align-items: baseline; margin-bottom: var(--space-6); }
      .ls-title { font-size: 14px; font-weight: 600; display: flex; align-items: center; gap: var(--space-3); }
      .ls-body { display: grid; grid-template-columns: minmax(180px, 220px) 1fr; gap: var(--space-8); align-items: start; }
      .ls-detail { min-width: 0; }
      .ls-stat {
        display: flex; flex-direction: column; gap: var(--space-2); padding: var(--space-7);
        border: 1px solid var(--color-border); border-radius: var(--radius-md); color: var(--color-text); text-decoration: none;
      }
      .ls-stat:hover { text-decoration: none; border-color: var(--color-border-strong); }
      .ls-stat__value { font-size: 30px; font-weight: 700; line-height: 1.1; }
      .ls-stat--alert { background: var(--color-warning-bg); border-color: transparent; }
      .ls-stat--alert .ls-stat__value { color: var(--color-warning); }
      .ls-stat__sub { font-size: 12px; color: var(--color-text-muted); }
      .ls-stat__sub--danger { color: var(--color-danger); font-weight: 600; }
      @media (max-width: 640px) { .ls-body { grid-template-columns: 1fr; } }
      .ls-link { font-size: 13px; }
      .ls-state { padding: 28px 0; }
      .ls-list { list-style: none; margin: 0; padding: 0; }
      .ls-row { display: flex; align-items: center; gap: var(--space-5); padding: var(--space-4) 0; border-bottom: 1px solid var(--color-track); }
      .ls-row:last-child { border-bottom: none; }
      .ls-thumb {
        flex: 0 0 auto; width: 40px; height: 40px; border-radius: var(--radius-sm); overflow: hidden;
        background: var(--color-page-bg); border: 1px solid var(--color-border);
        display: inline-flex; align-items: center; justify-content: center; font-size: 18px;
      }
      .ls-thumb img { width: 100%; height: 100%; object-fit: cover; }
      .ls-name { flex: 1 1 auto; min-width: 0; display: flex; flex-direction: column; }
      .ls-item { font-size: 13px; font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
      .ls-sub { font-size: 12px; }
      .ls-footer { font-size: 12px; padding-top: var(--space-4); }
    `
  ]
})
export class LowStockCardComponent implements OnInit {
  private readonly inventoryService = inject(InventoryService);

  readonly status = signal<'loading' | 'loaded' | 'error'>('loading');
  readonly error = signal<string | null>(null);
  readonly items = signal<InventoryItem[]>([]);
  readonly total = signal(0);
  readonly lowCount = signal(0);
  readonly negativeCount = signal(0);

  constructor() {
    inject(RefreshService).onRefresh(() => this.load());
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.status.set('loading');
    this.error.set(null);
    this.inventoryService.list({ stockLevel: 'LOW', page: 1, pageSize: MAX_ROWS }).subscribe({
      next: (res) => {
        // Worst first: negative stock, then furthest below its threshold.
        const sorted = [...res.items].sort((a, b) => a.onHandQty - a.reorderThreshold - (b.onHandQty - b.reorderThreshold));
        this.items.set(sorted);
        this.total.set(res.totalCount);
        this.lowCount.set(res.summary.lowStockCount);
        this.negativeCount.set(res.summary.negativeStockCount);
        this.status.set('loaded');
      },
      error: (err: unknown) => {
        this.error.set(extractErrorMessage(err, 'Could not load low-stock items.'));
        this.status.set('error');
      }
    });
  }

  qty(value: number): string {
    return formatQty(value);
  }

  color(item: InventoryItem) {
    return stockLevelColor(item.stockLevel);
  }
}
