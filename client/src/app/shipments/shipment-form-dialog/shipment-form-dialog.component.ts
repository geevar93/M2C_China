import { HttpErrorResponse } from '@angular/common/http';
import { Component, EventEmitter, Input, OnInit, Output, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MasterDataService } from '../../core/services/master-data.service';
import { extractErrorMessage } from '../../core/services/problem-details.util';
import { CustomersService } from '../../customers/services/customers.service';
import { InventoryService } from '../../inventory/services/inventory.service';
import { InventoryItem } from '../../inventory/models/inventory.models';
import { ShipmentsService } from '../services/shipments.service';
import {
  CreateShipmentLineRequest,
  CreateShipmentRequest,
  InsufficientStockItem,
  InsufficientStockProblemDetails,
  ShipmentDetail,
  UpdateShipmentRequest
} from '../models/shipment.models';

interface CustomerOption {
  id: string;
  name: string;
}

interface LineRow {
  inventoryItemId: string;
  quantity: string;
}

/**
 * Shared create/edit dialog for outbound shipments (ACTION_PLAN E7-12/E7-13),
 * following `VendorFormDialogComponent`'s dual-mode convention — `shipment`
 * input is null in create mode. Used by the shipments list's "+ Record
 * Outbound Shipment" button (`POST /shipments`) and the shipment detail
 * screen's "Edit" action (`PUT /shipments/{id}`).
 *
 * **D-43, load-bearing:** `UpdateShipmentRequest` has no `statusId` — status
 * only ever moves through `ShipmentsService.changeStatus()`, the one path
 * that also writes a `shipment_status_history` row. This dialog therefore
 * renders a status **picker** only in create mode and a **read-only** status
 * line in edit mode — never a status `<select>` bound to the update request,
 * which would silently fail to take effect server-side.
 *
 * **D-36:** a `FREIGHT_ONLY` shipment must be sent with no lines at all, or
 * the API 400s. The lines section is hidden outright once `FREIGHT_ONLY` is
 * selected, and any staged lines are dropped rather than silently sent.
 *
 * **D-35, the 409 path:** a create/update that would drive on-hand quantity
 * negative 409s with a named `insufficientStock[]` list. This dialog surfaces
 * those specifics and offers an explicit "Record Anyway" action that retries
 * the exact same request with `allowNegativeStock: true` — it never retries
 * silently, and never swallows the 409 into a generic error banner.
 */
@Component({
  selector: 'app-shipment-form-dialog',
  standalone: true,
  templateUrl: './shipment-form-dialog.component.html',
  styleUrl: './shipment-form-dialog.component.scss'
})
export class ShipmentFormDialogComponent implements OnInit {
  private readonly shipmentsService = inject(ShipmentsService);
  private readonly customersService = inject(CustomersService);
  private readonly inventoryService = inject(InventoryService);
  private readonly masterDataService = inject(MasterDataService);

  @Input() shipment: ShipmentDetail | null = null;
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<ShipmentDetail>();

  readonly serviceTypeOptions = toSignal(this.masterDataService.serviceTypeOptions(), { initialValue: [] });
  readonly shipmentStatusOptions = toSignal(this.masterDataService.shipmentStatusOptions(), { initialValue: [] });

  readonly isEdit = computed(() => !!this.shipment);
  readonly dialogTitle = computed(() => (this.isEdit() ? 'Edit Shipment' : 'Record Outbound Shipment'));

  readonly customerId = signal('');
  readonly destination = signal('');
  readonly serviceTypeId = signal('');
  readonly dispatchDate = signal('');
  readonly statusId = signal('');
  readonly freightCost = signal('');
  readonly totalValue = signal('');
  readonly mode = signal('');
  readonly awbOrBl = signal('');
  readonly eta = signal('');
  readonly lines = signal<LineRow[]>([]);

  readonly customersLoading = signal(false);
  readonly customerOptions = signal<CustomerOption[]>([]);
  readonly inventoryLoading = signal(false);
  readonly inventoryOptions = signal<InventoryItem[]>([]);

  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly insufficientStock = signal<InsufficientStockItem[] | null>(null);

  /** True once the selected/edited service type is CIF — freight-only shipments never take lines (D-36). */
  readonly showsLines = computed(() => {
    const opt = this.serviceTypeOptions().find((o) => o.id === this.serviceTypeId());
    return !opt || opt.code === 'CIF';
  });

  /** `totalValue` is only honoured by the API when there are no lines (D-31) — disabled once a line is staged. */
  readonly totalValueEditable = computed(() => this.lines().length === 0);

  private pendingRequest: CreateShipmentRequest | UpdateShipmentRequest | null = null;

  ngOnInit(): void {
    this.masterDataService.ensureLoaded().subscribe({ error: () => {} });
    this.loadCustomers();
    this.loadInventory();

    const s = this.shipment;
    if (!s) return;
    this.customerId.set(s.customer.id);
    this.destination.set(s.destination ?? '');
    this.serviceTypeId.set(s.serviceType.id);
    this.dispatchDate.set(toDateInputValue(s.dispatchDate));
    this.statusId.set(s.status.id);
    this.freightCost.set(s.freightCost != null ? String(s.freightCost) : '');
    this.totalValue.set(s.totalValue != null ? String(s.totalValue) : '');
    this.mode.set(s.mode ?? '');
    this.awbOrBl.set(s.awbOrBl ?? '');
    this.eta.set(toDateInputValue(s.eta));
    this.lines.set(s.lines.map((l) => ({ inventoryItemId: l.inventoryItemId, quantity: String(l.quantity) })));
  }

  addLine(): void {
    this.lines.update((rows) => [...rows, { inventoryItemId: '', quantity: '' }]);
  }

