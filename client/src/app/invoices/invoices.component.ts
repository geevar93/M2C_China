import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { MasterDataService } from '../core/services/master-data.service';
import { StatusStyleService } from '../shared/services/status-style.service';
import { MOCK_INVOICE_CUSTOMERS, MOCK_INVOICES, MockInvoice, totalOf } from './mock-invoices';
import { formatInr, formatInvoiceDate } from './utils/format.util';

interface InvoiceRow {
  id: string;
  invoiceNumber: string;
  customerBusinessName: string;
  dateLabel: string;
  svcLabel: string;
  svcBg: string;
  svcFg: string;
  amountLabel: string;
  statusLabel: string;
  stBg: string;
  stFg: string;
}

const ALL = '';

/**
 * Invoice list (ACTION_PLAN E0-05a / E8-09 design pass) — DESIGN PREVIEW ON
 * MOCKED DATA. There is no invoicing backend (no `InvoicesController`, no
 * entity, no migration; epic E8 has not started) — see `./mock-invoices.ts`
 * and ACTION_PLAN.md §12.3. The `.banner-warning` below is load-bearing: this
 * screen must never be mistaken for a working module during a demo.
 *
 * Invoice statuses and service types are the one genuinely real thing here —
 * both are configurable master data loaded from `MasterDataService`
 * (`invoiceStatuses` / `serviceTypes`), never a hard-coded list (DR-6).
 * Filtering/sorting run entirely client-side over the mocked array so the
 * screen actually behaves rather than being a static picture.
 */
@Component({
  selector: 'app-invoices',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './invoices.component.html',
  styleUrl: './invoices.component.scss'
})
export class InvoicesComponent {
  private readonly masterDataService = inject(MasterDataService);
  private readonly styles = inject(StatusStyleService);

  private readonly masterData = toSignal(this.masterDataService.masterData$, {
    initialValue: { status: 'idle' as const, data: null, error: null }
  });

  readonly invoiceStatusOptions = toSignal(this.masterDataService.invoiceStatusOptions(), { initialValue: [] });
  readonly serviceTypeOptions = toSignal(this.masterDataService.serviceTypeOptions(), { initialValue: [] });

  readonly masterDataLoading = computed(() => {
    const status = this.masterData().status;
    return status === 'loading' || status === 'idle';
  });
  readonly masterDataError = computed(() => this.masterData().error);
  readonly hasMasterData = computed(() => !!this.masterData().data);

  readonly customerDirectory = MOCK_INVOICE_CUSTOMERS.slice().sort((a, b) => a.businessName.localeCompare(b.businessName));

  readonly search = signal('');
  readonly customerId = signal(ALL);
  readonly statusId = signal(ALL);
  readonly serviceTypeId = signal(ALL);
  readonly dateFrom = signal('');
  readonly dateTo = signal('');

  readonly filteredInvoices = computed<MockInvoice[]>(() => {
    const md = this.masterData().data;
    const statusCode = md?.invoiceStatuses.find((s) => s.id === this.statusId())?.code;
    const svcCode = md?.serviceTypes.find((s) => s.id === this.serviceTypeId())?.code;
    const search = this.search().trim().toLowerCase();
    const customerId = this.customerId();
    const from = this.dateFrom();
    const to = this.dateTo();

    return MOCK_INVOICES.filter((inv) => {
      if (statusCode && inv.statusCode !== statusCode) return false;
      if (svcCode && inv.serviceTypeCode !== svcCode) return false;
      if (customerId && inv.customerId !== customerId) return false;
      if (from && inv.issueDate < from) return false;
      if (to && inv.issueDate > to) return false;
      if (search) {
        const customer = MOCK_INVOICE_CUSTOMERS.find((c) => c.id === inv.customerId);
        const haystack = `${inv.invoiceNumber} ${customer?.businessName ?? ''} ${customer?.contactName ?? ''}`.toLowerCase();
        if (!haystack.includes(search)) return false;
      }
      return true;
    }).sort((a, b) => b.issueDate.localeCompare(a.issueDate));
  });

  readonly rows = computed<InvoiceRow[]>(() => this.filteredInvoices().map((inv) => this.toRow(inv)));

  readonly noResults = computed(() => !this.masterDataLoading() && this.rows().length === 0);

  /** Sum of ISSUED + PAID invoices in the current filtered view — the amount that has genuinely been billed. */
  readonly totalIssuedLabel = computed(() =>
    formatInr(
      this.filteredInvoices()
        .filter((i) => i.statusCode === 'ISSUED' || i.statusCode === 'PAID')
        .reduce((sum, i) => sum + totalOf(i), 0)
    )
  );

  /** Sum of ISSUED-but-not-PAID invoices in the current filtered view — the amount still owed. */
  readonly totalOutstandingLabel = computed(() =>
    formatInr(
      this.filteredInvoices()
        .filter((i) => i.statusCode === 'ISSUED')
        .reduce((sum, i) => sum + totalOf(i), 0)
    )
  );

  constructor() {
    this.masterDataService.ensureLoaded().subscribe({ error: () => {} });
  }

  setStatus(value: string): void {
    this.statusId.set(value);
  }

  setServiceType(value: string): void {
    this.serviceTypeId.set(value);
  }

  setCustomer(value: string): void {
    this.customerId.set(value);
  }

  clearFilters(): void {
    this.search.set('');
    this.customerId.set(ALL);
    this.statusId.set(ALL);
    this.serviceTypeId.set(ALL);
    this.dateFrom.set('');
    this.dateTo.set('');
  }

  retryMasterData(): void {
    this.masterDataService.reload().subscribe({ error: () => {} });
  }

  private toRow(inv: MockInvoice): InvoiceRow {
    const svc = this.styles.serviceType(inv.serviceTypeCode);
    const st = this.styles.status(inv.statusCode);
    const md = this.masterData().data;
    const statusLabel = md?.invoiceStatuses.find((s) => s.code === inv.statusCode)?.label ?? inv.statusCode;
    const customer = MOCK_INVOICE_CUSTOMERS.find((c) => c.id === inv.customerId);

    return {
      id: inv.id,
      invoiceNumber: inv.invoiceNumber,
      customerBusinessName: customer?.businessName ?? '—',
      dateLabel: formatInvoiceDate(inv.issueDate),
      svcLabel: svc.label,
      svcBg: svc.bg,
      svcFg: svc.fg,
      amountLabel: formatInr(totalOf(inv)),
      statusLabel,
      stBg: st.bg,
      stFg: st.fg
    };
  }
}
