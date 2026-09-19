import { Component, EventEmitter, Input, OnInit, Output, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MasterDataService } from '../../core/services/master-data.service';
import { extractErrorMessage } from '../../core/services/problem-details.util';
import { VendorsService } from '../../vendors/services/vendors.service';
import { CatalogsService } from '../services/catalogs.service';
import { CatalogSection, CreateCatalogSectionRequest, UpdateCatalogSectionRequest } from '../models/catalog.models';

export type UploadDialogMode = 'existing' | 'new';

interface VendorOption {
  id: string;
  name: string;
}

/**
 * Shared upload dialog (ACTION_PLAN E6-08) — ported from the prototype's
 * `uploadOpen` dialog (Source/Sourcing Ops Platform.dc.html ~line 1070) and
 * reused from three call sites: the Catalogs screen's "+ Upload Catalog PDF",
 * a vendor detail's "+ Upload Catalog PDF" (vendor-locked), and a catalog
 * card's "Edit" action (`editSection` set).
 *
 * DEVIATIONS FROM THE PROTOTYPE (flagged, see M4 report):
 *  - The prototype's "Catalog Section" field is a static `<option>`
 *    enumeration of section titles, because a static-data prototype has no
 *    live picklist to bind to. Against the real API a section must already
 *    exist before a document can be uploaded into it
 *    (`POST /catalog-sections/{id}/documents`), so this dialog adds an
 *    explicit "Add to existing section" / "Create new section" mode toggle
 *    the prototype doesn't show, rather than pretending the fixed picklist
 *    is dynamic.
 *  - The prototype labels its vendor field "Vendor (optional — editable
 *    later)" (~line 1091). The real, binding backend contract disagrees:
 *    `CatalogSectionDto.VendorId`/`VendorName` are non-nullable, and
 *    `UpdateCatalogSectionRequest` has no `VendorId` field at all — a vendor
 *    is required at creation and immutable afterwards. This dialog follows
 *    the real contract: the vendor picker is required (not defaultable to
 *    "Unassigned") in "create new section" mode, and is rendered read-only
 *    (never editable) in edit mode.
 *  - The prototype's cover-image drop zone is dropped entirely (no
 *    `coverImage` field anywhere in the schema/contract) — see
 *    `catalogs.component.ts`'s class doc for the cover-image decision.
 */
@Component({
  selector: 'app-catalog-upload-dialog',
  standalone: true,
  templateUrl: './upload-dialog.component.html',
  styleUrl: './upload-dialog.component.scss'
})
export class CatalogUploadDialogComponent implements OnInit {
  private readonly catalogsService = inject(CatalogsService);
  private readonly vendorsService = inject(VendorsService);
  private readonly masterDataService = inject(MasterDataService);

  /** Set when opened from a vendor's own detail screen — hides the vendor picker and scopes "existing section" lookups to this vendor. */
  @Input() vendorLock: { id: string; name: string } | null = null;
  /** Set when opened via a catalog card's "Edit" action — locks onto that section instead of offering existing/new mode. */
  @Input() editSection: CatalogSection | null = null;

  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<CatalogSection>();

  readonly categoryOptions = toSignal(this.masterDataService.categoryOptions(), { initialValue: [] });

  readonly isEdit = computed(() => !!this.editSection);
  readonly mode = signal<UploadDialogMode>('existing');

  readonly sectionsLoading = signal(false);
  readonly sectionsError = signal<string | null>(null);
  readonly sectionOptions = signal<CatalogSection[]>([]);
  readonly selectedSectionId = signal('');

  readonly vendorsLoading = signal(false);
  readonly vendorOptions = signal<VendorOption[]>([]);

  readonly titleInput = signal('');
  readonly categoryId = signal('');
  readonly tagsInput = signal('');
  /** Required in "create new section" mode; read-only display value in edit mode (the vendor is immutable once a section exists — see the class doc). */
  readonly vendorId = signal('');
  /** Display-only label for edit mode's read-only vendor row. */
  readonly lockedVendorName = signal('');

  readonly selectedFile = signal<File | null>(null);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  readonly dialogTitle = computed(() => (this.isEdit() ? 'Edit Catalog Section' : 'Upload Catalog PDF'));
  readonly saveCta = computed(() => {
    if (this.saving()) return 'Saving…';
    if (this.isEdit()) return 'Save Section';
    return this.mode() === 'new' ? 'Create & Upload' : 'Upload New Version';
  });

  ngOnInit(): void {
    this.masterDataService.ensureLoaded().subscribe({ error: () => {} });

    const section = this.editSection;
    if (section) {
      this.titleInput.set(section.title);
      this.categoryId.set(section.category.id);
      this.tagsInput.set(section.tags.join(', '));
      this.vendorId.set(section.vendorId);
      this.lockedVendorName.set(section.vendorName);
      this.selectedSectionId.set(section.id);
      // Vendor is immutable once a section exists (no vendorId on
      // UpdateCatalogSectionRequest) — no vendor list needs loading here.
      return;
    }

    if (this.vendorLock) {
      this.loadSections(this.vendorLock.id);
    } else {
      this.loadSections(undefined);
      this.loadVendors();
    }
  }