  removeLine(index: number): void {
    this.lines.update((rows) => rows.filter((_, i) => i !== index));
  }

  setLineItem(index: number, inventoryItemId: string): void {
    this.lines.update((rows) => rows.map((r, i) => (i === index ? { ...r, inventoryItemId } : r)));
  }

  setLineQuantity(index: number, quantity: string): void {
    this.lines.update((rows) => rows.map((r, i) => (i === index ? { ...r, quantity } : r)));
  }

  cancel(): void {
    this.closed.emit();
  }

  save(): void {
    if (this.saving()) return;
    const customerId = this.customerId();
    const serviceTypeId = this.serviceTypeId();
    if (!customerId || !serviceTypeId) {
      this.error.set('Customer and service type are required.');
      return;
    }
    if (!this.isEdit() && !this.statusId()) {
      this.error.set('An initial status is required.');
      return;
    }

    const lineRequests = this.buildLineRequests();
    if (lineRequests === null) {
      this.error.set('Every shipment line needs an item and a quantity greater than zero.');
      return;
    }

    this.error.set(null);
    this.insufficientStock.set(null);

    if (this.isEdit()) {
      const request: UpdateShipmentRequest = {
        customerId,
        destination: this.destination().trim() || null,
        serviceTypeId,
        dispatchDate: toIsoOrNull(this.dispatchDate()),
        freightCost: this.freightCost().trim() ? Number(this.freightCost()) : null,
        totalValue: this.totalValueEditable() && this.totalValue().trim() ? Number(this.totalValue()) : null,
        mode: this.mode().trim() || null,
        awbOrBl: this.awbOrBl().trim() || null,
        eta: toIsoOrNull(this.eta()),
        lines: lineRequests
      };
      this.submit(request);
    } else {
      const request: CreateShipmentRequest = {
        customerId,
        destination: this.destination().trim() || null,
        serviceTypeId,
        dispatchDate: toIsoOrNull(this.dispatchDate()),
        statusId: this.statusId(),
        freightCost: this.freightCost().trim() ? Number(this.freightCost()) : null,
        totalValue: this.totalValueEditable() && this.totalValue().trim() ? Number(this.totalValue()) : null,
        mode: this.mode().trim() || null,
        awbOrBl: this.awbOrBl().trim() || null,
        eta: toIsoOrNull(this.eta()),
        lines: lineRequests
      };
      this.submit(request);
    }
  }

  /** D-35's explicit override — retries the exact prior request with `allowNegativeStock: true`. Never fired automatically. */
  confirmOverride(): void {
    if (!this.pendingRequest) return;
    const retryRequest = { ...this.pendingRequest, allowNegativeStock: true };
    this.insufficientStock.set(null);
    this.submit(retryRequest as CreateShipmentRequest | UpdateShipmentRequest, true);
  }

  private submit(request: CreateShipmentRequest | UpdateShipmentRequest, isRetry = false): void {
    this.saving.set(true);
    if (!isRetry) this.pendingRequest = request;
    const existing = this.shipment;
    const request$ = existing
      ? this.shipmentsService.update(existing.id, request as UpdateShipmentRequest)
      : this.shipmentsService.create(request as CreateShipmentRequest);
    request$.subscribe({
      next: (result) => {
        this.saving.set(false);
        this.pendingRequest = null;
        this.saved.emit(result);
      },
      error: (err: unknown) => {
        this.saving.set(false);
        if (err instanceof HttpErrorResponse && err.status === 409) {
          const body = err.error as InsufficientStockProblemDetails | undefined;
          if (body?.insufficientStock?.length) {
            this.error.set(body.detail ?? 'Recording this shipment would drive stock negative for one or more items.');
            this.insufficientStock.set(body.insufficientStock);
            return;
          }
        }
        this.error.set(extractErrorMessage(err, 'Could not save this shipment. Please try again.'));
      }
    });
  }

  /** Returns null (invalid) rather than an empty array when a staged line is incomplete — freight-only's genuinely-empty `[]` is a different, valid case (D-36). */
  private buildLineRequests(): CreateShipmentLineRequest[] | null {
    if (!this.showsLines()) return [];
    const rows = this.lines();
    const result: CreateShipmentLineRequest[] = [];
    for (const row of rows) {
      const qty = Number(row.quantity);
      if (!row.inventoryItemId || !row.quantity.trim() || Number.isNaN(qty) || qty <= 0) {
        return null;
      }
      result.push({ inventoryItemId: row.inventoryItemId, quantity: qty });
    }
    return result;
  }

  private loadCustomers(): void {
    this.customersLoading.set(true);
    this.customersService.list({ page: 1, pageSize: 200 }).subscribe({
      next: (res) => {
        this.customersLoading.set(false);
        this.customerOptions.set(res.items.map((c) => ({ id: c.id, name: c.businessName })));
      },
      error: () => this.customersLoading.set(false)
    });
  }

  private loadInventory(): void {
    this.inventoryLoading.set(true);
    this.inventoryService.list({ page: 1, pageSize: 200 }).subscribe({
      next: (res) => {
        this.inventoryLoading.set(false);
        this.inventoryOptions.set(res.items);
      },
      error: () => this.inventoryLoading.set(false)
    });
  }
}

/** `<input type="date">` expects `yyyy-MM-dd`; the API's dispatch/ETA fields are full ISO-8601 with time (see `shipment.models.ts`'s module doc). */
function toDateInputValue(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  return d.toISOString().slice(0, 10);
}

function toIsoOrNull(dateInputValue: string): string | null {
  if (!dateInputValue) return null;
  return new Date(`${dateInputValue}T00:00:00.000Z`).toISOString();
}
