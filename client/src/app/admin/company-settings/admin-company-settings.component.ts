import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { extractErrorMessage } from '../../core/services/problem-details.util';
import { INDIAN_STATES, stateCodeFromGstin } from '../../shared/models/indian-states';
import { CompanySettings, UpsertCompanySettingsRequest } from '../../invoices/models/invoice.models';
import { AdminCompanySettingsService } from '../services/admin-company-settings.service';
import { RefreshService } from '../../core/services/refresh.service';

/**
 * Admin > Company Settings — the billing-block editor behind
 * `/admin/company-settings`.
 *
 * This screen existed only as a backend (`AdminCompanySettingsController`, plus the
 * `CompanySettings` singleton) with no UI, while `invoice-detail` already told the
 * user "No invoice can be issued until a Super Admin configures them under
 * Admin > Company Settings". That instruction pointed at a screen that was never
 * built, so the invoicing gate was unclearable from inside the app.
 *
 * Three fields are marked required — `legalEntityName`, `registeredAddress` and
 * `stateCode` — because those are exactly what `InvoiceService` hard-gates issuing
 * on. The state code joined the set with the GST work: it decides CGST+SGST versus
 * IGST, and the server refuses to issue rather than guess a tax treatment. The
 * rest of the block is genuinely optional server-side (every field is `string?`
 * and blanks are trimmed to null), so the form does not invent requirements the
 * API does not have. The unsaved/missing state is surfaced as a banner naming the
 * outstanding fields rather than as inline errors on first load: an all-null row
 * is the legitimate "never configured" state, not user error.
 *
 * Save is a full PUT of every field, not a patch — the endpoint is an upsert over a
 * singleton, and sending only the dirty fields would null the rest.
 */
@Component({
  selector: 'app-admin-company-settings',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './admin-company-settings.component.html',
  styleUrl: './admin-company-settings.component.scss'
})
export class AdminCompanySettingsComponent {
  private readonly service = inject(AdminCompanySettingsService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly saved = signal(false);

  readonly states = INDIAN_STATES;

  readonly legalEntityName = signal('');
  readonly gstin = signal('');
  readonly stateCode = signal('');
  readonly registeredAddress = signal('');
  readonly bankAccountName = signal('');
  readonly bankAccountNumber = signal('');
  readonly bankIfsc = signal('');
  readonly bankBranch = signal('');
  readonly invoiceNumberPrefix = signal('');
  readonly declarationText = signal('');

  readonly updatedAt = signal<string | null>(null);
  readonly updatedByName = signal<string | null>(null);

  /** True once every invoice-gating field is filled — drives the banner. */
  readonly invoiceReady = computed(
    () => !!this.legalEntityName().trim() && !!this.registeredAddress().trim() && !!this.stateCode().trim()
  );

  /** Names the outstanding gate fields so the banner says what to do, not just that something is wrong. */
  readonly missingFieldLabels = computed(() => {
    const missing: string[] = [];
    if (!this.legalEntityName().trim()) missing.push('Legal entity name');
    if (!this.registeredAddress().trim()) missing.push('Registered address');
    if (!this.stateCode().trim()) missing.push('GST state');
    return missing;
  });

  /**
   * Typing a GSTIN fills the state from its first two digits, which ARE the state
   * code. Only ever fills a BLANK selection — a state picked by hand is never
   * overwritten, since the operator may be correcting a GSTIN that is itself wrong.
   */
  onGstinInput(value: string): void {
    this.gstin.set(value);
    if (!this.stateCode()) {
      const derived = stateCodeFromGstin(value);
      if (derived) {
        this.stateCode.set(derived);
      }
    }
  }

  private readonly refreshService = inject(RefreshService);

  constructor() {
    // Topbar "Refresh" reloads this screen the same way its Retry control does.
    this.refreshService.onRefresh(() => this.retry());

    this.fetch();
  }

  retry(): void {
    this.fetch();
  }

  save(): void {
    if (this.saving()) return;
    this.saveError.set(null);
    this.saved.set(false);

    if (!this.invoiceReady()) {
      this.saveError.set(
        'Legal entity name, registered address and GST state are all required before an invoice can be issued.'
      );
      return;
    }

    this.saving.set(true);
    this.service.upsert(this.buildRequest()).subscribe({
      next: (data) => {
        this.saving.set(false);
        this.apply(data);
        this.saved.set(true);
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.saveError.set(extractErrorMessage(err, 'Could not save company settings. Please try again.'));
      }
    });
  }

  /**
   * Blank strings go over the wire as null rather than "": the server trims them to
   * null anyway, and sending null keeps "cleared" and "never set" the single state
   * the DTO models.
   */
  private buildRequest(): UpsertCompanySettingsRequest {
    const trim = (s: string): string | null => (s.trim() ? s.trim() : null);
    return {
      legalEntityName: trim(this.legalEntityName()),
      gstin: trim(this.gstin()),
      stateCode: trim(this.stateCode()),
      registeredAddress: trim(this.registeredAddress()),
      bankAccountName: trim(this.bankAccountName()),
      bankAccountNumber: trim(this.bankAccountNumber()),
      bankIfsc: trim(this.bankIfsc()),
      bankBranch: trim(this.bankBranch()),
      invoiceNumberPrefix: trim(this.invoiceNumberPrefix()),
      declarationText: trim(this.declarationText())
    };
  }

  private apply(data: CompanySettings): void {
    this.legalEntityName.set(data.legalEntityName ?? '');
    this.gstin.set(data.gstin ?? '');
    this.stateCode.set(data.stateCode ?? '');
    this.registeredAddress.set(data.registeredAddress ?? '');
    this.bankAccountName.set(data.bankAccountName ?? '');
    this.bankAccountNumber.set(data.bankAccountNumber ?? '');
    this.bankIfsc.set(data.bankIfsc ?? '');
    this.bankBranch.set(data.bankBranch ?? '');
    this.invoiceNumberPrefix.set(data.invoiceNumberPrefix ?? '');
    this.declarationText.set(data.declarationText ?? '');
    this.updatedAt.set(data.updatedAt);
    this.updatedByName.set(data.updatedByName);
  }

  private fetch(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.get().subscribe({
      next: (data) => {
        this.apply(data);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.error.set(extractErrorMessage(err, 'Could not load company settings. Please try again.'));
      }
    });
  }
}
