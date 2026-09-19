import { Component, EventEmitter, Input, OnDestroy, OnInit, Output, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Observable, of, switchMap } from 'rxjs';
import { MasterDataService } from '../../core/services/master-data.service';
import { extractErrorMessage } from '../../core/services/problem-details.util';
import { VendorsService } from '../../vendors/services/vendors.service';
import { VendorListItem } from '../../vendors/models/vendor.models';
import { InventoryService } from '../services/inventory.service';
import { CreateInventoryItemRequest, InventoryItem, UpdateInventoryItemRequest } from '../models/inventory.models';
import { ACCEPTED_IMAGE_TYPES, MAX_IMAGE_BYTES, createThumbnail } from '../../shared/utils/image-thumbnail.util';

/** Anything other than a letter or a space. Mirrors the API's check in `InventoryService`. */
const UNIT_DISALLOWED = /[^\p{L} ]/u;
const UNIT_DISALLOWED_ALL = /[^\p{L} ]/gu;

/**
 * Shared inventory item create/edit dialog (ACTION_PLAN E7-01), following
 * `VendorFormDialogComponent`'s house pattern (dialog atoms, `field`-atom
 * inputs, create-vs-edit driven by whether `item` is set).
 *
 * "Current Stock" is editable in both modes. On edit it is only sent when it
 * actually changed; the API then records the change as a stock adjustment
 * ("Count edited"), so the history of the balance is kept.
 *
 * HSN/SAC and GST rate are no longer captured here — they are entered on the
 * invoice line instead. On edit, any values an item already carries are sent
 * back unchanged so this form never wipes them.
 *
 * The optional product image is uploaded after the item is saved (a new item
 * needs its id first). The browser generates the thumbnail, so the API stores
 * both without needing an image library.
 */
@Component({
  selector: 'app-inventory-item-form-dialog',
  standalone: true,
  templateUrl: './item-form-dialog.component.html',
  styleUrl: './item-form-dialog.component.scss'
})
export class ItemFormDialogComponent implements OnInit, OnDestroy {
  private readonly inventoryService = inject(InventoryService);
  private readonly masterDataService = inject(MasterDataService);
  private readonly vendorsService = inject(VendorsService);

  @Input() item: InventoryItem | null = null;
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<InventoryItem>();

  readonly categoryOptions = toSignal(this.masterDataService.categoryOptions(), { initialValue: [] });

  /** First 100 vendors for the optional "Source Vendor" picker (vendors aren't a master-data lookup). */
  readonly vendorOptions = signal<VendorListItem[]>([]);

  readonly isEdit = computed(() => !!this.item);
  readonly dialogTitle = computed(() => (this.isEdit() ? 'Edit Item' : 'Add Item'));

  readonly name = signal('');
  readonly sku = signal('');
  readonly description = signal('');
  readonly categoryId = signal('');
  readonly vendorId = signal('');
  readonly unit = signal('');
  /** Offered in the Unit field's dropdown; any other letters-only unit may still be typed. */
  readonly unitSuggestions = ['pcs', 'box', 'set', 'pair', 'dozen', 'pack', 'kg', 'g', 'ltr', 'ml', 'mtr', 'roll', 'carton', 'bag'];
  readonly reorderThreshold = signal('');
  readonly unitCost = signal('');
  readonly sellingPrice = signal('');
  readonly onHandQty = signal('');

