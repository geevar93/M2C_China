import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { MasterDataService } from '../core/services/master-data.service';
import { MasterDataResponse } from '../core/models/master-data.models';
import { extractErrorMessage } from '../core/services/problem-details.util';
import { StatusStyleService } from '../shared/services/status-style.service';
import { CustomersService } from './services/customers.service';
import { CustomerListItem } from './models/customer.models';
import { RefreshService } from '../core/services/refresh.service';

interface CustomerRow {
  id: string;
  business: string;
  name: string;
  city: string;
  phone: string;
  svcLabel: string;
  svcBg: string;
  svcFg: string;
  statusLabel: string;
  stBg: string;
  stFg: string;
  categoriesLabel: string;
  owner: string;
  tags: string[];
}

const PAGE_SIZE = 25;
const ALL = '';

/**
 * Customers list (ACTION_PLAN E4-14) — ported from Source/Sourcing Ops
 * Platform.dc.html `showCustomers` (~line 173). Search + service-type/status/
 * category filters call `GET /customers`; the prototype's `custCif`/
 * `custFreight` summary counts are dropped (see class doc) because the
 * paged list endpoint doesn't return a same-shape aggregate — computing them
 * from only the current page would be misleading.
 */
@Component({
  selector: 'app-customers',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './customers.component.html',
  styleUrl: './customers.component.scss'
})
export class CustomersComponent {
  private readonly customersService = inject(CustomersService);
  private readonly masterDataService = inject(MasterDataService);
  private readonly styles = inject(StatusStyleService);

  private readonly masterData = toSignal(this.masterDataService.masterData$, {
    initialValue: { status: 'idle' as const, data: null, error: null }
  });

  readonly serviceTypeOptions = toSignal(this.masterDataService.serviceTypeOptions(), { initialValue: [] });
  readonly leadStatusOptions = toSignal(this.masterDataService.leadStatusOptions(), { initialValue: [] });
  readonly categoryOptions = toSignal(this.masterDataService.categoryOptions(), { initialValue: [] });

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  private readonly items = signal<CustomerListItem[]>([]);
  readonly totalCount = signal(0);

  readonly search = signal('');
  readonly serviceTypeId = signal(ALL);
  readonly statusId = signal(ALL);
  readonly categoryId = signal(ALL);
  readonly tag = signal(ALL);
  readonly page = signal(1);

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)));

  readonly rows = computed<CustomerRow[]>(() => {
    const md = this.masterData().data;
    return this.items().map((item) => this.toRow(item, md));
  });

  readonly noResults = computed(() => !this.loading() && !this.error() && this.rows().length === 0);

  private readonly search$ = new Subject<string>();
  private readonly tag$ = new Subject<string>();

  private readonly refreshService = inject(RefreshService);

  constructor() {
    // Topbar "Refresh" reloads this screen the same way its Retry control does.
    this.refreshService.onRefresh(() => this.retry());

    this.masterDataService.ensureLoaded().subscribe({ error: () => {} });

    this.search$
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((value) => {
        this.search.set(value);
        this.page.set(1);
        this.fetch();
      });

    this.tag$
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((value) => {
        this.tag.set(value);
        this.page.set(1);
        this.fetch();
      });

    this.fetch();
  }

  onSearchInput(value: string): void {
    this.search$.next(value);
  }

  onTagInput(value: string): void {
    this.tag$.next(value);
  }

  /** Set directly (no debounce) — used when the user clicks a tag chip on a row, per E4-11. */
  setTag(value: string): void {
    this.tag.set(value);
    this.page.set(1);
    this.fetch();
  }

  clearTag(): void {
    this.setTag(ALL);
  }

  setServiceType(value: string): void {
    this.serviceTypeId.set(value);
    this.page.set(1);
    this.fetch();
  }

  setStatus(value: string): void {
    this.statusId.set(value);
    this.page.set(1);
    this.fetch();
  }

  setCategory(value: string): void {
    this.categoryId.set(value);
    this.page.set(1);
    this.fetch();
  }

  clearFilters(): void {
    this.search.set('');
    this.serviceTypeId.set(ALL);
    this.statusId.set(ALL);
    this.categoryId.set(ALL);
    this.tag.set(ALL);
    this.page.set(1);
    this.fetch();
  }

  prevPage(): void {
    if (this.page() <= 1) return;
    this.page.update((p) => p - 1);
    this.fetch();
  }

  nextPage(): void {
    if (this.page() >= this.totalPages()) return;
    this.page.update((p) => p + 1);
    this.fetch();
  }

  retry(): void {
    this.fetch();
  }

  private fetch(): void {
    this.loading.set(true);
    this.error.set(null);
    this.customersService
      .list({
        search: this.search() || undefined,
        page: this.page(),
        pageSize: PAGE_SIZE,
        serviceTypeId: this.serviceTypeId() || undefined,
        statusId: this.statusId() || undefined,
        categoryId: this.categoryId() || undefined,
        tag: this.tag() || undefined
      })
      .subscribe({
        next: (res) => {
          this.items.set(res.items);
          this.totalCount.set(res.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.error.set(extractErrorMessage(err, 'Could not load customers. Please try again.'));
        }
      });
  }

  private toRow(item: CustomerListItem, md: MasterDataResponse | null): CustomerRow {
    const svcRow = md?.serviceTypes.find((r) => r.id === item.serviceTypeId);
    const svc = this.styles.serviceType(svcRow?.code);
    const statusRow = md?.leadStatuses.find((r) => r.id === item.statusId);
    const st = this.styles.status(statusRow?.code);
    const catNames = item.categoryIds
      .map((id) => md?.categories.find((c) => c.id === id)?.name)
      .filter((name): name is string => !!name);

    return {
      id: item.id,
      business: item.businessName || item.name,
      name: item.name,
      city: item.city ?? '—',
      phone: item.phone,
      svcLabel: svc.label,
      svcBg: svc.bg,
      svcFg: svc.fg,
      statusLabel: statusRow?.label ?? '—',
      stBg: st.bg,
      stFg: st.fg,
      categoriesLabel: catNames.length ? catNames.join(', ') : '—',
      owner: item.ownerName ?? 'Unassigned',
      tags: item.tags
    };
  }
}
