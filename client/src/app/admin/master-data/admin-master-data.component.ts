import { LowerCasePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { extractErrorMessage } from '../../core/services/problem-details.util';
import { AdminMasterDataService } from '../services/admin-master-data.service';
import {
  COLLECTION_KEYS,
  COLLECTION_LABELS,
  DOCUMENT_TYPE_SCOPES,
  CategoryRow,
  DocumentTypeScope,
  LookupRow,
  MasterDataAggregate,
  MasterDataCollectionKey,
  MasterDataRow,
  ReorderItem,
  UpsertMasterDataRequest,
  isCategoryCollection
} from '../models/admin-master-data.models';

/**
 * Master data configuration (ACTION_PLAN E0-04b), against the existing M2
 * `MasterDataController` — no backend change. docs/SCREEN_DESIGNS.md's
 * E0-04b section is binding. Two asymmetries are rendered honestly rather
 * than normalised away, per the design's explicit instruction:
 *  - `categories` rows carry `name`; every other collection carries
 *    `code`/`label` — the form and the list column switch shape by tab.
 *  - `code` is shown but disabled on edit (D-12: `StatusStyleService` keys
 *    colours by `code`, so a mutable code would silently break colours
 *    app-wide) — the field-hint explains that the label is what to rename.
 *
 * The `documentTypes` tab additionally carries `scope` (D-34/D-44, open item
 * N-20b). It is required on create and read-only on edit, for the same reason
 * `code` is: re-scoping a type that documents already reference would move
 * those documents into the other module's dropdown. Without this selector the
 * server's "Vendor" default applied silently and a Super Admin could not
 * create a type that backs a shipment upload at all — the FSD §3.3
 * configurability violation DR-6 exists to catch, and the second time this one
 * screen has hit it (D-26 was the first).
 *
 * Reorder is Move-up/Move-down, not drag-and-drop (no new library — C1 /
 * TECH_SPEC §11 forbid one, and buttons are also what actually works at
 * phone width, E12-01).
 */
@Component({
  selector: 'app-admin-master-data',
  standalone: true,
  imports: [LowerCasePipe],
  templateUrl: './admin-master-data.component.html',
  styleUrl: './admin-master-data.component.scss'
})
export class AdminMasterDataComponent {
  private readonly service = inject(AdminMasterDataService);

  readonly collectionKeys = COLLECTION_KEYS;
  readonly collectionLabels = COLLECTION_LABELS;

  readonly activeCollection = signal<MasterDataCollectionKey>('categories');
  readonly includeRetired = signal(false);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly actionError = signal<string | null>(null);
  private readonly aggregate = signal<MasterDataAggregate | null>(null);

  readonly isCategoryTab = computed(() => isCategoryCollection(this.activeCollection()));

  /** Only `documentTypes` carries a scope — every other tab hides the field entirely. */
  readonly isDocumentTypeTab = computed(() => this.activeCollection() === 'documentTypes');

  readonly documentTypeScopes = DOCUMENT_TYPE_SCOPES;

  /** Rows of the active collection, filtered by "Show retired" and sorted by
   * `sortOrder`. Retired rows are never silently hidden — dimmed via
   * `.row-inactive` with an "Inactive" chip when the toggle is on. */
  readonly rows = computed<MasterDataRow[]>(() => {
    const agg = this.aggregate();
    if (!agg) return [];
    const all = agg[this.activeCollection()] as MasterDataRow[];
    const visible = this.includeRetired() ? all : all.filter((r) => r.isActive);
    return [...visible].sort((a, b) => a.sortOrder - b.sortOrder);
  });

  readonly noResults = computed(() => !this.loading() && !this.error() && this.rows().length === 0);

  private readonly reorderingId = signal<string | null>(null);
  readonly togglingId = signal<string | null>(null);
  readonly deletingId = signal<string | null>(null);

  // ---- Create/rename form modal ----
  readonly formOpen = signal(false);
  readonly formMode = signal<'create' | 'edit'>('create');
  private readonly formEditingRow = signal<MasterDataRow | null>(null);
  readonly formName = signal('');
  readonly formCode = signal('');
  readonly formLabel = signal('');
  /** `documentTypes` only. Starts empty so create forces a deliberate choice
   * rather than inheriting the server's "Vendor" default silently (N-20b). */
  readonly formScope = signal<DocumentTypeScope | ''>('');
  readonly formSaving = signal(false);
  readonly formError = signal<string | null>(null);

  constructor() {
    this.fetch();
  }

  selectTab(key: MasterDataCollectionKey): void {
    this.activeCollection.set(key);
    this.actionError.set(null);
  }

  toggleIncludeRetired(): void {
    this.includeRetired.update((v) => !v);
  }

  retry(): void {
    this.fetch();
  }

  categoryName(row: MasterDataRow): string {
    return (row as CategoryRow).name;
  }

  lookupCode(row: MasterDataRow): string {
    return (row as LookupRow).code;
  }

  lookupLabel(row: MasterDataRow): string {
    return (row as LookupRow).label;
  }

  /** Empty for every collection but `documentTypes` — the list column renders nothing rather than a placeholder. */
  lookupScope(row: MasterDataRow): string {
    return (row as LookupRow).scope ?? '';
  }

  // ---- Reorder (Move up/down buttons — see class doc for why not drag-and-drop) ----

  canMoveUp(row: MasterDataRow): boolean {
    if (!row.isActive) return false;
    const list = this.activeRowsSorted();
    return list.findIndex((r) => r.id === row.id) > 0;
  }

  canMoveDown(row: MasterDataRow): boolean {
    if (!row.isActive) return false;
    const list = this.activeRowsSorted();
    const idx = list.findIndex((r) => r.id === row.id);
    return idx >= 0 && idx < list.length - 1;
  }

  moveUp(row: MasterDataRow): void {
    this.swap(row, -1);
  }

  moveDown(row: MasterDataRow): void {
    this.swap(row, 1);
  }

  private activeRowsSorted(): MasterDataRow[] {
    const agg = this.aggregate();
    if (!agg) return [];
    const all = agg[this.activeCollection()] as MasterDataRow[];
    return [...all].filter((r) => r.isActive).sort((a, b) => a.sortOrder - b.sortOrder);
  }

  private swap(row: MasterDataRow, direction: -1 | 1): void {
    if (this.reorderingId()) return;
    const list = this.activeRowsSorted();
    const idx = list.findIndex((r) => r.id === row.id);
    const otherIdx = idx + direction;
    if (idx < 0 || otherIdx < 0 || otherIdx >= list.length) return;
    const a = list[idx];
    const b = list[otherIdx];
    const items: ReorderItem[] = [
      { id: a.id, sortOrder: b.sortOrder },
      { id: b.id, sortOrder: a.sortOrder }
    ];
    this.reorderingId.set(row.id);
    this.actionError.set(null);
    this.service.reorder(this.activeCollection(), items).subscribe({
      next: () => {
        this.reorderingId.set(null);
        this.fetch();
      },
      error: (err: unknown) => {
        this.reorderingId.set(null);
        this.actionError.set(extractErrorMessage(err, 'Could not reorder this row. Please try again.'));
      }
    });
  }

  // ---- Retire / Restore ----

  retireRow(row: MasterDataRow): void {
    if (this.togglingId()) return;
    this.togglingId.set(row.id);
    this.actionError.set(null);
    this.service.retire(this.activeCollection(), row.id).subscribe({
      next: () => {
        this.togglingId.set(null);
        this.fetch();
      },
      error: (err: unknown) => {
        this.togglingId.set(null);
        this.actionError.set(extractErrorMessage(err, 'Could not retire this row. Please try again.'));
      }
    });
  }

  restoreRow(row: MasterDataRow): void {
    if (this.togglingId()) return;
    this.togglingId.set(row.id);
    this.actionError.set(null);
    this.service.restore(this.activeCollection(), row.id).subscribe({
      next: () => {
        this.togglingId.set(null);
        this.fetch();
      },
      error: (err: unknown) => {
        this.togglingId.set(null);
        this.actionError.set(extractErrorMessage(err, 'Could not restore this row. Please try again.'));
      }
    });
  }

  // ---- Delete (offered, but expected to 409 for referenced rows; disabled
  // up front for isSystemDefault rows rather than offered and then refused) ----

  deleteRow(row: MasterDataRow): void {
    if (row.isSystemDefault || this.deletingId()) return;
    this.deletingId.set(row.id);
    this.actionError.set(null);
    this.service.delete(this.activeCollection(), row.id).subscribe({
      next: () => {
        this.deletingId.set(null);
        this.fetch();
      },
      error: (err: unknown) => {
        this.deletingId.set(null);
        // E3-08's 409 detail reads "...Retire it instead of deleting." —
        // surfaced verbatim, the server's reasoning is the useful part.
        this.actionError.set(extractErrorMessage(err, 'Could not delete this row. Please try again.'));
      }
    });
  }

  // ---- Create / rename form ----

  openCreateForm(): void {
    this.formMode.set('create');
    this.formEditingRow.set(null);
    this.formName.set('');
    this.formCode.set('');
    this.formLabel.set('');
    this.formScope.set('');
    this.formError.set(null);
    this.formOpen.set(true);
  }

  openEditForm(row: MasterDataRow): void {
    this.formMode.set('edit');
    this.formEditingRow.set(row);
    this.formError.set(null);
    if (this.isCategoryTab()) {
      this.formName.set(this.categoryName(row));
      this.formCode.set('');
      this.formLabel.set('');
      this.formScope.set('');
    } else {
      this.formName.set('');
      this.formCode.set(this.lookupCode(row));
      this.formLabel.set(this.lookupLabel(row));
      // Shown disabled on edit alongside `code` — the server ignores `scope` on
      // PUT, so an editable field here would look like it worked and not.
      this.formScope.set((this.lookupScope(row) as DocumentTypeScope) || '');
    }
    this.formOpen.set(true);
  }

  cancelForm(): void {
    this.formOpen.set(false);
  }

  submitForm(): void {
    if (this.formSaving()) return;
    const key = this.activeCollection();
    const isCategory = this.isCategoryTab();

    if (isCategory) {
      const name = this.formName().trim();
      if (!name) {
        this.formError.set('Name is required.');
        return;
      }
      this.saveForm(key, { name });
      return;
    }

    const code = this.formCode().trim();
    const label = this.formLabel().trim();
    if (!code || !label) {
      this.formError.set('Code and label are required.');
      return;
    }

    // Scope is create-only (the server ignores it on PUT, D-34), and required
    // rather than defaulted: a type created as "Vendor" by accident can never
    // back a shipment upload and cannot be re-scoped afterwards.
    if (this.isDocumentTypeTab() && this.formMode() === 'create') {
      const scope = this.formScope();
      if (!scope) {
        this.formError.set('Scope is required — choose whether this type is for vendor or shipment documents.');
        return;
      }
      this.saveForm(key, { code, label, scope });
      return;
    }

    this.saveForm(key, { code, label });
  }

  private saveForm(key: MasterDataCollectionKey, request: UpsertMasterDataRequest): void {
    this.formSaving.set(true);
    this.formError.set(null);
    const editing = this.formEditingRow();
    const obs = editing ? this.service.update(key, editing.id, request) : this.service.create(key, request);
    obs.subscribe({
      next: () => {
        this.formSaving.set(false);
        this.formOpen.set(false);
        this.fetch();
      },
      error: (err: unknown) => {
        this.formSaving.set(false);
        this.formError.set(extractErrorMessage(err, 'Could not save this row. Please try again.'));
      }
    });
  }

  private fetch(): void {
    this.loading.set(true);
    this.error.set(null);
    // Always loads with includeRetired=true — "Show retired" is a client-side
    // filter over one cached payload, matching core MasterDataService's
    // retired-rows-are-cached-not-refetched approach.
    this.service.getAggregate(true).subscribe({
      next: (data) => {
        this.aggregate.set(data);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.error.set(extractErrorMessage(err, 'Could not load master data. Please try again.'));
      }
    });
  }
}