  setMode(mode: UploadDialogMode): void {
    this.mode.set(mode);
    this.error.set(null);
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    if (file && file.type !== 'application/pdf') {
      this.error.set('Only PDF files can be uploaded.');
      this.selectedFile.set(null);
      input.value = '';
      return;
    }
    this.error.set(null);
    this.selectedFile.set(file);
  }

  cancel(): void {
    this.closed.emit();
  }

  save(): void {
    if (this.saving()) return;
    if (this.isEdit()) {
      this.saveEdit();
    } else if (this.mode() === 'existing') {
      this.saveToExistingSection();
    } else {
      this.saveNewSection();
    }
  }

  private saveEdit(): void {
    const section = this.editSection;
    if (!section) return;
    const title = this.titleInput().trim();
    const categoryId = this.categoryId();
    if (!title || !categoryId) {
      this.error.set('Title and category are required.');
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    // No vendorId here — UpdateCatalogSectionRequest has no such field, the
    // vendor is immutable once the section exists (see class doc).
    const request: UpdateCatalogSectionRequest = {
      title,
      categoryId,
      tags: this.parseTags()
    };
    this.catalogsService.update(section.id, request).subscribe({
      next: (updated) => this.afterSectionWritten(updated, this.selectedFile()),
      error: (err: unknown) => {
        this.saving.set(false);
        this.error.set(extractErrorMessage(err, 'Could not save this catalog section. Please try again.'));
      }
    });
  }

  private saveToExistingSection(): void {
    const sectionId = this.selectedSectionId();
    const file = this.selectedFile();
    if (!sectionId) {
      this.error.set('Choose a catalog section.');
      return;
    }
    if (!file) {
      this.error.set('Choose a PDF file to upload.');
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.uploadTo(sectionId, file);
  }

  private saveNewSection(): void {
    const title = this.titleInput().trim();
    const categoryId = this.categoryId();
    const file = this.selectedFile();
    const vendorId = this.vendorLock ? this.vendorLock.id : this.vendorId();
    if (!title || !categoryId) {
      this.error.set('Title and category are required.');
      return;
    }
    if (!vendorId) {
      this.error.set('Choose a vendor — every catalog section belongs to exactly one vendor.');
      return;
    }
    if (!file) {
      this.error.set('Choose a PDF file to upload.');
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    const request: CreateCatalogSectionRequest = {
      vendorId,
      title,
      categoryId,
      tags: this.parseTags()
    };
    this.catalogsService.create(request).subscribe({
      // A brand-new section is removed again if its first upload fails, so a retry
      // doesn't leave an empty duplicate catalog behind.
      next: (section) => this.uploadTo(section.id, file, true),
      error: (err: unknown) => {
        this.saving.set(false);
        this.error.set(extractErrorMessage(err, 'Could not create this catalog section. Please try again.'));
      }
    });
  }

  private afterSectionWritten(section: CatalogSection, file: File | null): void {
    if (!file) {
      this.saving.set(false);
      this.saved.emit(section);
      return;
    }
    this.uploadTo(section.id, file);
  }

  private uploadTo(sectionId: string, file: File, rollbackSectionOnFailure = false): void {
    this.catalogsService.uploadDocument(sectionId, file).subscribe({
      next: () => {
        this.catalogsService.getById(sectionId).subscribe({
          next: (section) => {
            this.saving.set(false);
            this.saved.emit(section);
          },
          error: (err: unknown) => {
            this.saving.set(false);
            this.error.set(extractErrorMessage(err, 'Uploaded, but could not refresh the section. Please reload.'));
          }
        });
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.error.set(extractErrorMessage(err, 'Could not upload this document. Please try again.'));
        if (rollbackSectionOnFailure) {
          this.catalogsService.delete(sectionId).subscribe({ error: () => {} });
        }
      }
    });
  }

  private parseTags(): string[] {
    return this.tagsInput()
      .split(',')
      .map((t) => t.trim())
      .filter(Boolean);
  }

  private loadSections(vendorId: string | undefined): void {
    this.sectionsLoading.set(true);
    this.sectionsError.set(null);
    this.catalogsService.list({ vendorId, page: 1, pageSize: 100 }).subscribe({
      next: (res) => {
        this.sectionsLoading.set(false);
        this.sectionOptions.set(res.items);
        if (res.items.length === 0) {
          this.mode.set('new');
        } else {
          this.selectedSectionId.set(res.items[0].id);
        }
      },
      error: (err: unknown) => {
        this.sectionsLoading.set(false);
        this.sectionsError.set(extractErrorMessage(err, 'Could not load catalog sections. Please try again.'));
        this.mode.set('new');
      }
    });
  }

  private loadVendors(): void {
    this.vendorsLoading.set(true);
    this.vendorsService.list({ page: 1, pageSize: 200 }).subscribe({
      next: (res) => {
        this.vendorsLoading.set(false);
        this.vendorOptions.set(res.items.map((v) => ({ id: v.id, name: v.name })));
      },
      error: () => this.vendorsLoading.set(false)
    });
  }
}
