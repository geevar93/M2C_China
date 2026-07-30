import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { MasterDataService } from '../../core/services/master-data.service';
import { extractErrorMessage } from '../../core/services/problem-details.util';
import { StatusStyleService } from '../../shared/services/status-style.service';
import { formatDateOnly } from '../../shared/utils/date-format.util';
import { formatFileSize } from '../../shared/utils/file-size.util';
import { saveBlobAs } from '../../shared/utils/file-download.util';
import { formatMoneyOrDash as formatMoney } from '../../shared/utils/money.util';
import { ShipmentsService } from '../services/shipments.service';
import { ShipmentDetail, ShipmentDocument } from '../models/shipment.models';
import { ShipmentFormDialogComponent } from '../shipment-form-dialog/shipment-form-dialog.component';
import { ShipmentDocumentUploadDialogComponent } from '../document-upload-dialog/document-upload-dialog.component';

interface StepView {
  id: string;
  label: string;
  mark: string;
  when: string;
  dotBg: string;
  dotFg: string;
  dotBorder: string;
  lineColor: string;
  labelColor: string;
}

interface LineRow {
  id: string;
  name: string;
  meta: string;
  qty: string;
  unitCost: string;
  lineTotal: string;
}

interface DetailField {
  k: string;
  v: string;
}

interface DocRow {
  id: string;
  fileName: string;
  meta: string;
}

const REACHED_COLOR = { dotBg: '#2d5be3', dotFg: '#fff', dotBorder: '#2d5be3', lineColor: '#2d5be3', labelColor: '#1a2332' };
const FUTURE_COLOR = { dotBg: '#fff', dotFg: '#6b7280', dotBorder: '#e5e7eb', lineColor: '#e5e7eb', labelColor: '#6b7280' };

/**
 * Shipment detail (ACTION_PLAN E7-13) — ported from Source/Sourcing Ops
 * Platform.dc.html `showShipDetail` (~line 795). Single `GET /shipments/{id}`
 * load (§15.3's `ShipmentDetail` shape embeds lines/statusHistory/documents),
 * no forkJoin.
 *
 * **D-43, the whole point of this screen's action-button split:** status only
 * ever advances through `ShipmentsService.changeStatus()` → `PUT
 * /shipments/{id}/status`, the one path that also writes a
 * `shipment_status_history` row. The "Edit" dialog (`ShipmentFormDialogComponent`)
 * never renders a status control — see that component's class doc.
 *
 * The stepper's step list AND the advance-button's "next status" are both
 * derived from the live `shipmentStatuses` lookup's `sortOrder`
 * (`MasterDataService`), never a hard-coded `['PACKED','DISPATCHED',...]`
 * array — a hard-coded list would break the day the business adds a stage,
 * which is exactly why E7-07 refuses to enforce a transition graph
 * server-side. Each step's "when" comes from the real `statusHistory` (D-33):
 * an em-dash for a status not yet reached, never an invented timestamp (the
 * prototype's own mock `stepWhen` array is exactly what NOT to port here).
 */
