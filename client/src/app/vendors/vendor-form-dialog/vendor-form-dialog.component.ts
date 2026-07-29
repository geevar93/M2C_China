import { Component, EventEmitter, Input, OnInit, Output, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MasterDataService } from '../../core/services/master-data.service';
import { extractErrorMessage } from '../../core/services/problem-details.util';
import { VendorsService } from '../services/vendors.service';
import { VendorDetail, VendorWriteRequest } from '../models/vendor.models';

/**
 * Shared vendor create/edit dialog. Ported from the prototype's "Edit
 * Vendor" dialog (Source/Sourcing Ops Platform.dc.html ~line 1142) and reused
 * for the vendors list' "+ Add Vendor" button (E5-01's `POST /vendors`) since
 * both forms are otherwise identical — `vendor` input is null in create mode.
 *
 * DEVIATION FROM THE PROTOTYPE (flagged, see M4 report): the prototype's
 * dialog only has Name/Contact/Phone/Region/Categories/Status/MOQ/Lead Time —
 * it predates the commercial-metadata fields (E5-06: payment terms,
 * reliability rating) and has no email field either, even though
 * `VendorDetail` displays all four. Since E5-06 explicitly requires them
 * "editable", this dialog adds Email/Payment Terms/Reliability
 * Rating/Notes fields built from the same form-field atoms rather than
 * silently leaving them permanently unwritable from the UI.
 */
@Component({
  selector: 'app-vendor-form-dialog',
  standalone: true,
  templateUrl: './vendor-form-dialog.component.html',
  styleUrl: './vendor-form-dialog.component.scss'
})
export class VendorFormDialogComponent implements OnInit {
  private readonly vendorsService = inject(VendorsService);
  private readonly masterDataService = inject(MasterDataService);

  @Input() vendor: VendorDetail | null = null;
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<VendorDetail>();

  readonly categoryOptions = toSignal(this.masterDataService.categoryOptions(), { initialValue: [] });
  readonly vendorStatusOptions = toSignal(this.masterDataService.vendorStatusOptions(), { initialValue: [] });

  readonly isEdit = computed(() => !!this.vendor);
  readonly dialogTitle = computed(() => (this.isEdit() ? 'Edit Vendor' : 'Add Vendor'));

  readonly name = signal('');
  readonly contactPerson = signal('');
  readonly phone = signal('');
  readonly email = signal('');
  readonly region = signal('');
  readonly selectedCategoryIds = signal<string[]>([]);
  readonly statusId = signal('');
  readonly moq = signal('');
  readonly leadTime = signal('');
  readonly paymentTerms = signal('');
  readonly reliabilityRating = signal('');
  readonly notes = signal('');

  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.masterDataService.ensureLoaded().subscribe({ error: () => {} });

    const v = this.vendor;
    if (!v) return;
    this.name.set(v.name);
    this.contactPerson.set(v.contactPerson ?? '');
    this.phone.set(v.phone ?? '');
    this.email.set(v.email ?? '');
    this.region.set(v.region ?? '');
    this.selectedCategoryIds.set(v.categories.map((c) => c.id));
    this.statusId.set(v.status.id);
    this.moq.set(v.moq ?? '');
    this.leadTime.set(v.leadTime ?? '');
    this.paymentTerms.set(v.paymentTerms ?? '');
    this.reliabilityRating.set(v.reliabilityRating != null ? String(v.reliabilityRating) : '');
    this.notes.set(v.notes ?? '');
  }

  toggleCategory(id: string): void {
    this.selectedCategoryIds.update((ids) => (ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]));
  }

  cancel(): void {
    this.closed.emit();
  }

  save(): void {
    if (this.saving()) return;
    const name = this.name().trim();
    const contactPerson = this.contactPerson().trim();
    const phone = this.phone().trim();
    const region = this.region().trim();
    const statusId = this.statusId();
    if (!name || !contactPerson || !phone || !region || !statusId) {
      this.error.set('Vendor name, contact person, phone, region and status are required.');
      return;
    }

    const ratingText = this.reliabilityRating().trim();
    const rating = ratingText ? Number(ratingText) : null;
    if (ratingText && (Number.isNaN(rating) || rating! < 0 || rating! > 5)) {
      this.error.set('Reliability rating must be a number between 0 and 5.');
      return;
    }

    const request: VendorWriteRequest = {
      name,
      contactPerson,
      phone,
      email: this.email().trim() || null,
      region,
      categoryIds: this.selectedCategoryIds(),
      statusId,
      moq: this.moq().trim() || null,
      leadTime: this.leadTime().trim() || null,
      paymentTerms: this.paymentTerms().trim() || null,
      reliabilityRating: rating,
      notes: this.notes().trim() || null
    };

    this.saving.set(true);
    this.error.set(null);
    const existing = this.vendor;
    const request$ = existing ? this.vendorsService.update(existing.id, request) : this.vendorsService.create(request);
    request$.subscribe({
      next: (result) => {
        this.saving.set(false);
        this.saved.emit(result);
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.error.set(extractErrorMessage(err, 'Could not save this vendor. Please try again.'));
      }
    });
  }
}
