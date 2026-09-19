import { Component, EventEmitter, Input, Output } from '@angular/core';

/**
 * Generic "are you sure?" dialog built on the shared dialog atoms. The host owns
 * the busy/error state (it runs the actual request) and simply closes the dialog
 * on success — this component only renders and emits.
 */
@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  template: `
    <div class="dialog-backdrop" (click)="onBackdrop($event)">
      <div class="dialog-panel confirm-panel" role="alertdialog" aria-modal="true" [attr.aria-label]="title">
        <div class="dialog-header">
          <div class="dialog-title">{{ title }}</div>
          <button type="button" class="dialog-close" (click)="cancelled.emit()" aria-label="Close">✕</button>
        </div>
        <p class="confirm-message">{{ message }}</p>
        @if (error) {
          <div class="field-error" role="alert">{{ error }}</div>
        }
        <div class="dialog-actions">
          <button type="button" class="btn btn-secondary" [disabled]="busy" (click)="cancelled.emit()">Cancel</button>
          <button type="button" class="btn" [class.btn-danger]="danger" [class.btn-primary]="!danger" [disabled]="busy" (click)="confirmed.emit()">
            {{ busy ? busyLabel : confirmLabel }}
          </button>
        </div>
      </div>
    </div>
  `,
  styles: [
    `
      .confirm-panel {
        max-width: 460px;
      }
      .confirm-message {
        margin: 0;
        font-size: 14px;
        line-height: 1.5;
        color: var(--color-text-strong-muted);
      }
    `
  ]
})
export class ConfirmDialogComponent {
  @Input() title = 'Are you sure?';
  @Input() message = '';
  @Input() confirmLabel = 'Delete';
  @Input() busyLabel = 'Deleting…';
  @Input() danger = true;
  @Input() busy = false;
  @Input() error: string | null = null;

  @Output() confirmed = new EventEmitter<void>();
  @Output() cancelled = new EventEmitter<void>();

  onBackdrop(event: MouseEvent): void {
    if (event.target === event.currentTarget && !this.busy) this.cancelled.emit();
  }
}
