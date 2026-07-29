import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { MasterDataService } from '../../core/services/master-data.service';
import { extractErrorMessage } from '../../core/services/problem-details.util';
import { StatusStyleService } from '../../shared/services/status-style.service';
import { TimelineStyleService } from '../../shared/services/timeline-style.service';
import { TimelineDatePipe } from '../../shared/pipes/timeline-date.pipe';
import { DispatchCustomerLock, DispatchDialogComponent } from '../../dispatch/dispatch-dialog/dispatch-dialog.component';
import { CustomersService } from '../services/customers.service';
import { CustomerDetail, TimelineEvent } from '../models/customer.models';

interface ProfileField {
  k: string;
  v: string;
}

interface TimelineRow extends TimelineEvent {
  dot: string;
}

/**
 * Customer detail (ACTION_PLAN E4-15) — ported from Source/Sourcing Ops
 * Platform.dc.html `showCustDetail` (~line 386). Two of the prototype's four
 * cards are intentionally dropped, both flagged here rather than faked:
 *  - "Catalog Dispatch Log" — the real TimelineEvent feed already carries
 *    `CatalogDispatched` entries; keeping a second, separate dispatch list
 *    would just duplicate the timeline against a data source this pass
 *    doesn't have (dispatch logging is E9/M4).
 *  - "Shipments" — no shipments endpoint exists in this pass's contract
 *    (shipments are M5); rendering it would mean fabricating rows, which the
 *    task brief explicitly says not to do.
 * "Edit" is ported as a visually-present but inert control (a later CRM
 * story). "Send Catalog via WhatsApp" is wired in this pass (E9-03) to the
 * shared `DispatchDialogComponent`, entered with the customer fixed
 * (`customerLock`) so the dialog only needs a catalog document picked. A
 * successful dispatch reloads the timeline rather than a second dispatch
 * list — per this class's own note above, `CatalogDispatched` timeline
 * entries are the one and only dispatch history surface on this screen.
 */
@Component({
  selector: 'app-customer-detail',
  standalone: true,
  imports: [RouterLink, TimelineDatePipe, DispatchDialogComponent],
  templateUrl: './customer-detail.component.html',
  styleUrl: './customer-detail.component.scss'
})
export class CustomerDetailComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly customersService = inject(CustomersService);
  private readonly masterDataService = inject(MasterDataService);
  private readonly styles = inject(StatusStyleService);
  private readonly timelineStyles = inject(TimelineStyleService);
  private readonly auth = inject(AuthService);

  readonly canDispatch = computed(() => this.auth.hasPermission('Dispatch.Send'));
  readonly dispatchOpen = signal(false);

  private readonly masterData = toSignal(this.masterDataService.masterData$, {
    initialValue: { status: 'idle' as const, data: null, error: null }
  });

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly customer = signal<CustomerDetail | null>(null);
  private readonly timelineEvents = signal<TimelineEvent[]>([]);

  readonly noteOpen = signal(false);
  readonly noteText = signal('');
  readonly savingNote = signal(false);
  readonly noteError = signal<string | null>(null);

  readonly timeline = computed<TimelineRow[]>(() =>
    this.timelineEvents().map((event) => ({
      ...event,
      dot: this.timelineStyles.dotColor(event.kind)
    }))
  );

  readonly svcChip = computed(() => {
    const c = this.customer();
    const md = this.masterData().data;
    const row = md?.serviceTypes.find((r) => r.id === c?.serviceTypeId);
    return this.styles.serviceType(row?.code);
  });

  readonly statusChip = computed(() => {
    const c = this.customer();
    const md = this.masterData().data;
    const row = md?.leadStatuses.find((r) => r.id === c?.statusId);
    return { ...this.styles.status(row?.code), label: row?.label ?? '—' };
  });

  readonly dispatchCustomerLock = computed<DispatchCustomerLock | null>(() => {
    const c = this.customer();
    if (!c) return null;
    const md = this.masterData().data;
    const row = md?.serviceTypes.find((r) => r.id === c.serviceTypeId);
    return {
      id: c.id,
      businessName: c.businessName,
      subline: `${c.name} · ${c.phone}`,
      serviceTypeCode: row?.code ?? null
    };
  });

  readonly profileFields = computed<ProfileField[]>(() => {
    const c = this.customer();
    if (!c) return [];
    const md = this.masterData().data;
    const catNames = c.categoryIds
      .map((id) => md?.categories.find((cat) => cat.id === id)?.name)
      .filter((n): n is string => !!n);

    const fields: ProfileField[] = [
      { k: 'Business Name', v: c.businessName },
      { k: 'Contact Name', v: c.name },
      { k: 'Phone', v: c.phone },
      { k: 'Email', v: c.email ?? '—' },
      { k: 'City', v: c.city ?? '—' },
      { k: 'Source Channel', v: c.sourceChannel },
      { k: 'Categories', v: catNames.length ? catNames.join(', ') : '—' },
      { k: 'Owner', v: c.ownerName ?? 'Unassigned' },
      { k: 'Tags', v: c.tags.length ? c.tags.join(', ') : '—' },
      { k: 'Notes', v: c.notes ?? '—' }
    ];

    if (this.svcChip().label === 'FREIGHT-ONLY') {
      fields.push(
        { k: 'External Marketplace', v: c.externalMarketplace ?? '—' },
        { k: 'External Order Ref', v: c.externalOrderRef ?? '—' },
        { k: 'External Supplier', v: c.externalSupplierName ?? '—' },
        { k: 'External Order Value', v: c.externalOrderValue != null ? `${c.externalOrderCurrency ?? ''} ${c.externalOrderValue}`.trim() : '—' }
      );
    }
    return fields;
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

  openDispatch(): void {
    this.dispatchOpen.set(true);
  }

  cancelDispatch(): void {
    this.dispatchOpen.set(false);
  }

  onDispatchLogged(): void {
    this.dispatchOpen.set(false);
    const c = this.customer();
    if (c) this.reloadTimeline(c.id);
  }

  openNote(): void {
    this.noteOpen.set(true);
    this.noteText.set('');
    this.noteError.set(null);
  }

  cancelNote(): void {
    this.noteOpen.set(false);
    this.noteText.set('');
  }

  saveNote(): void {
    const text = this.noteText().trim();
    const c = this.customer();
    if (!text || !c || this.savingNote()) return;

    this.savingNote.set(true);
    this.noteError.set(null);
    this.customersService.addInteraction(c.id, { type: 'Note', text }).subscribe({
      next: () => {
        this.savingNote.set(false);
        this.noteOpen.set(false);
        this.noteText.set('');
        this.reloadTimeline(c.id);
      },
      error: (err: unknown) => {
        this.savingNote.set(false);
        this.noteError.set(extractErrorMessage(err, 'Could not save this note. Please try again.'));
      }
    });
  }

  private load(id: string): void {
    this.loading.set(true);
    this.error.set(null);
    forkJoin({
      customer: this.customersService.getById(id),
      timeline: this.customersService.getTimeline(id)
    }).subscribe({
      next: ({ customer, timeline }) => {
        this.customer.set(customer);
        this.timelineEvents.set(timeline);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.error.set(extractErrorMessage(err, 'Could not load this customer. Please try again.'));
      }
    });
  }

  private reloadTimeline(id: string): void {
    this.customersService.getTimeline(id).subscribe({
      next: (timeline) => this.timelineEvents.set(timeline),
      error: () => {}
    });
  }
}
