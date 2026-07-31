import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { MasterDataService } from '../../core/services/master-data.service';
import { extractErrorMessage } from '../../core/services/problem-details.util';
import { StatusStyleService } from '../../shared/services/status-style.service';
import { SERVICE_TYPE_CIF } from '../../shared/constants/service-type-codes';
import { formatDateOnly, formatTimelineDate } from '../../shared/utils/date-format.util';
import { formatInr, formatQty } from '../../shared/utils/currency.util';
import { formatFileSize } from '../../shared/utils/file-size.util';
import { previewBlob } from '../../shared/utils/file-download.util';
import { ShipmentDocumentsService } from '../services/shipment-documents.service';
import { ShipmentsService } from '../services/shipments.service';
import { ShipmentDetail } from '../models/shipment.models';

interface Step {
  label: string;
  mark: string;
  when: string;
  state: 'done' | 'current' | 'todo';
}

interface LineRow {
  id: string;
  name: string;
  meta: string;
  qtyLabel: string;
  unitCostLabel: string;
  lineTotalLabel: string;
}

interface DocRow {
  id: string;
  filename: string;
  meta: string;
}

/**
 * Shipment detail (ACTION_PLAN E7-13) — ported from Source/Sourcing Ops
 * Platform.dc.html `showShipDetail` (~line 794) against the `/shipments/{id}`
 * contract, diffed against a live response first (D-22).
 *
 * Three places this deliberately departs from the prototype's mock, all because
 * the mock had no backend to be honest against:
 *  - The **stepper's statuses come from master data**, not the prototype's
 *    hard-coded `['PACKED','DISPATCHED','IN TRANSIT','DELIVERED']`. They are
 *    configurable rows a Super Admin may add to (FSD §3.3 / DR-6); a hard-coded
 *    ladder would silently drop any stage the business adds.
 *  - Each step's **"when" comes from `statusHistory`** (D-33), which exists
 *    precisely so this is renderable. The prototype used a fixed date array.
 *  - **"Recorded by" reads `recordedByName`** (D-47, derived from the earliest
 *    history row) rather than the mock's hard-coded person.
 *
 * Status changes go only through `PUT /shipments/{id}/status` — the single path
 * that writes history (D-43), so the stepper can never develop gaps.
 */
