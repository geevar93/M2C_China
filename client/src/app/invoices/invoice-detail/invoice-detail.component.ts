import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { MasterDataService } from '../../core/services/master-data.service';
import { StatusStyleService } from '../../shared/services/status-style.service';
import {
  MOCK_INVOICE_CUSTOMERS,
  MOCK_INVOICES,
  MockInvoice,
  MockInvoiceCustomer,
  MockInvoiceStatusCode,
  subtotalOf,
  taxOf,
  totalOf
} from '../mock-invoices';
import { formatInr, formatInvoiceDate } from '../utils/format.util';

interface TrailStep {
  code: MockInvoiceStatusCode;
  label: string;
  reached: boolean;
  dotColor: string;
}

type MarkPaidAvailability = 'hidden' | 'disabled' | 'enabled';

/**
 * Invoice generate / detail (ACTION_PLAN E0-05b / E8-09/E8-10 design pass).
 * DESIGN PREVIEW ON MOCKED DATA — see `../mock-invoices.ts` and
 * ACTION_PLAN.md §12.3 for why: no `InvoicesController`, no invoice entity,
 * no migration exists. This one component serves both routes wired in
 * `app.routes.ts`:
 *  - `/invoices/new`    → generate mode (no `:id` route param)
 *  - `/invoices/:id`    → detail mode, looked up in the mocked array
 *
 * Design decisions carried over verbatim from `docs/SCREEN_DESIGNS.md`:
 *  - The "From" (company) card renders an explicit not-configured empty
 *    state rather than invented legal-entity/GSTIN/bank details — FSD Q9c is
 *    genuinely unanswered and `company_settings` is genuinely empty.
 *  - The invoice number is a visible placeholder with a note that its format
 *    is not yet decided (E8-08, blocked on Q9c) — picking one here would
 *    quietly become the decision.
 *  - Mark Paid is a distinct action (not a status dropdown entry): it
 *    captures a paid date + optional reference, and is only offered at all
 *    when the signed-in user has `Invoicing.MarkPaid` (FR-BIL-06). Since
 *    there is no backend to persist it, confirming shows a preview message
 *    rather than mutating the mocked invoice — it must never silently do
 *    nothing, and never pretend to have saved something it didn't.
 *  - Download PDF / Send via WhatsApp / Cancel are drawn but inert
 *    (disabled + a `title` explaining what unbuilt piece they depend on),
 *    matching the same pattern the ported customer-detail screen already
 *    uses for its own inert "Send Catalog" / "Edit" actions.
 */
