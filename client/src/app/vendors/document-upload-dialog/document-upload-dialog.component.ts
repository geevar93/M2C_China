import { Component, EventEmitter, Input, OnInit, Output, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MasterDataService } from '../../core/services/master-data.service';
import { extractErrorMessage } from '../../core/services/problem-details.util';
import { VendorsService } from '../services/vendors.service';
import { VendorDocument } from '../models/vendor.models';

/**
 * "Attach Document" dialog for the vendor detail screen (ACTION_PLAN E5-10,
 * backing E5-07's `POST /vendors/{id}/documents` — the endpoint went live in
 * E5-07 with zero client consumers; this is the frontend half, raised as open
 * item N-22). Structurally a copy of
 * `ShipmentDocumentUploadDialogComponent` (`shipments/document-upload-dialog`)
 * — same fields, same save/cancel flow — but deliberately its own component,
 * not a shared one: this dialog's document-type dropdown is scoped to
 * `'Vendor'`, never `'Shipment'`, and it posts through `VendorsService`, not
 * `ShipmentsService`. Also distinct from `CatalogUploadDialogComponent`,
 * which files catalog PDFs into sections and has no type selector at all —
 * do not merge the two.
 */
@Component({
  selector: 'app-vendor-document-upload-dialog',
  standalone: true,
  templateUrl: './document-upload-dialog.component.html',
  styleUrl: './document-upload-dialog.component.scss'
})
export class VendorDocumentUploadDialogComponent implements OnInit {
  private readonly vendorsService = inject(VendorsService);
  private readonly masterDataService = inject(MasterDataService);

  @Input({ required: true }) vendorId!: string;
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<VendorDocument>();

  readonly documentTypeOptions = toSignal(this.masterDataService.documentTypeOptions('Vendor'), { initialValue: [] });

  readonly docTypeId = signal('');
  readonly selectedFile = signal<File | null>(null);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.masterDataService.ensureLoaded().subscribe({ error: () => {} });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile.set(input.files?.[0] ?? null);
  }

  cancel(): void {
    this.closed.emit();
  }

  save(): void {
    if (this.saving()) return;
    const file = this.selectedFile();
    const docTypeId = this.docTypeId();
    if (!docTypeId) {
      this.error.set('Choose a document type.');
      return;
    }
    if (!file) {
      this.error.set('Choose a file to upload.');
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.vendorsService.uploadDocument(this.vendorId, file, docTypeId).subscribe({
      next: (doc) => {
        this.saving.set(false);
        this.saved.emit(doc);
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.error.set(extractErrorMessage(err, 'Could not upload this document. Please try again.'));
      }
    });
  }
}
