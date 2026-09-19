import { Component, EventEmitter, Input, OnInit, Output, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { Subject, switchMap } from 'rxjs';
import { SearchSelectComponent, SearchSelectOption } from '../../shared/components/search-select/search-select.component';
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
import { CreateDispatchLogRequest, DispatchLogDto, DocumentShareLink } from '../models/dispatch.models';

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

type StepId = 1 | 2;

/** Matches shown per search in the recipient / catalog pickers; type to narrow further. */
const PICKER_PAGE_SIZE = 20;

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
 * STEP-GATING DECISION (flagged): the step tiles are advisory progress
 * markers, not a wizard — completing them is never required to enable "Log
 * Dispatch". The prototype's own footer status text ("Dispatch not logged
 * yet." vs "N of N steps done") describes the steps as informational, and
 * nothing in the prototype's `logDispatch` handler checks `waSteps` before
 * logging. `canLogDispatch()` below only requires a valid customer/document
 * pair, a non-empty message, and no in-flight/broken compose call.
 *
 * E9-10 — THREE STEPS BECAME TWO. The prototype's step 1 existed only because
 * a `wa.me` deep link cannot attach a file, so staff had to download the PDF
 * and attach it by hand inside WhatsApp. Compose now returns a temporary
 * public link to the document and the server has already substituted it into
 * the message, so the recipient opens the PDF straight from the chat. What is
 * left is: open WhatsApp (link pre-filled), then send. The download is kept
 * as a secondary action rather than deleted — staff genuinely want the file
 * on their own device sometimes, and it is the fallback if a recipient cannot
 * open links — but it is no longer a step, because nothing downstream depends
 * on it. This is a deliberate, recorded divergence from the ported prototype:
 * the prototype describes a constraint that no longer exists.
 */
@Component({
  selector: 'app-dispatch-dialog',
  standalone: true,
  imports: [SearchSelectComponent],
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

  /** Results of the current recipient search (server-side, first PICKER_PAGE_SIZE matches). */
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

  /** E9-10: the minted public link for the current selection. Null until compose succeeds. */
  readonly shareLink = signal<DocumentShareLink | null>(null);

  readonly doneSteps = signal<Record<StepId, boolean>>({ 1: false, 2: false });
  readonly downloading = signal(false);
  readonly downloadError = signal<string | null>(null);

  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);

  readonly customerOptions = computed<CustomerOption[]>(() =>
    this.customersRaw().map((c) => ({
      id: c.id,
      businessName: c.businessName || c.name,
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

  /** The chosen rows are kept separately, so their details survive a later search that no longer contains them. */
  private readonly pickedCustomer = signal<CustomerOption | null>(null);
  private readonly pickedDocument = signal<DocumentOption | null>(null);

  private readonly selectedCustomerOption = computed(() => this.pickedCustomer());
  private readonly selectedDocumentOption = computed(() => this.pickedDocument());

  readonly customerPickerOptions = computed<SearchSelectOption[]>(() =>
    this.customerOptions().map((c) => ({ value: c.id, label: c.businessName, sublabel: c.subline }))
  );
  readonly documentPickerOptions = computed<SearchSelectOption[]>(() =>
    this.documentOptions().map((d) => ({ value: d.documentId, label: d.title, sublabel: `${d.filename} · ${d.meta}` }))
  );

  private readonly customerSearch$ = new Subject<string>();
  private readonly documentSearch$ = new Subject<string>();
  /** Set once the unfiltered first page is back — only then is "there are none at all" knowable. */
  readonly customersEverLoaded = signal(false);
  readonly documentsEverLoaded = signal(false);
  readonly hasAnyCustomers = signal(false);
  readonly hasAnyDocuments = signal(false);

  constructor() {
    // switchMap drops a slower, older search response instead of letting it overwrite a newer one.
    this.customerSearch$
      .pipe(
        switchMap((term) => {
          this.customersLoading.set(true);
          this.customersError.set(null);
          return this.customersService.list({ search: term || undefined, page: 1, pageSize: PICKER_PAGE_SIZE });
        }),
        takeUntilDestroyed()
      )
      .subscribe({
        next: (res) => this.onCustomersLoaded(res.items, res.totalCount),
        error: (err: unknown) => {
          this.customersLoading.set(false);
          this.customersError.set(extractErrorMessage(err, 'Could not load customers. Please try again.'));
        }
      });
    this.documentSearch$
      .pipe(
        switchMap((term) => {
          this.documentsLoading.set(true);
          this.documentsError.set(null);
          return this.catalogsService.list({ search: term || undefined, page: 1, pageSize: PICKER_PAGE_SIZE });
        }),
        takeUntilDestroyed()
      )
      .subscribe({
        next: (res) => this.onSectionsLoaded(res.items),
        error: (err: unknown) => {
          this.documentsLoading.set(false);
          this.documentsError.set(extractErrorMessage(err, 'Could not load catalog documents. Please try again.'));
        }
      });
  }

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
      ? 'Dispatch not logged yet. Open WhatsApp, send, then log it.'
      : `${n} of 2 steps done · logs against ${this.recipientBusinessName()}`;
  });

  /**
   * E9-10: how long the recipient has, shown next to the message so the staff
   * member can say so in the chat if they want. Formatted from the server's
   * timestamp — the client never computes the expiry itself, because the
   * server's clock is the one the link is actually checked against.
   */
  readonly shareLinkExpiryText = computed(() => {
    const link = this.shareLink();
    if (!link) return '';
    const expires = new Date(link.expiresAtUtc);
    return `Link expires ${expires.toLocaleString(undefined, {
      weekday: 'short',
      hour: 'numeric',
      minute: '2-digit',
      day: 'numeric',
      month: 'short'
    })}`;
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
    this.pickedCustomer.set(this.customerOptions().find((o) => o.id === id) ?? null);
    this.selectedCustomerId.set(id);
    this.afterSelectionChanged();
  }

  onDocumentChange(id: string): void {
    this.pickedDocument.set(this.documentOptions().find((o) => o.documentId === id) ?? null);
    this.selectedDocumentId.set(id);
    this.afterSelectionChanged();
  }

  searchCustomers(term: string): void {
    this.customerSearch$.next(term);
  }

  searchDocuments(term: string): void {
    this.documentSearch$.next(term);
  }

  onMessageInput(value: string): void {
    this.message.set(value);
  }

  retryCompose(): void {
    if (this.canCompose()) this.composeNow();
  }

  /**
   * E9-10: no longer a step. Downloads the PDF through the **authenticated**
   * endpoint for the staff member's own device — deliberately not through the
   * public share link, which exists for the recipient and would be a strictly
   * weaker path for a caller who already holds a session.
   */
  downloadForMyself(): void {
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
    this.markStepDone(1);
  }

  markSentStep(): void {
    this.markStepDone(2);
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
    this.customerSearch$.next('');
  }

  private loadDocuments(): void {
    this.documentSearch$.next('');
  }

  private onCustomersLoaded(items: CustomerListItem[], totalCount: number): void {
    this.customersLoading.set(false);
    this.customersRaw.set(items);
    if (!this.customersEverLoaded()) {
      this.customersEverLoaded.set(true);
      this.hasAnyCustomers.set(totalCount > 0);
      // Same default as before: preselect the first customer on open.
      if (items.length && !this.selectedCustomerId()) this.onCustomerChange(items[0].id);
    }
  }

  private onSectionsLoaded(sections: CatalogSection[]): void {
    this.documentsLoading.set(false);
    this.sectionsRaw.set(sections);
    if (!this.documentsEverLoaded()) {
      this.documentsEverLoaded.set(true);
      this.hasAnyDocuments.set(this.documentOptions().length > 0);
      const first = this.documentOptions()[0];
      if (first && !this.selectedDocumentId()) this.onDocumentChange(first.documentId);
    }
  }

  private afterSelectionChanged(): void {
    this.doneSteps.set({ 1: false, 2: false });
    this.saveError.set(null);
    if (this.canCompose()) {
      this.composeNow();
    } else {
      this.deepLinkUrl.set(null);
      this.shareLink.set(null);
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
        this.shareLink.set(res.shareLink ?? null);
        this.message.set(res.message);
      },
      error: (err: unknown) => {
        this.composeLoading.set(false);
        this.deepLinkUrl.set(null);
        this.shareLink.set(null);
        this.composeError.set(extractErrorMessage(err, 'Could not prepare this dispatch. Please try again.'));
      }
    });
  }

  private markStepDone(step: StepId): void {
    this.doneSteps.update((s) => ({ ...s, [step]: true }));
  }
}
