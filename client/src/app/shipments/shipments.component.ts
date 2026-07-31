import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { AuthService } from '../core/services/auth.service';
import { extractErrorMessage } from '../core/services/problem-details.util';
import { StatusStyleService } from '../shared/services/status-style.service';
import { SERVICE_TYPE_CIF } from '../shared/constants/service-type-codes';
import { formatDateOnly } from '../shared/utils/date-format.util';
import { formatInr } from '../shared/utils/currency.util';
import { ShipmentsService } from './services/shipments.service';
import { ShipmentListItem, ShipmentListResponse, ShipmentStatusCount } from './models/shipment.models';

interface ShipmentRow {
  id: string;
  reference: string;
  customerName: string;
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

interface StatusTab {
  /** Empty string is the "All shipments" tab — it clears the filter rather than setting one. */
  statusId: string;
  label: string;
  count: number;
  active: boolean;
}

const PAGE_SIZE = 25;

/**
 * Shipments list (ACTION_PLAN E7-12) — ported from Source/Sourcing Ops
 * Platform.dc.html `showShipments` (~line 750) against the `/shipments` contract,
 * diffed against a live response first (D-22).
 *
 * The status tabs are built from the response's `statusCounts`, never from a
 * hard-coded status list (DR-6) and never recounted from `items`. Two reasons:
 * the seeded statuses are configurable master data a Super Admin may add to, and
 * the server computes those counts across the whole filtered set **excluding the
 * status filter itself** (E7-08) — recount them from the current page and every
 * tab reads either its own total or zero the moment one is selected.
 */
@Component({
  selector: 'app-shipments',
  standalone: true,
  imports: [RouterLink],
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
  readonly statusId = signal('');
  readonly page = signal(1);

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)));

  readonly rows = computed<ShipmentRow[]>(() => this.items().map((item) => this.toRow(item)));
  readonly noResults = computed(() => !this.loading() && !this.error() && this.rows().length === 0);

  /**
   * "All shipments" plus one tab per status, in the lookup's own `sortOrder`.
   *
   * The All count sums `statusCounts` rather than reading `totalCount`: because the
   * counts exclude the status filter, that sum is the true all-statuses total and
   * stays stable as tabs are clicked, whereas `totalCount` is the *filtered* count
   * and would collapse to the selected tab's number.
   */
  readonly tabs = computed<StatusTab[]>(() => {
    const counts = this.statusCounts();
    if (counts.length === 0) return [];

    const selected = this.statusId();
    const all = counts.reduce((sum, c) => sum + c.count, 0);

    return [
      { statusId: '', label: 'All shipments', count: all, active: selected === '' },
      ...[...counts]
        .sort((a, b) => a.sortOrder - b.sortOrder)
        .map((c) => ({ statusId: c.statusId, label: c.label, count: c.count, active: selected === c.statusId }))
    ];
  });

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

  selectTab(statusId: string): void {
    if (this.statusId() === statusId) return;
    this.statusId.set(statusId);
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
        next: (res: ShipmentListResponse) => {
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
      // `reference` is nullable on the wire (the column predates E7-05 populating
      // it), so a row with none still has to render something clickable.
      reference: item.reference ?? '(no reference)',
      customerName: item.customer.name,
      svcLabel: svc.label,
      svcBg: svc.bg,
      svcFg: svc.fg,
      // Switches on the immutable code (D-12/D-36), not the label. Freight-only
      // shipments hold no stock at all (FSD A8) — they cannot even carry lines.
      stockImpact: item.serviceType.code === SERVICE_TYPE_CIF ? 'Decrements inventory' : 'No stock held',
      destination: item.destination ?? '—',
      dispatchedLabel: formatDateOnly(item.dispatchDate),
      statusLabel: item.status.label,
      stBg: st.bg,
      stFg: st.fg,
      valueLabel: item.totalValue === null ? '—' : formatInr(item.totalValue)
    };
  }
}
