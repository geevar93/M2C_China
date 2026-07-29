import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { AuthService } from '../core/services/auth.service';
import { MasterDataService } from '../core/services/master-data.service';
import { extractErrorMessage } from '../core/services/problem-details.util';
import { previewBlob } from '../shared/utils/file-download.util';
import { formatFileSize } from '../shared/utils/file-size.util';
import { formatDateOnly } from '../shared/utils/date-format.util';
import { CatalogsService } from './services/catalogs.service';
import { CatalogSection } from './models/catalog.models';
import { CatalogUploadDialogComponent } from './upload-dialog/upload-dialog.component';
import { DispatchDialogComponent, DispatchDocumentLock } from '../dispatch/dispatch-dialog/dispatch-dialog.component';
import { DispatchService } from '../dispatch/services/dispatch.service';

interface CatalogCard {
  id: string;
  title: string;
  vendorId: string;
  vendorName: string;
  categoryName: string;
  docCountLabel: string;
  latestFileName: string;
  latestMeta: string;
  latestDocId: string | null;
  sentMeta: string;
  section: CatalogSection;
}

const PAGE_SIZE = 24;
const ALL = '';

/**
 * Catalogs cross-vendor browse (ACTION_PLAN E6-08) — ported from Source/
 * Sourcing Ops Platform.dc.html `showCatalogs` (~line 615). Search + category
 * filter call `GET /catalog-sections` (E6-05).
 *
 * COVER IMAGE DECISION (flagged, see M4 report): the prototype's
 * `k.coverImg` is a static placeholder-image array with no backing schema
 * field anywhere in TECH_SPEC §6 or the API contract. Rather than inventing
 * one or silently dropping the image slot, each card keeps the same
 * `140px` image-container box from the prototype's DOM but renders a
 * neutral, token-built placeholder (`.catalog-cover-placeholder`, built only
 * from existing colour/spacing tokens) instead of a photo — so the layout is
 * unchanged and nothing is invented.
 *
 * The prototype's per-card "Sent to N customers · last …" footer and ➤
 * dispatch button are wired in this pass (E9-05/E9-07): the footer now comes
 * from `GET /catalog-documents/{id}/dispatches`, fetched per-card once its
 * latest document id is known (a card whose fetch fails shows "Sent history
 * unavailable" rather than blocking the rest of the grid). The ➤ button
 * opens the shared `DispatchDialogComponent` with the card's latest document
 * fixed (`documentLock`) so the dialog only needs a customer picked.
 */
@Component({
  selector: 'app-catalogs',
  standalone: true,
  imports: [RouterLink, CatalogUploadDialogComponent, DispatchDialogComponent],
  templateUrl: './catalogs.component.html',
  styleUrl: './catalogs.component.scss'
})
export class CatalogsComponent {
  private readonly catalogsService = inject(CatalogsService);
  private readonly masterDataService = inject(MasterDataService);
  private readonly dispatchService = inject(DispatchService);
  private readonly auth = inject(AuthService);

  readonly categoryOptions = toSignal(this.masterDataService.categoryOptions(), { initialValue: [] });
  readonly canEdit = computed(() => this.auth.hasPermission('Catalogs.Edit'));
  readonly canDispatch = computed(() => this.auth.hasPermission('Dispatch.Send'));

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  private readonly items = signal<CatalogSection[]>([]);
  readonly totalCount = signal(0);

