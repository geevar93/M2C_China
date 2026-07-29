import { Component, EventEmitter, Input, OnInit, Output, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MasterDataService } from '../../core/services/master-data.service';
import { extractErrorMessage } from '../../core/services/problem-details.util';
import { StatusStyleService } from '../../shared/services/status-style.service';
import { saveBlobAs } from '../../shared/utils/file-download.util';
import { formatFileSize } from '../../shared/utils/file-size.util';
import { CatalogsService } from '../../catalogs/services/catalogs.service';
import { CatalogSection } from '../../catalogs/models/catalog.models';
import { CustomersService } from '../../customers/services/customers.service';
import { CustomerListItem } from '../../customers/models/customer.models';
import { DispatchService } from '../services/dispatch.service';
import { CreateDispatchLogRequest, DispatchLogDto } from '../models/dispatch.models';

/** Set when opened from a fixed customer (E9-03) — the recipient side is locked, a catalog document is picked. */
export interface DispatchCustomerLock {
  id: string;
  businessName: string;
  /** Pre-formatted "contact · phone" line — the caller already has this shaped for its own screen. */
  subline: string;
  serviceTypeCode: string | null;
}

/** Set when opened from a fixed catalog document (E9-05 / a vendor's document row) — the document side is locked, a customer is picked. */
export interface DispatchDocumentLock {
  documentId: string;
  title: string;
  filename: string;
  /** Pre-formatted "version · size · vendor" line — the caller already has this shaped for its own screen. */
  meta: string;
}

interface DocumentOption {
  documentId: string;
  title: string;
  filename: string;
  meta: string;
}

interface CustomerOption {
  id: string;
  businessName: string;
  subline: string;
  serviceTypeId: string;
}

type StepId = 1 | 2 | 3;

/**
 * Shared WhatsApp dispatch dialog (ACTION_PLAN E9-03/E9-04/E9-05) — one
 * component, reused from three launch points (customer detail, the Catalogs
 * screen, a vendor's document row), never forked per screen.
 *
 * Ported from the prototype's `waOpen` dialog (Source/Sourcing Ops
 * Platform.dc.html ~lines 987-1068), with the "Pre-filled Message" preview
 * bubble, the 3-step tile grid and the footer status line carried over 1:1.
 *
 * DEVIATION FROM THE PROTOTYPE (flagged, see M4 report): the prototype's
 * dialog is entered from one generic place, so *both* Recipient and Catalog
 * PDF are always `<select>` dropdowns. This build has two real entry
 * directions — from a customer (recipient fixed) and from a catalog document
 * (document fixed) — so whichever side is fixed renders as a static panel
 * instead of a dropdown. That static layout (name/subline/chip stacked) is
 * carried over from the prototype's own `mobile` reference screen's "To" /
 * "Catalog" panels (~lines 950-964), which already show exactly this
 * locked-recipient shape — so nothing here is invented, it's a second piece
 * of the same prototype applied to the case the desktop dialog didn't need.
 *
 * ACTION_PLAN E9-04 CORRECTION (recorded in ACTION_PLAN §13.6): the story
 * text says "open chat → download PDF → mark sent" — that contradicts the
 * approved prototype, which is "Download PDF → Open WhatsApp → Attach & send
 * in chat" with a separate "Log Dispatch" action. This component ports the
 * prototype, not the story text.
 *
 * STEP-GATING DECISION (flagged): the three step tiles are advisory progress
 * markers, not a wizard — completing them is never required to enable "Log
 * Dispatch". The prototype's own footer status text ("Dispatch not logged
 * yet. Complete the three steps." vs "N of 3 steps done") describes the
 * steps as informational, and nothing in the prototype's `logDispatch`
 * handler checks `waSteps` before logging. `canLogDispatch()` below only
 * requires a valid customer/document pair, a non-empty message, and no
 * in-flight/broken compose call — never `doneCount() === 3`.
 */
@Component({
  selector: 'app-dispatch-dialog',
  standalone: true,
  templateUrl: './dispatch-dialog.component.html',
  styleUrl: './dispatch-dialog.component.scss'
})
export class DispatchDialogComponent implements OnInit {
  private readonly customersService = inject(CustomersService);
  private readonly catalogsService = inject(CatalogsService);
  private readonly dispatchService = inject(DispatchService);
  private readonly masterDataService = inject(MasterDataService);
  private readonly styles = inject(StatusStyleService);

