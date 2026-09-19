import { AfterViewInit, Component, ElementRef, EventEmitter, HostListener, Input, Output, ViewChild, computed, inject, signal } from '@angular/core';
import { extractErrorMessage } from '../../core/services/problem-details.util';
import { InventoryService } from '../services/inventory.service';
import { InventoryItem } from '../models/inventory.models';
import { formatQty } from '../utils/format.util';

/**
 * Compact "Add Stock" popup: the user enters only the quantity received and
 * it is added on top of the current balance. Saves through
 * `POST /inventory/{id}/inbound` (E7-02), which writes an inbound entry and
 * increments `onHandQty` in one transaction — so there is no client-side
 * arithmetic on the stored balance and the receipt stays in the history.
 */
@Component({
  selector: 'app-inventory-add-stock-dialog',
  standalone: true,
  templateUrl: './add-stock-dialog.component.html',
  styleUrl: './add-stock-dialog.component.scss'
})
export class AddStockDialogComponent implements AfterViewInit {
  private readonly inventoryService = inject(InventoryService);

  @Input({ required: true }) item!: InventoryItem;
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<InventoryItem>();

  @ViewChild('qtyInput') private qtyInput?: ElementRef<HTMLInputElement>;

  readonly quantity = signal('');
  readonly reference = signal('');
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  private readonly parsedQty = computed(() => {
    const raw = this.quantity().trim();
    if (raw === '') return null;
    const n = Number(raw);
    return Number.isFinite(n) ? n : NaN;
  });

  readonly canSave = computed(() => {
    const n = this.parsedQty();
    return n !== null && n > 0 && !this.saving();
  });

  readonly currentLabel = computed(() => formatQty(this.item.onHandQty));
  /** Live preview of the resulting balance; `null` until a valid quantity is typed. */
  readonly newTotalLabel = computed(() => {
    const n = this.parsedQty();
    return n !== null && n > 0 ? formatQty(this.item.onHandQty + n) : null;
  });

  ngAfterViewInit(): void {
    this.qtyInput?.nativeElement.focus();
  }

  @HostListener('document:keydown.escape')
  cancel(): void {
    if (this.saving()) return;
    this.closed.emit();
  }

  save(): void {
    if (!this.canSave()) {
      if (this.parsedQty() !== null) this.error.set('Enter a quantity greater than zero.');
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    this.inventoryService
      .recordInbound(this.item.id, {
        quantity: this.parsedQty()!,
        entryDate: todayIso(),
        reference: this.reference().trim() || null
      })
      .subscribe({
        next: (res) => {
          this.saving.set(false);
          this.saved.emit(res.item);
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.error.set(extractErrorMessage(err, 'Could not add stock. Please try again.'));
        }
      });
  }
}

/** Local calendar date as `yyyy-MM-dd` — the API's `DateOnly` shape. */
function todayIso(): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}