  readonly search = signal('');
  readonly categoryId = signal(ALL);
  readonly page = signal(1);

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)));

  readonly cards = computed<CatalogCard[]>(() => this.items().map((s) => this.toCard(s)));
  readonly noResults = computed(() => !this.loading() && !this.error() && this.cards().length === 0);

  readonly previewingDocId = signal<string | null>(null);
  readonly previewError = signal<string | null>(null);

  readonly uploadOpen = signal(false);
  readonly editSection = signal<CatalogSection | null>(null);

  readonly dispatchOpen = signal(false);
  readonly dispatchDocumentLock = signal<DispatchDocumentLock | null>(null);
  private readonly sentMetaByDocId = signal<Record<string, string>>({});

  private readonly search$ = new Subject<string>();

  constructor() {
    this.masterDataService.ensureLoaded().subscribe({ error: () => {} });

    this.search$.pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed()).subscribe((value) => {
      this.search.set(value);
      this.page.set(1);
      this.fetch();
    });

    this.fetch();
  }

  onSearchInput(value: string): void {
    this.search$.next(value);
  }

  setCategory(value: string): void {
    this.categoryId.set(value);
    this.page.set(1);
    this.fetch();
  }

  prevPage(): void {
    if (this.page() <= 1) return;
    this.page.update((p) => p - 1);
    this.fetch();
  }

  nextPage(): void {
    if (this.page() >= this.totalPages()) return;
    this.page.update((p) => p + 1);
    this.fetch();
  }

  retry(): void {
    this.fetch();
  }

  openUpload(): void {
    this.editSection.set(null);
    this.uploadOpen.set(true);
  }

  openEdit(section: CatalogSection): void {
    this.editSection.set(section);
    this.uploadOpen.set(true);
  }

  cancelUpload(): void {
    this.uploadOpen.set(false);
    this.editSection.set(null);
  }

  onUploadSaved(_section: CatalogSection): void {
    this.uploadOpen.set(false);
    this.editSection.set(null);
    this.fetch();
  }

  openDispatch(card: CatalogCard): void {
    if (!card.latestDocId) return;
    this.dispatchDocumentLock.set({
      documentId: card.latestDocId,
      title: card.title,
      filename: card.latestFileName,
      meta: `${card.latestMeta} · ${card.vendorName}`
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
    if (docId) this.loadSentMeta(docId);
  }

  previewDocument(docId: string | null): void {
    if (!docId || this.previewingDocId()) return;
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

  private fetch(): void {
    this.loading.set(true);
    this.error.set(null);
    this.catalogsService
      .list({
        search: this.search() || undefined,
        page: this.page(),
        pageSize: PAGE_SIZE,
        categoryId: this.categoryId() || undefined
      })
      .subscribe({
        next: (res) => {
          this.items.set(res.items);
          this.totalCount.set(res.totalCount);
          this.loading.set(false);
          for (const section of res.items) {
            const latest = section.documents.find((d) => d.isLatest) ?? section.documents[0];
            if (latest) this.loadSentMeta(latest.id);
          }
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.error.set(extractErrorMessage(err, 'Could not load catalogs. Please try again.'));
        }
      });
  }

  /** E9-07: "Sent to N customers · last DATE", derived from the dispatch log — one card's fetch failing never blocks the rest of the grid. */
  private loadSentMeta(documentId: string): void {
    this.dispatchService.history(documentId).subscribe({
      next: (entries) => {
        const label =
          entries.length === 0
            ? 'Not sent yet'
            : `Sent to ${new Set(entries.map((e) => e.customerId)).size} customer${
                new Set(entries.map((e) => e.customerId)).size === 1 ? '' : 's'
              } · last ${formatDateOnly(entries[0].sentAtUtc)}`;
        this.sentMetaByDocId.update((m) => ({ ...m, [documentId]: label }));
      },
      error: () => this.sentMetaByDocId.update((m) => ({ ...m, [documentId]: 'Sent history unavailable' }))
    });
  }

  private toCard(section: CatalogSection): CatalogCard {
    const latest = section.documents.find((d) => d.isLatest) ?? section.documents[0] ?? null;
    const count = section.documents.length;
    const sentMeta = latest ? (this.sentMetaByDocId()[latest.id] ?? 'Loading dispatch history…') : 'No documents yet';
    return {
      id: section.id,
      title: section.title,
      vendorId: section.vendorId,
      vendorName: section.vendorName,
      categoryName: section.category.name,
      docCountLabel: `${count} doc${count === 1 ? '' : 's'}`,
      latestFileName: latest?.originalFilename ?? 'No documents yet',
      latestMeta: latest ? `${formatFileSize(latest.sizeBytes)} · ${formatDateOnly(latest.uploadedAt)}` : '—',
      latestDocId: latest?.id ?? null,
      sentMeta,
      section
    };
  }
}
