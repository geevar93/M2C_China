import { Component, EventEmitter, Input, OnInit, Output, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MasterDataService } from '../../core/services/master-data.service';
import { extractErrorMessage } from '../../core/services/problem-details.util';
import { ShipmentsService } from '../services/shipments.service';
import { ShipmentDocument } from '../models/shipment.models';

/**
 * "Attach Document" dialog for a shipment detail screen (E7-13, backing
 * E7-09's `POST /shipments/{id}/documents`). The document-type dropdown is
 * populated from `MasterDataService.documentTypeOptions('Shipment')` —
 * **never** the unscoped `documentTypes` collection — per N-20(c): an
 * unscoped dropdown would let a shipment upload offer a `Vendor`-scoped type
 * that can never actually attach here.
 */
@Component({
  selector: 'app-shipment-document-upload-dialog',
  standalone: true,
  templateUrl: './document-upload-dialog.component.html',
  styleUrl: './document-upload-dialog.component.scss'
})
export class ShipmentDocumentUploadDialogComponent implements OnInit {
  private readonly shipmentsService = inject(ShipmentsService);
  private readonly masterDataService = inject(MasterDataService);

  @Input({ required: true }) shipmentId!: string;
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<ShipmentDocument>();

  readonly documentTypeOptions = toSignal(this.masterDataService.documentTypeOptions('Shipment'), { initialValue: [] });

  readonly documentTypeId = signal('');
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
    const documentTypeId = this.documentTypeId();
    if (!documentTypeId) {
      this.error.set('Choose a document type.');
      return;
    }
    if (!file) {
      this.error.set('Choose a file to upload.');
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.shipmentsService.uploadDocument(this.shipmentId, file, documentTypeId).subscribe({
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
