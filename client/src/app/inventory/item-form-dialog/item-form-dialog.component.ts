import { Component, EventEmitter, Input, OnInit, Output, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MasterDataService } from '../../core/services/master-data.service';
import { extractErrorMessage } from '../../core/services/problem-details.util';
import { VendorsService } from '../../vendors/services/vendors.service';
import { VendorListItem } from '../../vendors/models/vendor.models';
import { InventoryService } from '../services/inventory.service';
import { CreateInventoryItemRequest, InventoryItem, UpdateInventoryItemRequest } from '../models/inventory.models';
import { formatQty } from '../utils/format.util';

/**
 * Shared inventory item create/edit dialog (ACTION_PLAN E7-01). There is no
 * prototype precedent for this dialog — the approved mock's inventory screen
 * has no wired add/edit action (its buttons are static, per
 * `Source/Sourcing Ops Platform.dc.html` ~line 670-748) — so this follows
 * `VendorFormDialogComponent`'s house pattern (dialog atoms, `field`-atom
 * inputs, create-vs-edit driven by whether `item` is set) instead.
 *
 * CRITICAL (D-42, §15.3): `UpdateInventoryItemRequest` has no `onHandQty` —
 * the API silently ignores one if sent. The edit form must therefore render
 * on-hand as **read-only text**, never an editable input; only the create
 * form may set an opening balance. See the template's `@if (!isEdit())`
 * branch — there is deliberately no `<input>` bound to on-hand in the other
 * branch.
 */
@Component({
  selector: 'app-inventory-item-form-dialog',
  standalone: true,
  templateUrl: './item-form-dialog.component.html',
  styleUrl: './item-form-dialog.component.scss'
})
export class ItemFormDialogComponent implements OnInit {
  private readonly inventoryService = inject(InventoryService);
  private readonly masterDataService = inject(MasterDataService);
  private readonly vendorsService = inject(VendorsService);

  @Input() item: InventoryItem | null = null;
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<InventoryItem>();

  readonly categoryOptions = toSignal(this.masterDataService.categoryOptions(), { initialValue: [] });

  /**
   * First 100 vendors by the API's default sort, for the optional "Source
   * Vendor" picker. There is no `MasterDataService` accessor for vendors —
   * vendors are a full CRUD entity, not a lookup collection — so this fetches
   * one page directly via `VendorsService`, same as the vendors screen's own
   * list call. A business with more than 100 vendors would need a searchable
   * combobox instead of this plain `<select>`; not built here since nothing
   * in the current data suggests that scale yet.
   */
  readonly vendorOptions = signal<VendorListItem[]>([]);

  readonly isEdit = computed(() => !!this.item);
  readonly dialogTitle = computed(() => (this.isEdit() ? 'Edit Item' : 'Add Item'));
  readonly onHandDisplay = computed(() => (this.item ? `${formatQty(this.item.onHandQty)} ${this.item.unit}` : ''));

  readonly name = signal('');
  readonly sku = signal('');
  readonly description = signal('');
  readonly categoryId = signal('');
  readonly vendorId = signal('');
  readonly unit = signal('');
  readonly reorderThreshold = signal('');
  readonly unitCost = signal('');
  /** Opening balance — create mode only (D-42). */
  readonly onHandQty = signal('');

  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.masterDataService.ensureLoaded().subscribe({ error: () => {} });
    this.vendorsService.list({ page: 1, pageSize: 100 }).subscribe({
      next: (res) => this.vendorOptions.set(res.items),
      error: () => {}
    });

    const it = this.item;
    if (!it) return;
    this.name.set(it.name);
    this.sku.set(it.sku ?? '');
    this.description.set(it.description ?? '');
    this.categoryId.set(it.category.id);
    this.vendorId.set(it.vendor?.id ?? '');
    this.unit.set(it.unit);
    this.reorderThreshold.set(String(it.reorderThreshold));
    this.unitCost.set(it.unitCost != null ? String(it.unitCost) : '');
  }

  cancel(): void {
    this.closed.emit();
  }

  save(): void {
    if (this.saving()) return;
    const name = this.name().trim();
    const categoryId = this.categoryId();
    const unit = this.unit().trim();
    const reorderText = this.reorderThreshold().trim();
    if (!name || !categoryId || !unit || !reorderText) {
      this.error.set('Name, category, unit and reorder threshold are required.');
      return;
    }

    const reorderThreshold = Number(reorderText);
    if (Number.isNaN(reorderThreshold) || reorderThreshold < 0) {
      this.error.set('Reorder threshold must be a non-negative number.');
      return;
    }

    const unitCostText = this.unitCost().trim();
    const unitCost = unitCostText ? Number(unitCostText) : null;
    if (unitCostText && (Number.isNaN(unitCost) || unitCost! < 0)) {
      this.error.set('Unit cost must be a non-negative number.');
      return;
    }

    this.saving.set(true);
    this.error.set(null);

    const existing = this.item;
    if (existing) {
      const request: UpdateInventoryItemRequest = {
        name,
        sku: this.sku().trim() || null,
        description: this.description().trim() || null,
        categoryId,
        vendorId: this.vendorId() || null,
        unit,
        reorderThreshold,
        unitCost
      };
      this.inventoryService.update(existing.id, request).subscribe({
        next: (result) => {
          this.saving.set(false);
          this.saved.emit(result);
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.error.set(extractErrorMessage(err, 'Could not save this item. Please try again.'));
        }
      });
      return;
    }

    const onHandText = this.onHandQty().trim();
    const onHandQty = onHandText ? Number(onHandText) : 0;
    if (Number.isNaN(onHandQty) || onHandQty < 0) {
      this.saving.set(false);
      this.error.set('Opening on-hand quantity must be a non-negative number.');
      return;
    }

    const createRequest: CreateInventoryItemRequest = {
      name,
      sku: this.sku().trim() || null,
      description: this.description().trim() || null,
      categoryId,
      vendorId: this.vendorId() || null,
      unit,
      onHandQty,
      reorderThreshold,
      unitCost
    };
    this.inventoryService.create(createRequest).subscribe({
      next: (result) => {
        this.saving.set(false);
        this.saved.emit(result);
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.error.set(extractErrorMessage(err, 'Could not create this item. Please try again.'));
      }
    });
  }
}
