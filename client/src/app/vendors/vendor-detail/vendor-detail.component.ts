import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { extractErrorMessage } from '../../core/services/problem-details.util';
import { StatusStyleService } from '../../shared/services/status-style.service';
import { formatDateOnly } from '../../shared/utils/date-format.util';
import { formatFileSize } from '../../shared/utils/file-size.util';
import { previewBlob } from '../../shared/utils/file-download.util';
import { CatalogsService } from '../../catalogs/services/catalogs.service';
import { CatalogUploadDialogComponent } from '../../catalogs/upload-dialog/upload-dialog.component';
import { CatalogSection } from '../../catalogs/models/catalog.models';
import { VendorsService } from '../services/vendors.service';
import { VendorDetail } from '../models/vendor.models';
import { VendorFormDialogComponent } from '../vendor-form-dialog/vendor-form-dialog.component';

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
}

interface SectionRow {
  id: string;
  title: string;
  metaLabel: string;
  tags: string[];
  docs: DocRow[];
}

/**
 * Vendor detail (ACTION_PLAN E5-09) — ported from Source/Sourcing Ops
 * Platform.dc.html `showVendorDetail` (~line 546). `GET /vendors/{id}`
 * embeds `catalogSections[]`/`documents[]` directly (E5-05) so this is a
 * single load, no `forkJoin`. "Send via WhatsApp" is ported as a
 * visually-present but inert control, same pattern `customer-detail`
 * already uses — dispatch wiring is E9 and explicitly out of scope here.
 */
@Component({
  selector: 'app-vendor-detail',
  standalone: true,
  imports: [RouterLink, VendorFormDialogComponent, CatalogUploadDialogComponent],
  templateUrl: './vendor-detail.component.html',
  styleUrl: './vendor-detail.component.scss'
})
export class VendorDetailComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly vendorsService = inject(VendorsService);
  private readonly catalogsService = inject(CatalogsService);
  private readonly styles = inject(StatusStyleService);
  private readonly auth = inject(AuthService);

  readonly canEditVendor = computed(() => this.auth.hasPermission('Vendors.Edit'));
  readonly canEditCatalogs = computed(() => this.auth.hasPermission('Catalogs.Edit'));

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly vendor = signal<VendorDetail | null>(null);

  readonly editOpen = signal(false);
  readonly uploadOpen = signal(false);

  readonly previewingDocId = signal<string | null>(null);
  readonly previewError = signal<string | null>(null);

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

  constructor() {
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
        uploadedLabel: `${formatDateOnly(d.uploadedAt)} · ${d.uploadedByName}`
      }))
    };
  }
}
