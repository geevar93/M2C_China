import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../core/services/auth.service';
import { MasterDataService } from '../core/services/master-data.service';
import { extractErrorMessage } from '../core/services/problem-details.util';
import { StatusStyleService } from '../shared/services/status-style.service';
import { SERVICE_TYPE_CIF, SERVICE_TYPE_FREIGHT_ONLY } from '../shared/constants/service-type-codes';
import { CustomersService } from './services/customers.service';
import { CreateCustomerRequest, DuplicateCustomerProblemDetails } from './models/customer.models';

interface ServiceTypeCardCopy {
  headline: string;
  description: string;
}

/** Descriptive copy ported verbatim from the prototype's two service-type cards
 *  (Source/Sourcing Ops Platform.dc.html ~line 312-327). Keyed by lookup `code`
 *  (not id) so it survives a relabel and degrades gracefully for any future
 *  service type via DEFAULT_SERVICE_TYPE_COPY — the *set* of cards itself is
 *  driven by MasterDataService.serviceTypeOptions(), never hard-coded (DR-6). */
const SERVICE_TYPE_COPY: Record<string, ServiceTypeCardCopy> = {
  [SERVICE_TYPE_CIF]: {
    headline: 'Full-service',
    description:
      'We source from our vendor roster and deliver landed goods. Platform holds product, vendor, pricing, insurance and landed cost.'
  },
  FREIGHT_ONLY: {
    headline: 'Transport only',
    description: 'Customer already bought the goods elsewhere. Platform holds shipment details, external purchase reference and freight fee.'
  }
};
const DEFAULT_SERVICE_TYPE_COPY: ServiceTypeCardCopy = {
  headline: 'Service type',
  description: 'Determines which downstream fields apply to this customer.'
};

/** Not master data — a fixed dial convenience list for a free-text phone field, not a category/status/service-type lookup (DR-6 doesn't apply). */
const COUNTRY_CODES = ['+91', '+86', '+971'];
/** Same reasoning — `sourceChannel` is a plain string on CustomerListItem, not an id into any MasterDataResponse collection. */
const SOURCE_CHANNELS = ['WhatsApp', 'Call', 'Referral', 'Other'];

function phoneDigitsValidator(control: AbstractControl): ValidationErrors | null {
  const digits = String(control.value ?? '').replace(/\D/g, '');
  return digits.length === 10 ? null : { phoneDigits: true };
}

/**
 * Deliberately light (N-37 brief): 15 alphanumeric characters when the user
 * has typed anything at all. No checksum, no positional structure check —
 * the server enforces the same length + alphanumeric rule and a wrongly
 * rejected real GSTIN is worse than a wrongly accepted one. Empty is valid;
 * this field is optional.
 */
function gstinFormatValidator(control: AbstractControl): ValidationErrors | null {
  const value = String(control.value ?? '').trim();
  if (!value) return null;
  return /^[A-Za-z0-9]{15}$/.test(value) ? null : { gstinFormat: true };
}

interface DuplicateInfo {
  businessName: string;
  name: string;
  phone: string;
}

/**
 * New Lead Intake (ACTION_PLAN E4-13) — ported from Source/Sourcing Ops
 * Platform.dc.html `showIntake` (~line 251). Deviations from the prototype's
 * exact markup, all flagged in the E4-13 report:
 *  - "Assigned Owner" is not an editable picker: the given API contract has
 *    no user-listing endpoint an Associate can safely call (only
 *    `GET /admin/users`, gated on Admin.ManageUsers). The lead is owned by
 *    the creating user; full reassignment is E4-09, out of this pass's scope.
 *  - The prototype's live "possible duplicate" banner (computed from an
 *    in-memory customer list while typing) is replaced by the real, server-
 *    authoritative 409 flow (E4-10): submit, and only on a genuine duplicate
 *    does a confirm dialog appear.
 *  - The "+ Other" custom-category affordance is dropped: adding a category
 *    to the master list is admin-only functionality (E11-08), out of scope
 *    and gated on an unsigned design; category chips are exactly
 *    MasterDataService.categoryOptions(), no parallel local list (DR-6).
 *  - The external-purchase-reference block is six discrete fields
 *    (CustomerDetail's external* fields) instead of the prototype's one
 *    combined free-text input — the binding contract requires each field
 *    individually; only the show/hide-on-freight-only behaviour is 1:1.
 */
