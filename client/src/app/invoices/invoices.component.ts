import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { AuthService } from '../core/services/auth.service';
import { extractErrorMessage } from '../core/services/problem-details.util';
import { MasterDataService } from '../core/services/master-data.service';
import { StatusStyleService } from '../shared/services/status-style.service';
import { CustomersService } from '../customers/services/customers.service';
import { InvoicesService } from './services/invoices.service';
import { InvoiceListItem, InvoiceStatusCount } from './models/invoice.models';
import { formatInr, formatInvoiceDate } from './utils/format.util';

interface InvoiceRow {
  id: string;
  invoiceNumber: string;
  customerName: string;
  dateLabel: string;
  svcLabel: string;
  svcBg: string;
  svcFg: string;
  amountLabel: string;
  statusLabel: string;
  stBg: string;
  stFg: string;
}

interface CustomerOption {
  id: string;
  name: string;
}

interface InvoiceTab {
  key: string;
  label: string;
  count: number;
  active: boolean;
}

const PAGE_SIZE = 25;
const ALL = '';

/**
 * Invoice list (ACTION_PLAN E8-09) — the live, data-wired successor to the
 * E0-05a design-preview pass. Wired against `InvoicesService`/`GET /invoices`
 * (§17.3, live-verified — see `../models/invoice.models`'s doc comment for
 * the traps: `invoiceDate` is a bare date string while `paidAt` is a full
 * instant, `totalAmount` is server-computed, `customer.name` is already the
 * business name).
 *
 * Filtering is entirely server-side — every filter/search/page change issues
 * a fresh `list()` call rather than slicing a client-held array, unlike the
 * mocked E0-05a pass this replaced.
 *
 * Status is rendered as pill tabs driven by `statusCounts` (E7-08's
 * semantics, reused verbatim from the shipments screen): counts are computed
 * across the whole filtered set excluding the status filter itself,
 * zero-count statuses are included, and the array already arrives ordered by
 * `sortOrder` — so the tabs render straight from it with no client-side
 * padding, sorting or filtering. Mirrors `ShipmentsComponent` (E7-12), the
 * closest existing screen with the same list+filters+statusCounts shape.
 *
 * Service-type filter options still come from `MasterDataService` (DR-6 —
 * never a hard-coded list); the customer filter is populated from the real
 * `CustomersService`, replacing E0-05a's mocked customer directory.
 */
@Component({
  selector: 'app-invoices',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './invoices.component.html',
  styleUrl: './invoices.component.scss'
})
export class InvoicesComponent {
  private readonly invoicesService = inject(InvoicesService);
  private readonly masterDataService = inject(MasterDataService);
  private readonly customersService = inject(CustomersService);
  private readonly styles = inject(StatusStyleService);
  private readonly auth = inject(AuthService);

  readonly canEdit = computed(() => this.auth.hasPermission('Invoicing.Edit'));

  readonly serviceTypeOptions = toSignal(this.masterDataService.serviceTypeOptions(), { initialValue: [] });
  readonly customerOptions = signal<CustomerOption[]>([]);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  private readonly items = signal<InvoiceListItem[]>([]);
  private readonly statusCounts = signal<InvoiceStatusCount[]>([]);
  readonly totalCount = signal(0);

  readonly search = signal('');
  readonly customerId = signal(ALL);
  readonly statusId = signal(ALL);
  readonly serviceTypeId = signal(ALL);
  readonly dateFrom = signal('');
  readonly dateTo = signal('');
  readonly page = signal(1);

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)));

  /** True once any filter/search deviates from the default — distinguishes "no invoices yet" from "no matches for these filters" in the empty state. */
  readonly hasActiveFilters = computed(
    () => !!(this.search() || this.customerId() || this.statusId() || this.serviceTypeId() || this.dateFrom() || this.dateTo())
  );

  readonly tabs = computed<InvoiceTab[]>(() => {
    const counts = this.statusCounts();
    const allCount = counts.reduce((sum, c) => sum + c.count, 0);
    const active = this.statusId();
    return [
      { key: ALL, label: 'All invoices', count: allCount, active: active === ALL },
      ...counts.map((c) => ({ key: c.statusId, label: c.label, count: c.count, active: active === c.statusId }))
    ];
  });

  readonly rows = computed<InvoiceRow[]>(() => this.items().map((item) => this.toRow(item)));
  readonly noResults = computed(() => !this.loading() && !this.error() && this.rows().length === 0);

  private readonly search$ = new Subject<string>();

  constructor() {
    this.search$.pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed()).subscribe((value) => {
      this.search.set(value);
      this.page.set(1);
      this.fetch();
    });

    this.loadCustomers();
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

  setCustomer(value: string): void {
    this.customerId.set(value);
    this.page.set(1);
    this.fetch();
  }

  setServiceType(value: string): void {
    this.serviceTypeId.set(value);
    this.page.set(1);
    this.fetch();
  }

  setDateFrom(value: string): void {
    this.dateFrom.set(value);
    this.page.set(1);
    this.fetch();
  }

  setDateTo(value: string): void {
    this.dateTo.set(value);
    this.page.set(1);
    this.fetch();
  }

  clearFilters(): void {
    this.search.set('');
    this.customerId.set(ALL);
    this.statusId.set(ALL);
    this.serviceTypeId.set(ALL);
    this.dateFrom.set('');
    this.dateTo.set('');
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
    this.invoicesService
      .list({
        search: this.search() || undefined,
        customerId: this.customerId() || undefined,
        statusId: this.statusId() || undefined,
        serviceTypeId: this.serviceTypeId() || undefined,
        fromDate: this.dateFrom() || undefined,
        toDate: this.dateTo() || undefined,
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
          this.error.set(extractErrorMessage(err, 'Could not load invoices. Please try again.'));
        }
      });
  }

  /** Populates the Customer filter dropdown from the real directory (replaces E0-05a's mocked customer list). A failure here only degrades the filter, so it does not block the invoice list itself. */
  private loadCustomers(): void {
    this.customersService.list({ page: 1, pageSize: 200 }).subscribe({
      next: (res) => {
        this.customerOptions.set(
          res.items.map((c) => ({ id: c.id, name: c.businessName })).sort((a, b) => a.name.localeCompare(b.name))
        );
      },
      error: () => {
        /* filter dropdown just stays empty; not fatal to the screen */
      }
    });
  }

  private toRow(item: InvoiceListItem): InvoiceRow {
    const svc = this.styles.serviceType(item.serviceType.code);
    const st = this.styles.status(item.status.code);
    return {
      id: item.id,
      invoiceNumber: item.invoiceNumber,
      customerName: item.customer.name,
      dateLabel: formatInvoiceDate(item.invoiceDate),
      svcLabel: item.serviceType.label,
      svcBg: svc.bg,
      svcFg: svc.fg,
      amountLabel: formatInr(item.totalAmount),
      statusLabel: item.status.label,
      stBg: st.bg,
      stFg: st.fg
    };
  }
}