@Component({
  selector: 'app-shipment-detail',
  standalone: true,
  imports: [RouterLink, ShipmentFormDialogComponent, ShipmentDocumentUploadDialogComponent],
  templateUrl: './shipment-detail.component.html',
  styleUrl: './shipment-detail.component.scss'
})
export class ShipmentDetailComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly shipmentsService = inject(ShipmentsService);
  private readonly masterDataService = inject(MasterDataService);
  private readonly styles = inject(StatusStyleService);
  private readonly auth = inject(AuthService);

  readonly canEdit = computed(() => this.auth.hasPermission('Shipments.Edit'));

  readonly shipmentStatusOptions = toSignal(this.masterDataService.shipmentStatusOptions(), { initialValue: [] });

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly shipment = signal<ShipmentDetail | null>(null);

  readonly editOpen = signal(false);
  readonly uploadOpen = signal(false);

  readonly advancing = signal(false);
  readonly advanceError = signal<string | null>(null);

  readonly openingDocId = signal<string | null>(null);
  readonly openError = signal<string | null>(null);

  readonly svcChip = computed(() => {
    const s = this.shipment();
    return s ? this.styles.serviceType(s.serviceType.code) : { label: '—', bg: '#e5e7eb', fg: '#374151' };
  });

  readonly statusChip = computed(() => {
    const s = this.shipment();
    return s ? this.styles.status(s.status.code) : { bg: '#e5e7eb', fg: '#374151' };
  });

  readonly subline = computed(() => {
    const s = this.shipment();
    if (!s) return '';
    return `${s.customer.name} · ${s.destination ?? '—'} · dispatched ${formatDateOnly(s.dispatchDate)}`;
  });

  /** Position of the shipment's current status within the live, sortOrder-sorted lookup — NOT a hard-coded index (see class doc). -1 if the status is retired/not found. */
  private readonly currentStatusIndex = computed(() => {
    const s = this.shipment();
    const options = this.shipmentStatusOptions();
    if (!s) return -1;
    return options.findIndex((o) => o.id === s.status.id);
  });

  readonly steps = computed<StepView[]>(() => {
    const s = this.shipment();
    const options = this.shipmentStatusOptions();
    if (!s) return [];
    const currentIdx = this.currentStatusIndex();
    return options.map((opt, i) => {
      const reached = currentIdx >= 0 && i <= currentIdx;
      const isCurrent = currentIdx >= 0 && i === currentIdx;
      const historyRow = s.statusHistory.find((h) => h.status.id === opt.id);
      const colors = reached ? REACHED_COLOR : FUTURE_COLOR;
      return {
        id: opt.id,
        label: opt.label,
        mark: currentIdx >= 0 && i < currentIdx ? '✓' : isCurrent ? '●' : String(i + 1),
        when: historyRow ? formatDateOnly(historyRow.changedAt) : '—',
        dotBg: colors.dotBg,
        dotFg: colors.dotFg,
        dotBorder: colors.dotBorder,
        // The connector line after the LAST reached step should also read as
        // "done" (matching the prototype's `i < idx` line rule) — future
        // steps' trailing line stays neutral.
        lineColor: currentIdx >= 0 && i < currentIdx ? REACHED_COLOR.lineColor : FUTURE_COLOR.lineColor,
        labelColor: colors.labelColor
      };
    });
  });

  /** Next status to advance to, by lookup position — never a hard-coded transition array. Null once already at the last status. */
  private readonly nextStatus = computed(() => {
    const options = this.shipmentStatusOptions();
    const idx = this.currentStatusIndex();
    if (idx < 0 || idx >= options.length - 1) return null;
    return options[idx + 1];
  });

  readonly canAdvance = computed(() => !!this.nextStatus());
  readonly advanceLabel = computed(() => {
    const next = this.nextStatus();
    return next ? `Mark ${next.label}` : 'Delivered';
  });

  readonly lines = computed<LineRow[]>(() => {
    const s = this.shipment();
    if (!s) return [];
    return s.lines.map((l) => ({
      id: l.id,
      name: l.inventoryItemName,
      meta: `${l.inventoryItemSku ?? '—'} · ${l.unit}`,
      qty: String(l.quantity),
      unitCost: formatMoney(l.unitCost),
      lineTotal: formatMoney(l.lineTotal)
    }));
  });

  readonly noLines = computed(() => this.lines().length === 0);

  readonly freightLabel = computed(() => formatMoney(this.shipment()?.freightCost ?? null));
  readonly valueLabel = computed(() => formatMoney(this.shipment()?.totalValue ?? null));

  readonly fields = computed<DetailField[]>(() => {
    const s = this.shipment();
    if (!s) return [];
    return [
      { k: 'Customer', v: s.customer.name },
      { k: 'Service type', v: s.serviceType.label },
      { k: 'Stock impact', v: s.serviceType.code === 'CIF' ? 'Inventory decremented' : 'No firm-held stock' },
      { k: 'Mode / port', v: s.mode ?? '—' },
      { k: 'AWB / BL', v: s.awbOrBl ?? '—' },
      { k: 'ETA', v: formatDateOnly(s.eta) },
      { k: 'Recorded by', v: s.recordedByName ?? '—' }
    ];
  });

  readonly docs = computed<DocRow[]>(() => {
    const s = this.shipment();
    if (!s) return [];
    return s.documents.map((d) => this.toDocRow(d));
  });

  constructor() {
    this.masterDataService.ensureLoaded().subscribe({ error: () => {} });
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

  onShipmentSaved(shipment: ShipmentDetail): void {
    this.editOpen.set(false);
    this.shipment.set(shipment);
  }

  openUpload(): void {
    this.uploadOpen.set(true);
  }

  cancelUpload(): void {
    this.uploadOpen.set(false);
  }

  onDocumentUploaded(_doc: ShipmentDocument): void {
    this.uploadOpen.set(false);
    const id = this.shipment()?.id;
    if (id) this.load(id);
  }

  advanceStatus(): void {
    const s = this.shipment();
    const next = this.nextStatus();
    if (!s || !next || this.advancing()) return;
    this.advancing.set(true);
    this.advanceError.set(null);
    this.shipmentsService.changeStatus(s.id, { statusId: next.id }).subscribe({
      next: (updated) => {
        this.advancing.set(false);
        this.shipment.set(updated);
      },
      error: (err: unknown) => {
        this.advancing.set(false);
        this.advanceError.set(extractErrorMessage(err, 'Could not update this shipment\'s status. Please try again.'));
      }
    });
  }

  openDocument(doc: DocRow): void {
    if (this.openingDocId()) return;
    this.openingDocId.set(doc.id);
    this.openError.set(null);
    this.shipmentsService.downloadDocument(doc.id).subscribe({
      next: (blob) => {
        this.openingDocId.set(null);
        saveBlobAs(blob, doc.fileName);
      },
      error: (err: unknown) => {
        this.openingDocId.set(null);
        this.openError.set(extractErrorMessage(err, 'Could not open this document. Please try again.'));
      }
    });
  }

  private load(id: string): void {
    this.loading.set(true);
    this.error.set(null);
    this.shipmentsService.getById(id).subscribe({
      next: (shipment) => {
        this.shipment.set(shipment);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.error.set(extractErrorMessage(err, 'Could not load this shipment. Please try again.'));
      }
    });
  }

  private toDocRow(d: ShipmentDocument): DocRow {
    return {
      id: d.id,
      fileName: d.originalFilename,
      meta: `${formatFileSize(d.sizeBytes)} · ${formatDateOnly(d.uploadedAt)}`
    };
  }
}
