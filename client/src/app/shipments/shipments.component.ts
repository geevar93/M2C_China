import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { AuthService } from '../core/services/auth.service';
import { extractErrorMessage } from '../core/services/problem-details.util';
import { StatusStyleService } from '../shared/services/status-style.service';
import { formatDateOnly } from '../shared/utils/date-format.util';
import { formatMoneyOrDash as formatMoney } from '../shared/utils/money.util';
import { ShipmentsService } from './services/shipments.service';
import { ShipmentListItem, ShipmentStatusCount } from './models/shipment.models';
import { ShipmentFormDialogComponent } from './shipment-form-dialog/shipment-form-dialog.component';

interface ShipmentRow {
  id: string;
  reference: string;
  customer: string;
  svcLabel: string;
  svcBg: string;
  svcFg: string;
  stockImpact: string;
  destination: string;
  dispatchedLabel: string;
  statusLabel: string;
  stBg: string;
  stFg: string;
  valueLabel: string;
}

interface ShipTab {
  key: string;
  label: string;
  count: number;
  active: boolean;
}

const PAGE_SIZE = 25;
const ALL = '';

/**
 * Shipments list (ACTION_PLAN E7-12) — ported from Source/Sourcing Ops
 * Platform.dc.html `showShipments` (~line 751). Status tabs are driven by the
 * live `GET /shipments` response's `statusCounts` (§15.3), never a
 * hard-coded status list (DR-6) — that array is live-confirmed to include
 * zero-count statuses and to be computed EXCLUDING the active status filter,
 * so every tab always shows its true total, not a filtered/zero one.
 */
@Component({
  selector: 'app-shipments',
  standalone: true,
  imports: [RouterLink, ShipmentFormDialogComponent],
  templateUrl: './shipments.component.html',
  styleUrl: './shipments.component.scss'
})
export class ShipmentsComponent {
  private readonly shipmentsService = inject(ShipmentsService);
  private readonly styles = inject(StatusStyleService);
  private readonly auth = inject(AuthService);

  readonly canEdit = computed(() => this.auth.hasPermission('Shipments.Edit'));

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  private readonly items = signal<ShipmentListItem[]>([]);
  private readonly statusCounts = signal<ShipmentStatusCount[]>([]);
  readonly totalCount = signal(0);

  readonly search = signal('');
  readonly statusId = signal(ALL);
  readonly page = signal(1);

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)));

  readonly tabs = computed<ShipTab[]>(() => {
    const counts = [...this.statusCounts()].sort((a, b) => a.sortOrder - b.sortOrder);
    const allCount = counts.reduce((sum, c) => sum + c.count, 0);
    const active = this.statusId();
    return [
      { key: ALL, label: 'All shipments', count: allCount, active: active === ALL },
      ...counts.map((c) => ({ key: c.statusId, label: c.label, count: c.count, active: active === c.statusId }))
    ];
  });

  readonly rows = computed<ShipmentRow[]>(() => this.items().map((item) => this.toRow(item)));
  readonly noResults = computed(() => !this.loading() && !this.error() && this.rows().length === 0);

  readonly createOpen = signal(false);

  private readonly search$ = new Subject<string>();

  constructor() {
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

  selectTab(key: string): void {
    this.statusId.set(key);
    this.page.set(1);
    this.fetch();
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

  onShipmentCreated(): void {
    this.createOpen.set(false);
    this.fetch();
  }

  private fetch(): void {
    this.loading.set(true);
    this.error.set(null);
    this.shipmentsService
      .list({
        search: this.search() || undefined,
        statusId: this.statusId() || undefined,
        page: this.page(),
        pageSize: PAGE_SIZE
      })
      .subscribe({
        next: (res) => {
          this.items.set(res.items);
          this.statusCounts.set(res.statusCounts);
          this.totalCount.set(res.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.error.set(extractErrorMessage(err, 'Could not load shipments. Please try again.'));
        }
      });
  }

  private toRow(item: ShipmentListItem): ShipmentRow {
    const svc = this.styles.serviceType(item.serviceType.code);
    const st = this.styles.status(item.status.code);
    return {
      id: item.id,
      reference: item.reference ?? '—',
      customer: item.customer.name,
      svcLabel: svc.label,
      svcBg: svc.bg,
      svcFg: svc.fg,
      // Switches on the immutable service-type CODE, never the label (D-36) —
      // labels are configurable master data and could be renamed.
      stockImpact: item.serviceType.code === 'CIF' ? 'Decrements inventory' : 'No stock held',
      destination: item.destination ?? '—',
      dispatchedLabel: formatDateOnly(item.dispatchDate),
      statusLabel: item.status.label,
      stBg: st.bg,
      stFg: st.fg,
      valueLabel: formatMoney(item.totalValue)
    };
  }
}
