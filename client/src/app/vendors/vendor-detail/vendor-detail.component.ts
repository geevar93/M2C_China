import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { extractErrorMessage } from '../../core/services/problem-details.util';
import { StatusStyleService } from '../../shared/services/status-style.service';
import { TimelineDatePipe } from '../../shared/pipes/timeline-date.pipe';
import { formatDateOnly } from '../../shared/utils/date-format.util';
import { formatFileSize } from '../../shared/utils/file-size.util';
import { previewBlob, saveBlobAs } from '../../shared/utils/file-download.util';
import { CatalogsService } from '../../catalogs/services/catalogs.service';
import { CatalogUploadDialogComponent } from '../../catalogs/upload-dialog/upload-dialog.component';
import { CatalogSection } from '../../catalogs/models/catalog.models';
import { DispatchDialogComponent, DispatchDocumentLock } from '../../dispatch/dispatch-dialog/dispatch-dialog.component';
import { DispatchService } from '../../dispatch/services/dispatch.service';
import { DispatchHistoryEntryDto } from '../../dispatch/models/dispatch.models';
import { VendorsService } from '../services/vendors.service';
import { VendorDetail, VendorDocument } from '../models/vendor.models';
import { VendorFormDialogComponent } from '../vendor-form-dialog/vendor-form-dialog.component';
import { VendorDocumentUploadDialogComponent } from '../document-upload-dialog/document-upload-dialog.component';
import { RefreshService } from '../../core/services/refresh.service';

interface StatTile {
  label: string;
  value: string;
}

interface DocRow {
  id: string;
  fileName: string;
  versionLabel: string;
  versionIsLatest: boolean;
  sizeLabel: string;
  uploadedLabel: string;
  /** Pre-formatted "version · size · vendor" line for the dispatch dialog's locked document panel. */
  dispatchMeta: string;
}

interface SectionRow {
  id: string;
  title: string;
  metaLabel: string;
  tags: string[];
  docs: DocRow[];
}

interface ComplianceDocRow {
  id: string;
  fileName: string;
  meta: string;
}

/**
 * Vendor detail (ACTION_PLAN E5-09) — ported from Source/Sourcing Ops
 * Platform.dc.html `showVendorDetail` (~line 546). `GET /vendors/{id}`
 * embeds `catalogSections[]`/`documents[]` directly (E5-05) so this is a
 * single load, no `forkJoin`. "Send via WhatsApp" is wired in this pass
 * (E9-05) to the shared `DispatchDialogComponent`, entered with the document
 * fixed (`documentLock`) so the dialog only needs a customer picked — unlike
 * the Catalogs screen's card (always the *latest* document), a vendor's
 * per-row button can dispatch any version, since the row is a specific
 * document. Each row also carries a "Sent to" toggle (E9-07) that expands an
 * inline history panel from `GET /catalog-documents/{id}/dispatches` —
 * gated on `Catalogs.View` (already required to see this screen at all),
 * not `Dispatch.Send`, per that endpoint's own doc comment.
 *
 * **Compliance Documents (ACTION_PLAN E5-10)**: unlike `catalogSections`,
 * `GET /vendors/{id}` does NOT embed these — `VendorDocumentDto` is a
 * separate resource, live-confirmed against `VendorDocumentDtos.cs`, so this
 * screen fires a second `GET /vendors/{id}/documents` alongside the vendor
 * load. Filed for reference only (FSD Q5) — no expiry/compliance-state UI,
 * just attach / list / download through the authenticated
 * `/vendor-documents/{id}/download` endpoint, same as a catalog document.
 */
