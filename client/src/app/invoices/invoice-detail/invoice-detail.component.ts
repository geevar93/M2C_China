import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { MasterDataService } from '../../core/services/master-data.service';
import { extractErrorMessage } from '../../core/services/problem-details.util';
import { CustomersService } from '../../customers/services/customers.service';
import { ShipmentsService } from '../../shipments/services/shipments.service';
import { StatusStyleService, StatusColor } from '../../shared/services/status-style.service';
import { formatTimelineDate } from '../../shared/utils/date-format.util';
import { saveBlobAs } from '../../shared/utils/file-download.util';
import {
  CompanySettings,
  CreateInvoiceRequest,
  InvoiceDetail,
  InvoiceStatusCode,
  canIssueInvoices
} from '../models/invoice.models';
import { InvoicesService } from '../services/invoices.service';
import { formatInr, formatInvoiceDate } from '../utils/format.util';

interface TrailStep {
  code: InvoiceStatusCode;
  label: string;
  reached: boolean;
  dotColor: string;
}

interface CustomerOption {
  id: string;
  name: string;
}

interface ShipmentOption {
  id: string;
  reference: string;
}

type MarkPaidAvailability = 'hidden' | 'disabled' | 'enabled';

/**
 * Invoice generate / detail (ACTION_PLAN E8-10), wired against the real
 * `/invoices` + `/admin/company-settings` API (`InvoicesService`,
 * `invoice.models.ts`) instead of the earlier design-only mock. Serves both
 * routes wired in `app.routes.ts`:
 *  - `/invoices/new` → generate mode (no `:id` route param, POST on save)
 *  - `/invoices/:id` → detail mode, `GET /invoices/{id}`
 *
 * Load-bearing behaviours carried over from the approved design
 * (docs/SCREEN_DESIGNS.md §E0-05b) and the live-verified contract:
 *  - The "From" (company) card renders the explicit "not configured" empty
 *    state whenever `canIssueInvoices(companySettings)` is false — company
 *    billing details are a real, renderable gap (D-70: all-null is a 200,
 *    not a 404), not an error to swallow.
 *  - Only status transitions legal from the invoice's *current* `status.code`
 *    are ever offered (DRAFT → ISSUED, DRAFT/ISSUED → CANCELLED). There is no
 *    "→ DRAFT" transition and ISSUED → PAID is reachable ONLY through the
 *    separate `markPaid()` action, never the status endpoint.
 *  - Mark Paid stays a distinct `Invoicing.MarkPaid`-gated action (FR-BIL-06,
 *    FSD A9) — no balances, no reconciliation UI.
 *  - 409s from edit/mark-paid/PDF-download are expected, meaningful responses
 *    (D-69) — their ProblemDetails `detail` is surfaced verbatim, never a
 *    generic failure banner. The 400 issue-time company-settings validation
 *    error is handled the same way.
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
  private readonly router = inject(Router);
  private readonly invoicesService = inject(InvoicesService);
  private readonly customersService = inject(CustomersService);
  private readonly shipmentsService = inject(ShipmentsService);
  private readonly masterDataService = inject(MasterDataService);
  private readonly styles = inject(StatusStyleService);
  private readonly auth = inject(AuthService);

  readonly formatInr = formatInr;
  readonly formatInvoiceDate = formatInvoiceDate;
  readonly formatTimelineDate = formatTimelineDate;

  private readonly routeId = toSignal(this.route.paramMap.pipe(map((p) => p.get('id'))), {
    initialValue: this.route.snapshot.paramMap.get('id')
  });

  readonly isGenerateMode = computed(() => !this.routeId());

  readonly invoiceStatusOptions = toSignal(this.masterDataService.invoiceStatusOptions(), { initialValue: [] });

  // ---- Load state ----------------------------------------------------------
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly invoice = signal<InvoiceDetail | null>(null);

  // ---- Company settings (the issue-time gate) -------------------------------
  readonly companySettingsLoading = signal(true);
  readonly companySettings = signal<CompanySettings | null>(null);
  readonly canIssue = computed(() => canIssueInvoices(this.companySettings()));
  readonly issueDisabledReason = computed(() => {
    if (this.companySettingsLoading()) return 'Checking company billing settings…';
    return 'Company billing details are not configured. A Super Admin must set the legal entity name and registered address under Admin > Company Settings before this invoice can be issued.';
  });

  // ---- Customer directory, for the Bill-to picker in generate/edit mode ----
  readonly customersLoading = signal(false);
  readonly customerOptions = signal<CustomerOption[]>([]);

  // ---- Generate / edit form --------------------------------------------------
  readonly isEditing = signal(false);
  readonly isFormMode = computed(() => this.isGenerateMode() || this.isEditing());

  readonly formCustomerId = signal('');
  readonly formInvoiceDate = signal(this.today());
  readonly formLineDescription = signal('');
  readonly formAmount = signal('');
  readonly formTaxAmount = signal('');
  readonly formError = signal<string | null>(null);
  readonly saving = signal(false);

  // ---- Shipment picker (N-32) — optional, scoped to the selected Bill-to customer.
  // Leaving no shipment selected is the normal, fully valid case (freight-only);
  // selecting one is what makes an invoice CIF (E8-01).
  readonly formShipmentId = signal('');
  readonly shipmentOptions = signal<ShipmentOption[]>([]);
  readonly shipmentsLoading = signal(false);
  readonly hasNoShipments = computed(
    () => !this.shipmentsLoading() && !!this.formCustomerId() && this.shipmentOptions().length === 0
  );

  // ---- Status actions (Issue / Cancel) ---------------------------------------
  readonly statusActionLoading = signal<'issue' | 'cancel' | null>(null);
  readonly statusActionError = signal<string | null>(null);
  readonly cancelConfirmOpen = signal(false);

  // ---- PDF download ----------------------------------------------------------
  readonly downloadingPdf = signal(false);
  readonly downloadError = signal<string | null>(null);

  // ---- Derived view state -----------------------------------------------------
  readonly statusChip = computed<StatusColor & { label: string }>(() => {
    const inv = this.invoice();
    if (!inv) return { ...this.styles.status('DRAFT'), label: 'Draft' };
    return { ...this.styles.status(inv.status.code), label: inv.status.label };
  });

  readonly dateLabel = computed(() => (this.invoice() ? formatInvoiceDate(this.invoice()!.invoiceDate) : '—'));

  readonly trailSteps = computed<TrailStep[]>(() => {
    const codes: InvoiceStatusCode[] = ['DRAFT', 'ISSUED', 'PAID'];
    const labels: Record<InvoiceStatusCode, string> = { DRAFT: 'Draft', ISSUED: 'Issued', PAID: 'Paid', CANCELLED: 'Cancelled' };
    const code = this.invoice()?.status.code as InvoiceStatusCode | undefined;
    const idx = !code || code === 'CANCELLED' ? -1 : codes.indexOf(code);
    return codes.map((c, i) => ({
      code: c,
      label: labels[c],
      reached: i <= idx,
      dotColor: i <= idx ? this.styles.status(c).fg : 'var(--color-border)'
    }));
  });

  readonly isCancelled = computed(() => this.invoice()?.status.code === 'CANCELLED');
  readonly cancelledChip = computed(() => this.styles.status('CANCELLED'));

  readonly canEditAction = computed(() => this.invoice()?.status.code === 'DRAFT');
  readonly canIssueAction = computed(() => this.invoice()?.status.code === 'DRAFT');
  readonly canCancelAction = computed(() => {
    const code = this.invoice()?.status.code;
    return code === 'DRAFT' || code === 'ISSUED';
  });

  readonly canMarkPaid = computed(() => this.auth.hasPermission('Invoicing.MarkPaid'));

  readonly markPaidAvailability = computed<MarkPaidAvailability>(() => {
    if (!this.canMarkPaid()) return 'hidden';
    const inv = this.invoice();
    if (!inv) return 'hidden';
    return inv.status.code === 'ISSUED' ? 'enabled' : 'disabled';
  });

  readonly markPaidDisabledReason = computed(() => {
    const inv = this.invoice();
    if (!inv) return '';
    switch (inv.status.code) {
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
  readonly markPaidSaving = signal(false);
  readonly markPaidError = signal<string | null>(null);

  constructor() {
    this.masterDataService.ensureLoaded().subscribe({ error: () => {} });
    this.loadCompanySettings();
    this.loadCustomers();

    effect(() => {
      const id = this.routeId();
      if (id) {
        this.load(id);
      } else {
        this.invoice.set(null);
        this.resetForm();
      }
    });

    // Re-scopes the shipment picker to whichever customer is currently selected
    // on the form (generate, edit, or a startEdit() prefill) and drops a
    // shipment selection that no longer belongs to that customer — the server
    // validates the customer/shipment pairing and would 409, but the user must
    // never reach that; the option list not containing the id is enough signal.
    effect(() => {
      const customerId = this.formCustomerId();
      this.loadShipmentsFor(customerId);
    });
  }

  retry(): void {
    const id = this.routeId();
    if (id) this.load(id);
  }

  retryCompanySettings(): void {
    this.loadCompanySettings();
  }

  // ---- Generate / edit form --------------------------------------------------

  startEdit(): void {
    const inv = this.invoice();
    if (!inv) return;
    this.formCustomerId.set(inv.customer.id);
    this.formInvoiceDate.set(inv.invoiceDate);
    this.formLineDescription.set(inv.lineDescription ?? '');
    this.formAmount.set(String(inv.amount));
    this.formTaxAmount.set(String(inv.taxAmount));
    // Set before the customer-id effect's shipment fetch resolves; the fetch
    // keeps this selection as long as it's present in the reloaded options
    // for `inv.customer.id`, which it will be since it's already that
    // customer's shipment.
    this.formShipmentId.set(inv.shipmentId ?? '');
    this.formError.set(null);
    this.isEditing.set(true);
  }

  cancelEdit(): void {
    this.isEditing.set(false);
    this.formError.set(null);
  }

  save(): void {
    if (this.saving()) return;

    const customerId = this.formCustomerId();
    const invoiceDate = this.formInvoiceDate();
    const amountNum = Number(this.formAmount());
    const taxNum = Number(this.formTaxAmount());

    if (!customerId) {
      this.formError.set('Bill-to customer is required.');
      return;
    }
    if (!invoiceDate) {
      this.formError.set('Invoice date is required.');
      return;
    }
    if (!this.formAmount().trim() || Number.isNaN(amountNum) || amountNum < 0) {
      this.formError.set('Amount must be a non-negative number.');
      return;
    }
    if (!this.formTaxAmount().trim() || Number.isNaN(taxNum) || taxNum < 0) {
      this.formError.set('Tax amount must be a non-negative number.');
      return;
    }

    const request: CreateInvoiceRequest = {
      customerId,
      shipmentId: this.formShipmentId() || null,
      invoiceDate,
      lineDescription: this.formLineDescription().trim() || null,
      amount: amountNum,
      taxAmount: taxNum,
      currency: 'INR'
    };

    this.formError.set(null);
    this.saving.set(true);

    if (this.isGenerateMode()) {
      this.invoicesService.create(request).subscribe({
        next: (created) => {
          this.saving.set(false);
          this.router.navigate(['/invoices', created.id]);
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.formError.set(extractErrorMessage(err, 'Could not create this invoice. Please try again.'));
        }
      });
      return;
    }

    const inv = this.invoice();
    if (!inv) {
      this.saving.set(false);
      return;
    }
    this.invoicesService.update(inv.id, request).subscribe({
      next: (updated) => {
        this.saving.set(false);
        this.invoice.set(updated);
        this.isEditing.set(false);
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.formError.set(extractErrorMessage(err, 'Could not save changes to this invoice. Please try again.'));
      }
    });
  }

  // ---- Status actions ----------------------------------------------------------

  issueInvoice(): void {
    const inv = this.invoice();
    if (!inv || this.statusActionLoading()) return;
    const statusId = this.statusIdFor('ISSUED');
    if (!statusId) {
      this.statusActionError.set('Could not find the Issued status. Please reload and try again.');
      return;
    }
    this.statusActionLoading.set('issue');
    this.statusActionError.set(null);
    this.invoicesService.changeStatus(inv.id, { statusId, note: null }).subscribe({
      next: (updated) => {
        this.statusActionLoading.set(null);
        this.invoice.set(updated);
      },
      error: (err: unknown) => {
        this.statusActionLoading.set(null);
        this.statusActionError.set(this.extractIssueError(err));
      }
    });
  }

  requestCancel(): void {
    this.cancelConfirmOpen.set(true);
  }

  dismissCancel(): void {
    this.cancelConfirmOpen.set(false);
  }

  confirmCancel(): void {
    const inv = this.invoice();
    if (!inv || this.statusActionLoading()) return;
    const statusId = this.statusIdFor('CANCELLED');
    if (!statusId) {
      this.statusActionError.set('Could not find the Cancelled status. Please reload and try again.');
      return;
    }
    this.statusActionLoading.set('cancel');
    this.statusActionError.set(null);
    this.invoicesService.changeStatus(inv.id, { statusId, note: null }).subscribe({
      next: (updated) => {
        this.statusActionLoading.set(null);
        this.cancelConfirmOpen.set(false);
        this.invoice.set(updated);
      },
      error: (err: unknown) => {
        this.statusActionLoading.set(null);
        this.cancelConfirmOpen.set(false);
        this.statusActionError.set(extractErrorMessage(err, 'Could not cancel this invoice. Please try again.'));
      }
    });
  }

  // ---- Mark paid ----------------------------------------------------------------

  openMarkPaid(): void {
    this.markPaidOpen.set(true);
    this.paidDateInput.set(this.today());
    this.paidReferenceInput.set('');
    this.paidDateError.set(null);
    this.markPaidError.set(null);
  }

  cancelMarkPaid(): void {
    if (this.markPaidSaving()) return;
    this.markPaidOpen.set(false);
  }

  confirmMarkPaid(): void {
    const inv = this.invoice();
    if (!inv || this.markPaidSaving()) return;
    if (!this.paidDateInput()) {
      this.paidDateError.set('Paid date is required.');
      return;
    }
    this.paidDateError.set(null);
    this.markPaidError.set(null);
    this.markPaidSaving.set(true);

    this.invoicesService
      .markPaid(inv.id, {
        paidAt: toIsoOrNull(this.paidDateInput()),
        paidReference: this.paidReferenceInput().trim() || null
      })
      .subscribe({
        next: (updated) => {
          this.markPaidSaving.set(false);
          this.invoice.set(updated);
          this.markPaidOpen.set(false);
        },
        error: (err: unknown) => {
          this.markPaidSaving.set(false);
          this.markPaidError.set(extractErrorMessage(err, 'Could not mark this invoice paid. Please try again.'));
        }
      });
  }

  // ---- PDF download ---------------------------------------------------------------

  downloadPdf(): void {
    const inv = this.invoice();
    if (!inv || !inv.hasPdf || this.downloadingPdf()) return;
    this.downloadingPdf.set(true);
    this.downloadError.set(null);
    this.invoicesService.downloadPdf(inv.id).subscribe({
      next: (blob) => {
        this.downloadingPdf.set(false);
        saveBlobAs(blob, `${inv.invoiceNumber}.pdf`);
      },
      error: (err: unknown) => {
        this.downloadingPdf.set(false);
        this.downloadError.set(extractErrorMessage(err, 'Could not download this invoice PDF. Please try again.'));
      }
    });
  }

  // ---- Presentation helpers used from the template -----------------------------

  historyChip(code: string): StatusColor {
    return this.styles.status(code);
  }

  // ---- Internals ------------------------------------------------------------------

  private load(id: string): void {
    this.loading.set(true);
    this.error.set(null);
    this.isEditing.set(false);
    this.invoicesService.get(id).subscribe({
      next: (inv) => {
        this.invoice.set(inv);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.error.set(extractErrorMessage(err, 'Could not load this invoice. Please try again.'));
      }
    });
  }

  private loadCompanySettings(): void {
    this.companySettingsLoading.set(true);
    this.invoicesService.getCompanySettings().subscribe({
      next: (settings) => {
        this.companySettingsLoading.set(false);
        this.companySettings.set(settings);
      },
      error: () => {
        // Treated as "not configured" rather than a blocking error — the Issue
        // action simply stays disabled, same as the genuine all-null 200 (D-70).
        this.companySettingsLoading.set(false);
        this.companySettings.set(null);
      }
    });
  }

  private loadCustomers(): void {
    this.customersLoading.set(true);
    this.customersService.list({ page: 1, pageSize: 200 }).subscribe({
      next: (res) => {
        this.customersLoading.set(false);
        this.customerOptions.set(res.items.map((c) => ({ id: c.id, name: c.businessName })));
      },
      error: () => this.customersLoading.set(false)
    });
  }

  private resetForm(): void {
    this.formCustomerId.set('');
    this.formInvoiceDate.set(this.today());
    this.formLineDescription.set('');
    this.formAmount.set('');
    this.formTaxAmount.set('');
    this.formShipmentId.set('');
    this.formError.set(null);
  }

  /**
   * Re-fetches the shipment options for `customerId` via
   * `ShipmentsService.list({ customerId })` (N-32). Clearing the customer
   * clears the options outright rather than issuing an unscoped request — an
   * unfiltered shipment list is never a valid picker for "this customer's
   * shipments". A currently-selected shipment that does not come back in the
   * new list is dropped, which is what makes a customer change safe: the
   * user can never submit a shipment that belongs to a different customer.
   */
  private loadShipmentsFor(customerId: string): void {
    if (!customerId) {
      this.shipmentOptions.set([]);
      this.formShipmentId.set('');
      this.shipmentsLoading.set(false);
      return;
    }
    this.shipmentsLoading.set(true);
    this.shipmentsService.list({ customerId, pageSize: 200 }).subscribe({
      next: (res) => {
        this.shipmentsLoading.set(false);
        const options = res.items.map((s) => ({ id: s.id, reference: s.reference ?? s.id }));
        this.shipmentOptions.set(options);
        if (this.formShipmentId() && !options.some((o) => o.id === this.formShipmentId())) {
          this.formShipmentId.set('');
        }
      },
      error: () => {
        // A failure here only degrades the picker (falls back to "no shipment
        // options"), same as the customer directory — not fatal to the screen.
        this.shipmentsLoading.set(false);
        this.shipmentOptions.set([]);
        this.formShipmentId.set('');
      }
    });
  }

  private statusIdFor(code: InvoiceStatusCode): string | undefined {
    return this.invoiceStatusOptions().find((o) => o.code === code)?.id;
  }

  /**
   * The DRAFT → ISSUED company-settings gate returns 400 (not 409), with the
   * human-facing message nested under `errors.companySettings[]` rather than
   * a top-level `detail` — `extractErrorMessage` alone would fall back to the
   * generic `"Validation failed."` title, so this pulls the specific message
   * out first (live-confirmed shape, see `invoice.models.ts`).
   */
  private extractIssueError(err: unknown): string {
    if (err instanceof HttpErrorResponse && err.status === 400) {
      const body = err.error as { errors?: Record<string, string[]> } | undefined;
      const messages = body?.errors?.['companySettings'];
      if (messages?.length) return messages.join(' ');
    }
    return extractErrorMessage(err, 'Could not issue this invoice. Please try again.');
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }
}

/** `<input type="date">` yields `yyyy-MM-dd`; `MarkInvoicePaidRequest.paidAt` wants a full UTC instant (or null to let the server default it to now). */
function toIsoOrNull(dateInputValue: string): string | null {
  if (!dateInputValue) return null;
  return new Date(`${dateInputValue}T00:00:00.000Z`).toISOString();
}