  @Input() customerLock: DispatchCustomerLock | null = null;
  @Input() documentLock: DispatchDocumentLock | null = null;

  @Output() closed = new EventEmitter<void>();
  @Output() logged = new EventEmitter<DispatchLogDto>();

  private readonly serviceTypeOptions = toSignal(this.masterDataService.serviceTypeOptions(), { initialValue: [] });

  private readonly customersRaw = signal<CustomerListItem[]>([]);
  readonly customersLoading = signal(false);
  readonly customersError = signal<string | null>(null);
  readonly selectedCustomerId = signal('');

  private readonly sectionsRaw = signal<CatalogSection[]>([]);
  readonly documentsLoading = signal(false);
  readonly documentsError = signal<string | null>(null);
  readonly selectedDocumentId = signal('');

  readonly composeLoading = signal(false);
  readonly composeError = signal<string | null>(null);
  private readonly deepLinkUrl = signal<string | null>(null);
  readonly message = signal('');

  readonly doneSteps = signal<Record<StepId, boolean>>({ 1: false, 2: false, 3: false });
  readonly downloading = signal(false);
  readonly downloadError = signal<string | null>(null);

  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);

  readonly customerOptions = computed<CustomerOption[]>(() =>
    this.customersRaw().map((c) => ({
      id: c.id,
      businessName: c.businessName,
      subline: `${c.name} · ${c.phone}`,
      serviceTypeId: c.serviceTypeId
    }))
  );

  readonly documentOptions = computed<DocumentOption[]>(() => {
    const rows: DocumentOption[] = [];
    for (const section of this.sectionsRaw()) {
      const doc = section.documents.find((d) => d.isLatest) ?? section.documents[0];
      if (!doc) continue;
      rows.push({
        documentId: doc.id,
        title: section.title,
        filename: doc.originalFilename,
        meta: `${doc.versionLabel ?? '—'} · ${formatFileSize(doc.sizeBytes)} · ${section.vendorName}`
      });
    }
    return rows;
  });

  private readonly selectedCustomerOption = computed(
    () => this.customerOptions().find((o) => o.id === this.selectedCustomerId()) ?? null
  );
  private readonly selectedDocumentOption = computed(
    () => this.documentOptions().find((o) => o.documentId === this.selectedDocumentId()) ?? null
  );

  readonly recipientBusinessName = computed(() => this.customerLock?.businessName ?? this.selectedCustomerOption()?.businessName ?? '—');
  readonly recipientSubline = computed(() => this.customerLock?.subline ?? this.selectedCustomerOption()?.subline ?? '—');
  readonly recipientSvc = computed(() => {
    if (this.customerLock) return this.styles.serviceType(this.customerLock.serviceTypeCode);
    const opt = this.selectedCustomerOption();
    const row = opt ? this.serviceTypeOptions().find((s) => s.id === opt.serviceTypeId) : undefined;
    return this.styles.serviceType(row?.code);
  });

  readonly documentTitle = computed(() => this.documentLock?.title ?? this.selectedDocumentOption()?.title ?? '—');
  readonly documentFilename = computed(() => this.documentLock?.filename ?? this.selectedDocumentOption()?.filename ?? '—');
  readonly documentMeta = computed(() => this.documentLock?.meta ?? this.selectedDocumentOption()?.meta ?? '—');

  readonly effectiveCustomerId = computed(() => this.customerLock?.id ?? this.selectedCustomerId());
  readonly effectiveDocumentId = computed(() => this.documentLock?.documentId ?? this.selectedDocumentId());
  readonly canCompose = computed(() => !!this.effectiveCustomerId() && !!this.effectiveDocumentId());
  readonly canOpenWhatsApp = computed(() => !!this.deepLinkUrl());

  readonly doneCount = computed(() => Object.values(this.doneSteps()).filter(Boolean).length);
  readonly statusText = computed(() => {
    const n = this.doneCount();
    return n === 0
      ? 'Dispatch not logged yet. Complete the three steps.'
      : `${n} of 3 steps done · logs against ${this.recipientBusinessName()}`;
  });

  readonly canLogDispatch = computed(
    () => this.canCompose() && !!this.message().trim() && !this.saving() && !this.composeLoading() && !this.composeError()
  );

  ngOnInit(): void {
    this.masterDataService.ensureLoaded().subscribe({ error: () => {} });

    if (!this.customerLock) {
      this.loadCustomers();
    }
    if (!this.documentLock) {
      this.loadDocuments();
    }
    if (this.canCompose()) {
      this.composeNow();
    }
  }

  onCustomerChange(id: string): void {
    this.selectedCustomerId.set(id);
    this.afterSelectionChanged();
  }

  onDocumentChange(id: string): void {
    this.selectedDocumentId.set(id);
    this.afterSelectionChanged();
  }

  onMessageInput(value: string): void {
    this.message.set(value);
  }

  retryCompose(): void {
    if (this.canCompose()) this.composeNow();
  }

  downloadStep(): void {
    if (this.downloading()) return;
    const docId = this.effectiveDocumentId();
    if (!docId) return;
    const filename = this.documentFilename();
    this.downloading.set(true);
    this.downloadError.set(null);
    this.catalogsService.downloadDocument(docId).subscribe({
      next: (blob) => {
        this.downloading.set(false);
        saveBlobAs(blob, filename);
        this.markStepDone(1);
      },
      error: (err: unknown) => {
        this.downloading.set(false);
        this.downloadError.set(extractErrorMessage(err, 'Could not download this PDF. Please try again.'));
      }
    });
  }

  openWhatsAppStep(): void {
    const url = this.deepLinkUrl();
    if (!url) return;
    window.open(url, '_blank');
    this.markStepDone(2);
  }

  markSentStep(): void {
    this.markStepDone(3);
  }

  cancel(): void {
    this.closed.emit();
  }

  logDispatch(): void {
    if (!this.canLogDispatch()) return;
    this.saving.set(true);
    this.saveError.set(null);
    const request: CreateDispatchLogRequest = {
      customerId: this.effectiveCustomerId(),
      catalogDocumentId: this.effectiveDocumentId(),
      message: this.message().trim()
    };
    this.dispatchService.create(request).subscribe({
      next: (dto) => {
        this.saving.set(false);
        this.logged.emit(dto);
        this.closed.emit();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.saveError.set(extractErrorMessage(err, 'Could not log this dispatch. Please try again.'));
      }
    });
  }

  private loadCustomers(): void {
    this.customersLoading.set(true);
    this.customersError.set(null);
    this.customersService.list({ page: 1, pageSize: 200 }).subscribe({
      next: (res) => {
        this.customersLoading.set(false);
        this.customersRaw.set(res.items);
        if (res.items.length && !this.selectedCustomerId()) {
          this.selectedCustomerId.set(res.items[0].id);
          this.afterSelectionChanged();
        }
      },
      error: (err: unknown) => {
        this.customersLoading.set(false);
        this.customersError.set(extractErrorMessage(err, 'Could not load customers. Please try again.'));
      }
    });
  }

  private loadDocuments(): void {
    this.documentsLoading.set(true);
    this.documentsError.set(null);
    this.catalogsService.list({ page: 1, pageSize: 100 }).subscribe({
      next: (res) => {
        this.documentsLoading.set(false);
        this.sectionsRaw.set(res.items);
        const first = this.documentOptions()[0];
        if (first && !this.selectedDocumentId()) {
          this.selectedDocumentId.set(first.documentId);
          this.afterSelectionChanged();
        }
      },
      error: (err: unknown) => {
        this.documentsLoading.set(false);
        this.documentsError.set(extractErrorMessage(err, 'Could not load catalog documents. Please try again.'));
      }
    });
  }

  private afterSelectionChanged(): void {
    this.doneSteps.set({ 1: false, 2: false, 3: false });
    this.saveError.set(null);
    if (this.canCompose()) {
      this.composeNow();
    } else {
      this.deepLinkUrl.set(null);
      this.message.set('');
    }
  }

  private composeNow(): void {
    this.composeLoading.set(true);
    this.composeError.set(null);
    this.dispatchService.compose(this.effectiveCustomerId(), this.effectiveDocumentId()).subscribe({
      next: (res) => {
        this.composeLoading.set(false);
        this.deepLinkUrl.set(res.deepLinkUrl);
        this.message.set(res.message);
      },
      error: (err: unknown) => {
        this.composeLoading.set(false);
        this.deepLinkUrl.set(null);
        this.composeError.set(extractErrorMessage(err, 'Could not prepare this dispatch. Please try again.'));
      }
    });
  }

  private markStepDone(step: StepId): void {
    this.doneSteps.update((s) => ({ ...s, [step]: true }));
  }
}