@Component({
  selector: 'app-shipment-detail',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './shipment-detail.component.html',
  styleUrl: './shipment-detail.component.scss'
})
export class ShipmentDetailComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly shipmentsService = inject(ShipmentsService);
  private readonly documentsService = inject(ShipmentDocumentsService);
  private readonly masterDataService = inject(MasterDataService);
  private readonly styles = inject(StatusStyleService);
  private readonly auth = inject(AuthService);

  private readonly shipmentId = this.route.snapshot.paramMap.get('id') ?? '';

  readonly statusOptions = toSignal(this.masterDataService.shipmentStatusOptions(), { initialValue: [] });
  /** Scope-filtered (N-20c) — the vendor-scoped types must never appear here. */
  readonly documentTypeOptions = toSignal(this.masterDataService.shipmentDocumentTypeOptions(), { initialValue: [] });

  readonly canEdit = computed(() => this.auth.hasPermission('Shipments.Edit'));

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly shipment = signal<ShipmentDetail | null>(null);

  readonly actionError = signal<string | null>(null);
  readonly advancing = signal(false);

  readonly uploadOpen = signal(false);
  readonly uploadTypeId = signal('');
  readonly uploadFile = signal<File | null>(null);
  readonly uploading = signal(false);
  readonly uploadError = signal<string | null>(null);

  readonly downloadingDocId = signal<string | null>(null);

  readonly headline = computed(() => {
    const s = this.shipment();
    if (!s) return null;
    const svc = this.styles.serviceType(s.serviceType.code);
    const st = this.styles.status(s.status.code);
    return {
      reference: s.reference ?? '(no reference)',
      svcLabel: svc.label,
      svcBg: svc.bg,
      svcFg: svc.fg,
      statusLabel: s.status.label,
      stBg: st.bg,
      stFg: st.fg,
      subtitle: [s.customer.name, s.destination, s.dispatchDate ? `dispatched ${formatDateOnly(s.dispatchDate)}` : null]
        .filter(Boolean)
        .join(' · ')
    };
  });

  /**
   * The 4-step progress stepper. Ordered by the lookup's own `sortOrder`, with each
   * step's timestamp taken from the **first** history row that reached that status —
   * first, not last, because a shipment sent back a stage and forwarded again should
   * show when it originally got there, and because that is what the prototype's
   * left-to-right reading implies.
   *
   * Steps after the current one show "—": a future transition has no time yet, and
   * showing the ETA there (as the mock did) would present an estimate as a record.
   */
  readonly steps = computed<Step[]>(() => {
    const s = this.shipment();
    const options = this.statusOptions();
    if (!s || options.length === 0) return [];

    const currentIdx = options.findIndex((o) => o.id === s.status.id);
    const firstReached = new Map<string, string>();
    for (const h of [...s.statusHistory].sort((a, b) => a.changedAt.localeCompare(b.changedAt))) {
      if (!firstReached.has(h.status.id)) firstReached.set(h.status.id, h.changedAt);
    }

    return options.map((o, i) => {
      const reachedAt = firstReached.get(o.id);
      const state: Step['state'] = i < currentIdx ? 'done' : i === currentIdx ? 'current' : 'todo';
      return {
        label: o.label,
        mark: state === 'done' ? '✓' : state === 'current' ? '●' : String(i + 1),
        when: reachedAt ? formatDateOnly(reachedAt) : '—',
        state
      };
    });
  });

  /** `Mark Dispatched` etc. Null at the last status — there is nowhere to advance to. */
  readonly nextStatus = computed(() => {
    const s = this.shipment();
    const options = this.statusOptions();
    if (!s || options.length === 0) return null;
    const idx = options.findIndex((o) => o.id === s.status.id);
    if (idx < 0 || idx >= options.length - 1) return null;
    return options[idx + 1];
  });

  readonly lines = computed<LineRow[]>(() =>
    (this.shipment()?.lines ?? []).map((l) => ({
      id: l.id,
      name: l.inventoryItemName,
      meta: [l.inventoryItemSku, `per ${l.unit}`].filter(Boolean).join(' · '),
      qtyLabel: formatQty(l.quantity),
      unitCostLabel: l.unitCost === null ? '—' : formatInr(l.unitCost),
      lineTotalLabel: l.lineTotal === null ? '—' : formatInr(l.lineTotal)
    }))
  );

  readonly totals = computed(() => {
    const s = this.shipment();
    if (!s) return null;
    return {
      freight: s.freightCost === null ? '—' : formatInr(s.freightCost),
      value: s.totalValue === null ? '—' : formatInr(s.totalValue)
    };
  });

  /** The right-hand "Details" panel, in the approved screen's field order. */
  readonly fields = computed(() => {
    const s = this.shipment();
    if (!s) return [];
    return [
      { k: 'Customer', v: s.customer.name },
      { k: 'Service type', v: s.serviceType.label },
      {
        k: 'Stock impact',
        // Freight-only shipments hold no stock and cannot carry lines at all (FSD A8 / D-36).
        v: s.serviceType.code === SERVICE_TYPE_CIF ? 'Inventory decremented' : 'No firm-held stock'
      },
      { k: 'Mode / port', v: s.mode ?? '—' },
      { k: 'AWB / BL', v: s.awbOrBl ?? '—' },
      { k: 'ETA', v: formatDateOnly(s.eta) },
      { k: 'Recorded by', v: s.recordedByName ?? '—' },
      { k: 'Created', v: formatTimelineDate(s.createdAt) }
    ];
  });

  readonly documents = computed<DocRow[]>(() =>
    (this.shipment()?.documents ?? []).map((d) => ({
      id: d.id,
      filename: d.originalFilename,
      // "184 KB · 21 Jul 2026", plus the type and uploader the mock had no way to show.
      meta: `${d.documentType.label} · ${formatFileSize(d.sizeBytes)} · ${formatDateOnly(d.uploadedAt)} · ${d.uploadedByName}`
    }))
  );

  readonly canUpload = computed(() => this.canEdit() && this.documentTypeOptions().length > 0);

  constructor() {
    this.masterDataService.ensureLoaded().subscribe({ error: () => {} });
    this.fetch();
  }

  retry(): void {
    this.fetch();
  }

  /** Advances one step via the only endpoint that writes status — and therefore history (D-43). */
  advanceStatus(): void {
    const s = this.shipment();
    const next = this.nextStatus();
    if (!s || !next || this.advancing()) return;

    this.advancing.set(true);
    this.actionError.set(null);
    this.shipmentsService.changeStatus(s.id, { statusId: next.id }).subscribe({
      next: (updated) => {
        this.advancing.set(false);
        this.shipment.set(updated);
      },
      error: (err: unknown) => {
        this.advancing.set(false);
        this.actionError.set(extractErrorMessage(err, 'Could not update the shipment status. Please try again.'));
      }
    });
  }

  openUpload(): void {
    this.uploadTypeId.set('');
    this.uploadFile.set(null);
    this.uploadError.set(null);
    this.uploadOpen.set(true);
  }

  cancelUpload(): void {
    this.uploadOpen.set(false);
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.uploadFile.set(input.files?.[0] ?? null);
  }

  submitUpload(): void {
    const s = this.shipment();
    const file = this.uploadFile();
    const typeId = this.uploadTypeId();
    if (!s || this.uploading()) return;

    if (!file) {
      this.uploadError.set('Choose a file to upload.');
      return;
    }
    if (!typeId) {
      this.uploadError.set('Choose a document type.');
      return;
    }

    this.uploading.set(true);
    this.uploadError.set(null);
    this.documentsService.upload(s.id, file, typeId).subscribe({
      next: () => {
        this.uploading.set(false);
        this.uploadOpen.set(false);
        // Re-reads the shipment rather than pushing the new row in locally: the
        // response is a document, and the detail screen renders more than documents.
        this.fetch();
      },
      error: (err: unknown) => {
        this.uploading.set(false);
        this.uploadError.set(extractErrorMessage(err, 'Could not upload this document. Please try again.'));
      }
    });
  }

  openDocument(docId: string): void {
    if (this.downloadingDocId()) return;
    this.downloadingDocId.set(docId);
    this.actionError.set(null);
    this.documentsService.download(docId).subscribe({
      next: (blob) => {
        this.downloadingDocId.set(null);
        previewBlob(blob);
      },
      error: (err: unknown) => {
        this.downloadingDocId.set(null);
        this.actionError.set(extractErrorMessage(err, 'Could not open this document. Please try again.'));
      }
    });
  }

  private fetch(): void {
    this.loading.set(true);
    this.error.set(null);
    this.shipmentsService.getById(this.shipmentId).subscribe({
      next: (s) => {
        this.shipment.set(s);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.error.set(extractErrorMessage(err, 'Could not load this shipment. Please try again.'));
      }
    });
  }
}
