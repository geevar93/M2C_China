import { Component, EventEmitter, Input, OnInit, Output, computed, inject, signal } from '@angular/core';
import { extractErrorMessage } from '../../core/services/problem-details.util';
import { InventoryService } from '../services/inventory.service';
import { RecordInboundResult } from '../models/inventory.models';

/** Minimal shape this dialog needs from an inventory item/row — kept narrow so both the list screen's row objects and raw `InventoryItem`s satisfy it without mapping. */
export interface InboundTargetItem {
  id: string;
  name: string;
  sku: string | null;
  unit: string;
}

function todayIso(): string {
  const d = new Date();
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

/**
 * Record Inbound Stock dialog (ACTION_PLAN E7-02, `POST /inventory/{id}/inbound`).
 * No prototype precedent — the approved mock's "+ Record Inbound Stock"
 * button is a static, unwired element (`Source/Sourcing Ops
 * Platform.dc.html` ~line 675) — built from the same dialog atoms as
 * `VendorFormDialogComponent`.
 *
 * Reachable two ways per the ACTION_PLAN brief: per-row (`presetItem` set,
 * the common case — no item picker shown), or from the screen's header
 * button with no row context yet (`presetItem` null), in which case
 * `candidateItems` — the currently-loaded page of inventory rows — backs a
 * plain item picker `<select>`. A business whose item isn't on the current
 * page/filter would need to filter to it first; there is no separate
 * type-ahead search here since the list screen's own search already serves
 * that purpose.
 *
 * `POST /inventory/{id}/inbound` returns both the new entry and the
 * re-computed item (§15.3) — `recorded` emits that whole result so the
 * caller can patch its row from the response instead of refetching the list.
 */
@Component({
  selector: 'app-inbound-dialog',
  standalone: true,
  templateUrl: './inbound-dialog.component.html',
  styleUrl: './inbound-dialog.component.scss'
})
export class InboundDialogComponent implements OnInit {
  private readonly inventoryService = inject(InventoryService);

  @Input() presetItem: InboundTargetItem | null = null;
  @Input() candidateItems: InboundTargetItem[] = [];
  @Output() closed = new EventEmitter<void>();
  @Output() recorded = new EventEmitter<RecordInboundResult>();

  readonly selectedItemId = signal('');
  readonly quantity = signal('');
  readonly entryDate = signal(todayIso());
  readonly reference = signal('');

  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  readonly selectedItem = computed<InboundTargetItem | null>(() => {
    if (this.presetItem) return this.presetItem;
    return this.candidateItems.find((i) => i.id === this.selectedItemId()) ?? null;
  });

  readonly selectedItemLabel = computed(() => {
    const it = this.selectedItem();
    if (!it) return '';
    return it.sku ? `${it.name} · ${it.sku}` : it.name;
  });

  ngOnInit(): void {
    if (this.presetItem) {
      this.selectedItemId.set(this.presetItem.id);
    }
  }

  setItem(id: string): void {
    this.selectedItemId.set(id);
  }

  cancel(): void {
    this.closed.emit();
  }

  save(): void {
    if (this.saving()) return;
    const item = this.selectedItem();
    if (!item) {
      this.error.set('Choose an item to record inbound stock against.');
      return;
    }

    const quantityText = this.quantity().trim();
    const quantity = Number(quantityText);
    if (!quantityText || Number.isNaN(quantity) || quantity <= 0) {
      this.error.set('Quantity must be a positive number.');
      return;
    }

    const entryDate = this.entryDate();
    if (!entryDate) {
      this.error.set('Entry date is required.');
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    this.inventoryService.recordInbound(item.id, { quantity, entryDate, reference: this.reference().trim() || null }).subscribe({
      next: (result) => {
        this.saving.set(false);
        this.recorded.emit(result);
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.error.set(extractErrorMessage(err, 'Could not record this inbound entry. Please try again.'));
      }
    });
  }
}