@Component({
  selector: 'app-invoice-detail',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './invoice-detail.component.html',
  styleUrl: './invoice-detail.component.scss'
})
export class InvoiceDetailComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly masterDataService = inject(MasterDataService);
  private readonly styles = inject(StatusStyleService);
  private readonly auth = inject(AuthService);

  readonly formatInr = formatInr;

  private readonly masterData = toSignal(this.masterDataService.masterData$, {
    initialValue: { status: 'idle' as const, data: null, error: null }
  });

  readonly masterDataLoading = computed(() => {
    const status = this.masterData().status;
    return status === 'loading' || status === 'idle';
  });
  readonly masterDataError = computed(() => this.masterData().error);
  readonly hasMasterData = computed(() => !!this.masterData().data);

  private readonly routeId = toSignal(this.route.paramMap.pipe(map((p) => p.get('id'))), {
    initialValue: this.route.snapshot.paramMap.get('id')
  });

  readonly isGenerateMode = computed(() => !this.routeId());

  readonly invoice = computed<MockInvoice | null>(() => {
    const id = this.routeId();
    if (!id) return null;
    return MOCK_INVOICES.find((i) => i.id === id) ?? null;
  });

  readonly invoiceNotFound = computed(() => !this.isGenerateMode() && !this.invoice());

  readonly customerDirectory = MOCK_INVOICE_CUSTOMERS;

  /** Bill-to picker, generate mode only — entirely local state, never an HTTP call. */
  readonly selectedCustomerId = signal('');

  readonly billToCustomer = computed<MockInvoiceCustomer | null>(() => {
    const inv = this.invoice();
    const id = inv ? inv.customerId : this.selectedCustomerId();
    if (!id) return null;
    return MOCK_INVOICE_CUSTOMERS.find((c) => c.id === id) ?? null;
  });

  readonly dateLabel = computed(() => (this.invoice() ? formatInvoiceDate(this.invoice()!.issueDate) : '—'));

  readonly statusChip = computed(() => {
    const inv = this.invoice();
    const fallback = this.styles.status('DRAFT');
    if (!inv) return { ...fallback, label: 'Draft' };
    const md = this.masterData().data;
    const label = md?.invoiceStatuses.find((s) => s.code === inv.statusCode)?.label ?? inv.statusCode;
    return { ...this.styles.status(inv.statusCode), label };
  });

  readonly subtotalLabel = computed(() => formatInr(this.invoice() ? subtotalOf(this.invoice()!) : 0));
  readonly taxLabel = computed(() => formatInr(this.invoice() ? taxOf(this.invoice()!) : 0));
  readonly totalLabel = computed(() => formatInr(this.invoice() ? totalOf(this.invoice()!) : 0));
  readonly taxRatePct = computed(() => this.invoice()?.taxRatePct ?? 0);

  readonly trailSteps = computed<TrailStep[]>(() => {
    const codes: MockInvoiceStatusCode[] = ['DRAFT', 'ISSUED', 'PAID'];
    const labels: Record<MockInvoiceStatusCode, string> = {
      DRAFT: 'Draft',
      ISSUED: 'Issued',
      PAID: 'Paid',
      CANCELLED: 'Cancelled'
    };
    const status = this.invoice()?.statusCode ?? 'DRAFT';
    const idx = status === 'CANCELLED' ? 0 : codes.indexOf(status);
    return codes.map((code, i) => ({
      code,
      label: labels[code],
      reached: i <= idx,
      dotColor: i <= idx ? this.styles.status(code).fg : 'var(--color-border)'
    }));
  });

  readonly isCancelled = computed(() => this.invoice()?.statusCode === 'CANCELLED');
  readonly cancelledChip = computed(() => this.styles.status('CANCELLED'));

  readonly canMarkPaid = computed(() => this.auth.hasPermission('Invoicing.MarkPaid'));

  readonly markPaidAvailability = computed<MarkPaidAvailability>(() => {
    if (!this.canMarkPaid()) return 'hidden';
    const inv = this.invoice();
    if (!inv) return 'hidden';
    return inv.statusCode === 'ISSUED' ? 'enabled' : 'disabled';
  });

  readonly markPaidDisabledReason = computed(() => {
    const inv = this.invoice();
    if (!inv) return '';
    switch (inv.statusCode) {
      case 'DRAFT':
        return 'Only issued invoices can be marked paid.';
      case 'PAID':
        return 'This invoice is already marked paid.';
      case 'CANCELLED':
        return 'Cancelled invoices cannot be marked paid.';
      default:
        return '';
    }
  });

  readonly markPaidOpen = signal(false);
  readonly paidDateInput = signal(this.today());
  readonly paidReferenceInput = signal('');
  readonly paidDateError = signal<string | null>(null);
  readonly markPaidPreviewMessage = signal<string | null>(null);

  constructor() {
    this.masterDataService.ensureLoaded().subscribe({ error: () => {} });
  }

  retryMasterData(): void {
    this.masterDataService.reload().subscribe({ error: () => {} });
  }

  openMarkPaid(): void {
    this.markPaidOpen.set(true);
    this.paidDateInput.set(this.today());
    this.paidReferenceInput.set('');
    this.paidDateError.set(null);
    this.markPaidPreviewMessage.set(null);
  }

  cancelMarkPaid(): void {
    this.markPaidOpen.set(false);
  }

  /**
   * There is no invoicing API to call, so confirming never mutates the mocked
   * invoice's status — that would look like a working feature and isn't one.
   * Instead it surfaces a plain-language preview message naming exactly what
   * would happen once E8-10 exists, per the task brief's "never silently do
   * nothing, and never throw" rule for actions that would hit a nonexistent
   * backend.
   */
  confirmMarkPaid(): void {
    if (!this.paidDateInput()) {
      this.paidDateError.set('Paid date is required.');
      return;
    }
    this.paidDateError.set(null);
    const ref = this.paidReferenceInput().trim();
    const refPart = ref ? ` and reference "${ref}"` : '';
    this.markPaidPreviewMessage.set(
      `Design preview — marking this invoice paid would call the invoicing API (not built yet, E8-10) with ` +
        `paid date ${this.paidDateInput()}${refPart}. No data was changed.`
    );
    this.markPaidOpen.set(false);
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }
}