@Component({
  selector: 'app-customer-intake',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './intake.component.html',
  styleUrl: './intake.component.scss'
})
export class CustomerIntakeComponent {
  private readonly fb = inject(FormBuilder);
  private readonly customersService = inject(CustomersService);
  private readonly masterDataService = inject(MasterDataService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly styles = inject(StatusStyleService);
  readonly countryCodes = COUNTRY_CODES;
  readonly sourceChannels = SOURCE_CHANNELS;

  readonly serviceTypeOptions = toSignal(this.masterDataService.serviceTypeOptions(), { initialValue: [] });
  readonly leadStatusOptions = toSignal(this.masterDataService.leadStatusOptions(), { initialValue: [] });
  readonly categoryOptions = toSignal(this.masterDataService.categoryOptions(), { initialValue: [] });
  readonly masterDataError = toSignal(this.masterDataService.error$, { initialValue: null });
  readonly currentUser = toSignal(this.auth.currentUser$, { initialValue: null });

  readonly serviceTypeId = signal('');
  readonly statusId = signal('');
  readonly selectedCategoryIds = signal<string[]>([]);
  readonly tags = signal<string[]>([]);
  readonly tagInput = signal('');

  readonly saving = signal(false);
  readonly submitError = signal<string | null>(null);
  readonly duplicate = signal<DuplicateInfo | null>(null);
  /** Server-side 400 field error for `gstin` (same shape the backend already
   *  uses for the invoice-issue gate's `errors` dictionary — see
   *  InvoiceDetailComponent.extractIssueError — applied here per-field
   *  instead of as a banner, since GSTIN has one specific home to surface in). */
  readonly gstinServerError = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    name: ['', Validators.required],
    businessName: ['', Validators.required],
    cc: ['+91', Validators.required],
    phone: ['', [Validators.required, phoneDigitsValidator]],
    email: ['', Validators.email],
    gstin: ['', gstinFormatValidator],
    city: [''],
    sourceChannel: ['WhatsApp', Validators.required],
    notes: [''],
    externalMarketplace: [''],
    externalOrderRef: [''],
    externalSupplierName: [''],
    externalOrderValue: [''],
    externalOrderCurrency: [''],
    externalOrderDate: ['']
  });

  private readonly selectedServiceTypeRow = computed(() => this.serviceTypeOptions().find((o) => o.id === this.serviceTypeId()));

  readonly categoryChips = computed(() =>
    this.categoryOptions().map((c) => ({ id: c.id, name: c.name, selected: this.selectedCategoryIds().includes(c.id) }))
  );

  constructor() {
    this.masterDataService.ensureLoaded().subscribe({ error: () => {} });

    // Default the service-type/status pickers to a sane first choice once
    // master data has loaded — the prototype defaults form.svc to 'CIF' and
    // form.status to 'NEW'; here that's "the CIF row if present, else the
    // first active row" / "the first active lead status by sortOrder".
    effect(
      () => {
        const opts = this.serviceTypeOptions();
        if (opts.length && !this.serviceTypeId()) {
          const cif = opts.find((o) => o.code === 'CIF');
          this.serviceTypeId.set((cif ?? opts[0]).id);
        }
      },
      { allowSignalWrites: true }
    );
    effect(
      () => {
        const opts = this.leadStatusOptions();
        if (opts.length && !this.statusId()) {
          this.statusId.set(opts[0].id);
        }
      },
      { allowSignalWrites: true }
    );
  }

  get phoneDigits(): string {
    return (this.form.controls.phone.value ?? '').replace(/\D/g, '');
  }

  get phoneOk(): boolean {
    return this.phoneDigits.length === 10;
  }

  get phoneBorderColor(): string {
    return this.phoneDigits.length > 0 && !this.phoneOk ? '#e53935' : '#e5e7eb';
  }

  get phoneMsg(): string {
    const cc = this.form.controls.cc.value;
    if (this.phoneDigits.length === 0) return `Stored as ${cc} + 10 digits for click-to-chat.`;
    return this.phoneOk ? `Valid · ${cc} ${this.phoneDigits}` : `Needs 10 digits — ${this.phoneDigits.length} entered.`;
  }

  get phoneMsgColor(): string {
    return this.phoneDigits.length > 0 && !this.phoneOk ? '#e53935' : '#6b7280';
  }

  /**
   * Uppercase on blur, not mid-typing — the server stores GSTINs uppercased,
   * so this keeps what's saved matching what's shown without rewriting the
   * field under the user's cursor while they type (no existing precedent
   * elsewhere in this form to match instead).
   */
  onGstinBlur(): void {
    const value = this.form.controls.gstin.value;
    if (value) this.form.controls.gstin.setValue(value.toUpperCase());
  }

  get showExtRef(): boolean {
    return this.selectedServiceTypeRow()?.code === SERVICE_TYPE_FREIGHT_ONLY;
  }

  get formMeta(): string {
    const statusLabel = this.leadStatusOptions().find((o) => o.id === this.statusId())?.label ?? '—';
    const svcLabel = this.selectedServiceTypeRow()?.label ?? '—';
    return `Will be logged as ${statusLabel} · ${svcLabel} · source ${this.form.controls.sourceChannel.value}`;
  }

  serviceTypeCopy(code: string): ServiceTypeCardCopy {
    return SERVICE_TYPE_COPY[code] ?? DEFAULT_SERVICE_TYPE_COPY;
  }

  pickServiceType(id: string): void {
    this.serviceTypeId.set(id);
  }

  toggleCategory(id: string): void {
    this.selectedCategoryIds.update((ids) => (ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]));
  }

  /**
   * Mirrors the backend's tag normalization (E4-11: trim, drop empties,
   * de-duplicate case-insensitively) client-side so what the user sees in
   * the chip list is exactly what gets persisted — no surprise merges after
   * save.
   */
  addTag(): void {
    const raw = this.tagInput().trim();
    this.tagInput.set('');
    if (!raw) return;
    const isDuplicate = this.tags().some((t) => t.toLowerCase() === raw.toLowerCase());
    if (isDuplicate) return;
    this.tags.update((list) => [...list, raw]);
  }

  removeTag(tag: string): void {
    this.tags.update((list) => list.filter((t) => t !== tag));
  }

  save(): void {
    if (this.saving()) return;
    this.submitError.set(null);
    this.gstinServerError.set(null);
    this.form.markAllAsTouched();
    if (this.form.invalid || !this.serviceTypeId() || !this.statusId()) {
      if (!this.phoneOk) this.submitError.set('Enter a valid 10-digit WhatsApp/phone number.');
      return;
    }
    this.saving.set(true);
    this.submit(false);
  }

  confirmDuplicateSave(): void {
    this.saving.set(true);
    this.submit(true);
  }

  cancelDuplicate(): void {
    this.duplicate.set(null);
  }

  private submit(confirmDuplicate: boolean): void {
    const payload = this.buildPayload(confirmDuplicate);
    this.customersService.create(payload).subscribe({
      next: (customer) => {
        this.saving.set(false);
        this.duplicate.set(null);
        this.router.navigate(['/customers', customer.id]);
      },
      error: (err: unknown) => {
        this.saving.set(false);
        if (err instanceof HttpErrorResponse && err.status === 409) {
          const body = err.error as DuplicateCustomerProblemDetails | null;
          const existing = body?.existingCustomer;
          this.duplicate.set({
            businessName: existing?.businessName ?? body?.detail ?? 'An existing customer',
            name: existing?.name ?? '',
            phone: existing?.phone ?? this.rawPhone()
          });
          return;
        }
        if (err instanceof HttpErrorResponse && err.status === 400) {
          const body = err.error as { errors?: Record<string, string[]> } | undefined;
          const gstinMessages = body?.errors?.['gstin'];
          if (gstinMessages?.length) {
            this.gstinServerError.set(gstinMessages.join(' '));
            this.form.controls.gstin.markAsTouched();
            return;
          }
        }
        this.submitError.set(extractErrorMessage(err, 'Could not save this lead. Please try again.'));
      }
    });
  }

  private rawPhone(): string {
    const { cc } = this.form.getRawValue();
    return `${cc} ${this.phoneDigits}`.trim();
  }

  private buildPayload(confirmDuplicate: boolean): CreateCustomerRequest {
    const raw = this.form.getRawValue();
    const isFreight = this.showExtRef;
    const orderValue = raw.externalOrderValue ? Number(raw.externalOrderValue) : NaN;

    return {
      name: raw.name.trim(),
      businessName: raw.businessName.trim(),
      phone: this.rawPhone(),
      email: raw.email.trim() || null,
      gstin: raw.gstin.trim() ? raw.gstin.trim().toUpperCase() : null,
      city: raw.city.trim() || null,
      sourceChannel: raw.sourceChannel,
      serviceTypeId: this.serviceTypeId(),
      statusId: this.statusId(),
      categoryIds: this.selectedCategoryIds(),
      ownerUserId: this.currentUser()?.id ?? null,
      tags: this.tags(),
      notes: raw.notes.trim() || null,
      externalMarketplace: isFreight ? raw.externalMarketplace.trim() || null : null,
      externalOrderRef: isFreight ? raw.externalOrderRef.trim() || null : null,
      externalSupplierName: isFreight ? raw.externalSupplierName.trim() || null : null,
      externalOrderValue: isFreight && !Number.isNaN(orderValue) ? orderValue : null,
      externalOrderCurrency: isFreight ? raw.externalOrderCurrency.trim() || null : null,
      externalOrderDate: isFreight ? raw.externalOrderDate || null : null,
      ...(confirmDuplicate ? { confirmDuplicate: true } : {})
    };
  }
}
