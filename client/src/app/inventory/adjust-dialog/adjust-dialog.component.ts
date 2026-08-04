import { HttpErrorResponse } from '@angular/common/http';
import { Component, EventEmitter, Input, OnInit, Output, computed, inject, signal } from '@angular/core';
import { extractErrorMessage } from '../../core/services/problem-details.util';
import { formatDateOnly, formatTimelineDate } from '../../shared/utils/date-format.util';
import { InventoryService } from '../services/inventory.service';
import { AdjustmentEntry, RecordAdjustmentResult } from '../models/inventory.models';
import { formatQty } from '../utils/format.util';

/** Minimal shape this dialog needs from an inventory item/row — same narrowing rationale as `InboundTargetItem`, plus the current `onHandQty`, which this dialog needs (unlike inbound) to compute and preview the delta. */
export interface AdjustTargetItem {
  id: string;
  name: string;
  sku: string | null;
  unit: string;
  onHandQty: number;
}

function todayIso(): string {
  const d = new Date();
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

/**
 * Record Stock Adjustment dialog (N-38, `POST/GET /inventory/{id}/adjustments`).
 * Deliberately built to the same shape as `InboundDialogComponent`: same
 * dialog atoms, same signal-based form handling (no ReactiveFormsModule),
 * same `save()`/`error()` banner pattern. The one structural difference:
 * this dialog always has a preset item — a physical count is inherently
 * about one specific item's current on-hand — so unlike inbound there is no
 * header-launched / item-picker mode.
 *
 * This is a *recorded correction*, not an editable quantity field (N-38
 * brief) — the user states what they counted and why; the before/after and
 * who is recorded, never a silent overwrite. `reason` is required (mirrors
 * the server's 400-on-blank rule client-side, field-level, the same
 * `err.error.errors[...]` surfacing `CustomerIntakeComponent` introduced for
 * `gstin` — see `reasonServerError` below), and the dialog shows the
 * resulting delta live as the user types, including the zero case ("no
 * change" is a valid, still-recorded adjustment, not something to block).
 *
 * Only `countedQty` is validated non-negative; `item.onHandQty` itself may
 * already be negative (oversold items are legitimate in this app) and
 * adjusting from that state works the same as from any other.
 */
@Component({
  selector: 'app-adjust-dialog',
  standalone: true,
  templateUrl: './adjust-dialog.component.html',
  styleUrl: './adjust-dialog.component.scss'
})
export class AdjustDialogComponent implements OnInit {
  private readonly inventoryService = inject(InventoryService);

  @Input({ required: true }) item!: AdjustTargetItem;
  @Output() closed = new EventEmitter<void>();
  @Output() adjusted = new EventEmitter<RecordAdjustmentResult>();

  readonly countedQty = signal('');
  readonly reason = signal('');
  readonly adjustedOn = signal(todayIso());
  readonly reasonTouched = signal(false);

  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  /** Server-side 400 field error for `reason` — reuses the `*ServerError` signal pattern `CustomerIntakeComponent.gstinServerError` established for surfacing `err.error.errors[...]` on a specific field, rather than a second mechanism. */
  readonly reasonServerError = signal<string | null>(null);

  readonly historyLoading = signal(false);
  readonly historyError = signal<string | null>(null);
  readonly history = signal<AdjustmentEntry[]>([]);

  readonly currentQtyLabel = computed(() => `${formatQty(this.item.onHandQty)} ${this.item.unit}`);

  /** `null` while `countedQty` isn't yet a valid non-negative number — the template hides the delta preview rather than showing one against a half-typed or invalid value. */
  readonly delta = computed<number | null>(() => {
    const n = this.parsedCountedQty();
    if (n === null) return null;
    return n - this.item.onHandQty;
  });

  /** e.g. current 40, counted 37 → "−3"; counted 40 → "No change" (a valid, still-recorded zero-delta adjustment — not discouraged). */
  readonly deltaLabel = computed(() => {
    const d = this.delta();
    if (d === null) return '';
    if (d === 0) return 'No change';
    return d > 0 ? `+${formatQty(d)}` : `−${formatQty(Math.abs(d))}`;
  });

  readonly deltaColor = computed(() => {
    const d = this.delta();
    if (d === null || d === 0) return 'var(--color-text-muted)';
    return d > 0 ? 'var(--color-success)' : 'var(--color-danger)';
  });

  ngOnInit(): void {
    this.loadHistory();
  }

  onReasonInput(value: string): void {
    this.reason.set(value);
    this.reasonServerError.set(null);
  }

  cancel(): void {
    this.closed.emit();
  }

  save(): void {
    if (this.saving()) return;

    this.reasonTouched.set(true);
    this.reasonServerError.set(null);
    this.error.set(null);

    if (!this.reason().trim()) {
      // Field-level message renders from `reasonTouched` in the template — the audit hole this feature closes, so it gets its own message rather than folding into the general banner.
      return;
    }

    const countedQty = this.parsedCountedQty();
    if (countedQty === null) {
      this.error.set('Counted quantity must be a non-negative number.');
      return;
    }

    const adjustedOn = this.adjustedOn();
    this.saving.set(true);
    this.inventoryService
      .recordAdjustment(this.item.id, { countedQty, reason: this.reason().trim(), adjustedOn: adjustedOn || undefined })
      .subscribe({
        next: (result) => {
          this.saving.set(false);
          this.adjusted.emit(result);
        },
        error: (err: unknown) => {
          this.saving.set(false);
          if (err instanceof HttpErrorResponse && err.status === 400) {
            const body = err.error as { errors?: Record<string, string[]> } | undefined;
            const reasonMessages = body?.errors?.['reason'];
            if (reasonMessages?.length) {
              this.reasonServerError.set(reasonMessages.join(' '));
              return;
            }
          }
          this.error.set(extractErrorMessage(err, 'Could not record this adjustment. Please try again.'));
        }
      });
  }

  /** `adjustedOn` is a bare count date (`InboundEntry.entryDate`'s convention); `adjustedAt` is the full record timestamp — deliberately formatted differently so the two aren't visually confused in the history list. */
  formatCountDate(iso: string): string {
    return formatDateOnly(iso);
  }

  formatRecordedAt(iso: string): string {
    return formatTimelineDate(iso);
  }

  formatDelta(entry: AdjustmentEntry): string {
    if (entry.delta === 0) return 'No change';
    return entry.delta > 0 ? `+${formatQty(entry.delta)}` : `−${formatQty(Math.abs(entry.delta))}`;
  }

  private parsedCountedQty(): number | null {
    const text = this.countedQty().trim();
    if (!text) return null;
    const n = Number(text);
    if (Number.isNaN(n) || n < 0) return null;
    return n;
  }

  private loadHistory(): void {
    this.historyLoading.set(true);
    this.historyError.set(null);
    this.inventoryService.listAdjustments(this.item.id).subscribe({
      next: (entries) => {
        this.historyLoading.set(false);
        this.history.set(entries);
      },
      error: (err: unknown) => {
        this.historyLoading.set(false);
        this.historyError.set(extractErrorMessage(err, 'Could not load adjustment history.'));
      }
    });
  }
}