@Component({
  selector: 'app-vendor-detail',
  standalone: true,
  imports: [
    RouterLink,
    VendorFormDialogComponent,
    CatalogUploadDialogComponent,
    DispatchDialogComponent,
    VendorDocumentUploadDialogComponent,
    TimelineDatePipe
  ],
  templateUrl: './vendor-detail.component.html',
  styleUrl: './vendor-detail.component.scss'
})
export class VendorDetailComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly vendorsService = inject(VendorsService);
  private readonly catalogsService = inject(CatalogsService);
  private readonly dispatchService = inject(DispatchService);
  private readonly styles = inject(StatusStyleService);
  private readonly auth = inject(AuthService);

  readonly canEditVendor = computed(() => this.auth.hasPermission('Vendors.Edit'));
  readonly canEditCatalogs = computed(() => this.auth.hasPermission('Catalogs.Edit'));
  readonly canDispatch = computed(() => this.auth.hasPermission('Dispatch.Send'));

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly vendor = signal<VendorDetail | null>(null);

  readonly editOpen = signal(false);
  readonly uploadOpen = signal(false);

  readonly previewingDocId = signal<string | null>(null);
  readonly previewError = signal<string | null>(null);

  readonly dispatchOpen = signal(false);
  readonly dispatchDocumentLock = signal<DispatchDocumentLock | null>(null);

  readonly historyOpenDocId = signal<string | null>(null);
  readonly historyLoading = signal(false);
  readonly historyError = signal<string | null>(null);
  readonly historyEntries = signal<DispatchHistoryEntryDto[]>([]);

  readonly documentsLoading = signal(false);
  readonly documentsError = signal<string | null>(null);
  readonly documents = signal<VendorDocument[]>([]);
  readonly documentUploadOpen = signal(false);
  readonly openingDocumentId = signal<string | null>(null);
  readonly openDocumentError = signal<string | null>(null);

  readonly statusChip = computed(() => {
    const v = this.vendor();
    return v ? this.styles.status(v.status.code) : { bg: '#e5e7eb', fg: '#374151' };
  });

  readonly subline = computed(() => {
    const v = this.vendor();
    if (!v) return '';
    const cats = v.categories.length ? v.categories.map((c) => c.name).join(', ') : '—';
    return `${v.contactPerson ?? '—'} · ${v.region ?? '—'} · ${v.phone ?? '—'} · ${cats}`;
  });

  readonly stats = computed<StatTile[]>(() => {
    const v = this.vendor();
    if (!v) return [];
    return [
      { label: 'MOQ', value: v.moq ?? '—' },
      { label: 'Lead Time', value: v.leadTime ?? '—' },
      { label: 'Reliability Rating', value: v.reliabilityRating != null ? `${v.reliabilityRating}` : '—' },
      { label: 'Catalogs', value: `${v.catalogCount}` }
    ];
  });

  readonly sections = computed<SectionRow[]>(() => {
    const v = this.vendor();
    if (!v) return [];
    return v.catalogSections.map((s) => this.toSectionRow(s));
  });

  readonly complianceDocs = computed<ComplianceDocRow[]>(() => this.documents().map((d) => this.toComplianceDocRow(d)));

  private readonly refreshService = inject(RefreshService);

  constructor() {
    // Topbar "Refresh" reloads this screen the same way its Retry control does.
    this.refreshService.onRefresh(() => this.retry());

    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      const id = params.get('id');
      if (id) this.load(id);
    });
  }

  retry(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) this.load(id);
  }

  openEdit(): void {
    this.editOpen.set(true);
  }

  cancelEdit(): void {
    this.editOpen.set(false);
  }

  onVendorSaved(vendor: VendorDetail): void {
    this.editOpen.set(false);
    this.vendor.set(vendor);
  }

  openUpload(): void {
    this.uploadOpen.set(true);
  }

  cancelUpload(): void {
    this.uploadOpen.set(false);
  }

  onUploadSaved(_section: CatalogSection): void {
    this.uploadOpen.set(false);
    const id = this.vendor()?.id;
    if (id) this.load(id);
  }

  openDispatch(doc: DocRow, sectionTitle: string): void {
    this.dispatchDocumentLock.set({
      documentId: doc.id,
      title: sectionTitle,
      filename: doc.fileName,
      meta: doc.dispatchMeta
    });
    this.dispatchOpen.set(true);
  }

  cancelDispatch(): void {
    this.dispatchOpen.set(false);
    this.dispatchDocumentLock.set(null);
  }

  onDispatchLogged(): void {
    const docId = this.dispatchDocumentLock()?.documentId;
    this.dispatchOpen.set(false);
    this.dispatchDocumentLock.set(null);
    if (docId && this.historyOpenDocId() === docId) this.loadHistory(docId);
  }

  toggleHistory(docId: string): void {
    if (this.historyOpenDocId() === docId) {
      this.historyOpenDocId.set(null);
      return;
    }
    this.historyOpenDocId.set(docId);
    this.loadHistory(docId);
  }

  retryHistory(docId: string): void {
    this.loadHistory(docId);
  }

  private loadHistory(docId: string): void {
    this.historyLoading.set(true);
    this.historyError.set(null);
    this.dispatchService.history(docId).subscribe({
      next: (entries) => {
        this.historyLoading.set(false);
        this.historyEntries.set(entries);
      },
      error: (err: unknown) => {
        this.historyLoading.set(false);
        this.historyError.set(extractErrorMessage(err, 'Could not load dispatch history. Please try again.'));
      }
    });
  }

  previewDocument(docId: string): void {
    if (this.previewingDocId()) return;
    this.previewingDocId.set(docId);
    this.previewError.set(null);
    this.catalogsService.downloadDocument(docId).subscribe({
      next: (blob) => {
        this.previewingDocId.set(null);
        previewBlob(blob);
      },
      error: (err: unknown) => {
        this.previewingDocId.set(null);
        this.previewError.set(extractErrorMessage(err, 'Could not open this document. Please try again.'));
      }
    });
  }

  openDocumentUpload(): void {
    this.documentUploadOpen.set(true);
  }

  cancelDocumentUpload(): void {
    this.documentUploadOpen.set(false);
  }

  onDocumentUploaded(_doc: VendorDocument): void {
    this.documentUploadOpen.set(false);
    const id = this.vendor()?.id;
    if (id) this.loadDocuments(id);
  }

  openComplianceDocument(doc: ComplianceDocRow): void {
    if (this.openingDocumentId()) return;
    this.openingDocumentId.set(doc.id);
    this.openDocumentError.set(null);
    this.vendorsService.downloadDocument(doc.id).subscribe({
      next: (blob) => {
        this.openingDocumentId.set(null);
        saveBlobAs(blob, doc.fileName);
      },
      error: (err: unknown) => {
        this.openingDocumentId.set(null);
        this.openDocumentError.set(extractErrorMessage(err, 'Could not open this document. Please try again.'));
      }
    });
  }

  retryDocuments(): void {
    const id = this.vendor()?.id;
    if (id) this.loadDocuments(id);
  }

  private load(id: string): void {
    this.loading.set(true);
    this.error.set(null);
    this.vendorsService.getById(id).subscribe({
      next: (vendor) => {
        this.vendor.set(vendor);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.error.set(extractErrorMessage(err, 'Could not load this vendor. Please try again.'));
      }
    });
    this.loadDocuments(id);
  }

  private loadDocuments(id: string): void {
    this.documentsLoading.set(true);
    this.documentsError.set(null);
    this.vendorsService.listDocuments(id).subscribe({
      next: (docs) => {
        this.documentsLoading.set(false);
        this.documents.set(docs);
      },
      error: (err: unknown) => {
        this.documentsLoading.set(false);
        this.documentsError.set(extractErrorMessage(err, 'Could not load compliance documents. Please try again.'));
      }
    });
  }

  private toComplianceDocRow(d: VendorDocument): ComplianceDocRow {
    return {
      id: d.id,
      fileName: d.originalFilename,
      meta: `${d.docType.label} · ${formatFileSize(d.sizeBytes)} · ${formatDateOnly(d.uploadedAt)}`
    };
  }

  private toSectionRow(section: CatalogSection): SectionRow {
    const docCount = section.documents.length;
    return {
      id: section.id,
      title: section.title,
      metaLabel: `${section.category.name} · ${docCount} document${docCount === 1 ? '' : 's'}`,
      tags: section.tags,
      docs: section.documents.map((d) => ({
        id: d.id,
        fileName: d.originalFilename,
        versionLabel: d.versionLabel ?? '—',
        versionIsLatest: d.isLatest,
        sizeLabel: formatFileSize(d.sizeBytes),
        uploadedLabel: `${formatDateOnly(d.uploadedAt)} · ${d.uploadedByName}`,
        dispatchMeta: `${d.versionLabel ?? '—'} · ${formatFileSize(d.sizeBytes)} · ${section.vendorName}`
      }))
    };
  }
}