  readonly acceptedImageTypes = ACCEPTED_IMAGE_TYPES.join(',');
  /** Newly picked image, not yet uploaded. */
  readonly pendingImage = signal<File | null>(null);
  /** Object URL (new pick) or data URL (existing thumbnail) shown in the preview. */
  readonly imagePreview = signal<string | null>(null);
  /** True once the user asked to drop the item's existing image. */
  readonly removeExistingImage = signal(false);

  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  private previewObjectUrl: string | null = null;

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
    this.sellingPrice.set(it.sellingPrice != null ? String(it.sellingPrice) : '');
    this.onHandQty.set(String(it.onHandQty));
    this.imagePreview.set(it.thumbnailDataUrl ?? null);
  }

  ngOnDestroy(): void {
    this.revokePreview();
  }

  onImageSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    input.value = '';
    if (!file) return;
    if (!ACCEPTED_IMAGE_TYPES.includes(file.type)) {
      this.error.set('Choose a JPEG, PNG or WebP image.');
      return;
    }
    if (file.size > MAX_IMAGE_BYTES) {
      this.error.set('Images must be 8 MB or smaller.');
      return;
    }
    this.error.set(null);
    this.revokePreview();
    this.previewObjectUrl = URL.createObjectURL(file);
    this.pendingImage.set(file);
    this.imagePreview.set(this.previewObjectUrl);
    this.removeExistingImage.set(false);
  }

  clearImage(): void {
    this.revokePreview();
    this.pendingImage.set(null);
    this.imagePreview.set(null);
    if (this.item?.hasImage) this.removeExistingImage.set(true);
  }

  cancel(): void {
    this.closed.emit();
  }

  /** Unit is a name (pcs, kg), never a number — drop anything but letters and spaces as it's typed. */
  onUnitInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    const cleaned = input.value.replace(UNIT_DISALLOWED_ALL, '');
    if (cleaned !== input.value) input.value = cleaned;
    this.unit.set(cleaned);
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
    if (UNIT_DISALLOWED.test(unit)) {
      this.error.set('Unit must contain letters only (e.g. pcs, kg, box).');
      return;
    }

    const reorderThreshold = Number(reorderText);
    if (Number.isNaN(reorderThreshold) || reorderThreshold < 0) {
      this.error.set('Reorder threshold must be a non-negative number.');
      return;
    }

    const unitCost = this.optionalNumber(this.unitCost(), 'Unit cost');
    if (unitCost === undefined) return;
    const sellingPrice = this.optionalNumber(this.sellingPrice(), 'Selling price');
    if (sellingPrice === undefined) return;

    const onHandText = this.onHandQty().trim();
    const onHandQty = onHandText ? Number(onHandText) : 0;
    if (Number.isNaN(onHandQty) || onHandQty < 0) {
      this.error.set('Current stock must be a non-negative number.');
      return;
    }

    this.saving.set(true);
    this.error.set(null);

    const base = {
      name,
      sku: this.sku().trim() || null,
      description: this.description().trim() || null,
      categoryId,
      vendorId: this.vendorId() || null,
      unit,
      reorderThreshold,
      unitCost,
      sellingPrice
    };

    const existing = this.item;
    const save$: Observable<InventoryItem> = existing
      ? this.inventoryService.update(existing.id, {
          ...base,
          // Not edited here any more — pass stored values through untouched.
          hsnCode: existing.hsnCode,
          gstRate: existing.gstRate,
          ...(onHandQty !== existing.onHandQty ? { onHandQty } : {})
        } satisfies UpdateInventoryItemRequest)
      : this.inventoryService.create({ ...base, onHandQty } satisfies CreateInventoryItemRequest);

    save$.pipe(switchMap((saved) => this.applyImageChange(saved))).subscribe({
      next: (result) => {
        this.saving.set(false);
        this.saved.emit(result);
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.error.set(extractErrorMessage(err, 'Could not save this item. Please try again.'));
      }
    });
  }

  /** Uploads a newly picked image or removes the existing one, after the item itself is saved. */
  private applyImageChange(saved: InventoryItem): Observable<InventoryItem> {
    const file = this.pendingImage();
    if (file) {
      return new Observable<Blob>((sub) => {
        createThumbnail(file).then(
          (thumb) => {
            sub.next(thumb);
            sub.complete();
          },
          (err) => sub.error(err)
        );
      }).pipe(switchMap((thumb) => this.inventoryService.uploadImage(saved.id, file, thumb)));
    }
    if (this.removeExistingImage() && saved.hasImage) {
      return this.inventoryService.removeImage(saved.id);
    }
    return of(saved);
  }

  /** Parses an optional non-negative number; returns `undefined` (and sets the error) when invalid. */
  private optionalNumber(text: string, label: string): number | null | undefined {
    const trimmed = text.trim();
    if (!trimmed) return null;
    const value = Number(trimmed);
    if (Number.isNaN(value) || value < 0) {
      this.error.set(`${label} must be a non-negative number.`);
      return undefined;
    }
    return value;
  }

  private revokePreview(): void {
    if (this.previewObjectUrl) {
      URL.revokeObjectURL(this.previewObjectUrl);
      this.previewObjectUrl = null;
    }
  }
}
