import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { AuthService } from '../core/services/auth.service';
import { MasterDataService } from '../core/services/master-data.service';
import { extractErrorMessage } from '../core/services/problem-details.util';
import { StatusStyleService } from '../shared/services/status-style.service';
import { VendorsService } from './services/vendors.service';
import { VendorDetail, VendorListItem } from './models/vendor.models';
import { VendorFormDialogComponent } from './vendor-form-dialog/vendor-form-dialog.component';

interface VendorRow {
  id: string;
  name: string;
  contactPerson: string;
  phone: string;
  region: string;
  categoriesLabel: string;
  termsLabel: string;
  catalogCount: number;
  statusLabel: string;
  stBg: string;
  stFg: string;
}

const PAGE_SIZE = 25;
const ALL = '';

/**
 * Vendors list (ACTION_PLAN E5-08) — ported from Source/Sourcing Ops
 * Platform.dc.html `showVendors` (~line 489). Search + category/status
 * filters call `GET /vendors` (E5-04); the "+ Add Vendor" button opens the
 * shared `VendorFormDialogComponent` in create mode (`POST /vendors`,
 * E5-01) — the prototype's button has no wired action of its own since it's
 * a static mock, but the endpoint exists, so this isn't left inert like
 * customer-detail's edit action was.
 */
@Component({
  selector: 'app-vendors',
  standalone: true,
  imports: [RouterLink, VendorFormDialogComponent],
  templateUrl: './vendors.component.html',
  styleUrl: './vendors.component.scss'
})
export class VendorsComponent {
  private readonly vendorsService = inject(VendorsService);
  private readonly masterDataService = inject(MasterDataService);
  private readonly styles = inject(StatusStyleService);
  private readonly auth = inject(AuthService);

  readonly categoryOptions = toSignal(this.masterDataService.categoryOptions(), { initialValue: [] });
  readonly vendorStatusOptions = toSignal(this.masterDataService.vendorStatusOptions(), { initialValue: [] });

  readonly canEdit = computed(() => this.auth.hasPermission('Vendors.Edit'));

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  private readonly items = signal<VendorListItem[]>([]);
  readonly totalCount = signal(0);

  readonly search = signal('');
  readonly categoryId = signal(ALL);
  readonly statusId = signal(ALL);
  readonly page = signal(1);

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)));

  readonly rows = computed<VendorRow[]>(() => this.items().map((item) => this.toRow(item)));
  readonly noResults = computed(() => !this.loading() && !this.error() && this.rows().length === 0);

  readonly createOpen = signal(false);

  private readonly search$ = new Subject<string>();

  constructor() {
    this.masterDataService.ensureLoaded().subscribe({ error: () => {} });

    this.search$.pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed()).subscribe((value) => {
      this.search.set(value);
      this.page.set(1);
      this.fetch();
    });

    this.fetch();
  }

  onSearchInput(value: string): void {
    this.search$.next(value);
  }

  setCategory(value: string): void {
    this.categoryId.set(value);
    this.page.set(1);
    this.fetch();
  }

  setStatus(value: string): void {
    this.statusId.set(value);
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

  openCreate(): void {
    this.createOpen.set(true);
  }

  cancelCreate(): void {
    this.createOpen.set(false);
  }

  onVendorCreated(_vendor: VendorDetail): void {
    this.createOpen.set(false);
    this.fetch();
  }

  private fetch(): void {
    this.loading.set(true);
    this.error.set(null);
    this.vendorsService
      .list({
        search: this.search() || undefined,
        page: this.page(),
        pageSize: PAGE_SIZE,
        categoryId: this.categoryId() || undefined,
        statusId: this.statusId() || undefined
      })
      .subscribe({
        next: (res) => {
          this.items.set(res.items);
          this.totalCount.set(res.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.error.set(extractErrorMessage(err, 'Could not load vendors. Please try again.'));
        }
      });
  }

  private toRow(item: VendorListItem): VendorRow {
    const st = this.styles.status(item.status.code);
    const terms = [item.moq, item.leadTime].filter(Boolean).join(' / ');
    return {
      id: item.id,
      name: item.name,
      contactPerson: item.contactPerson ?? '—',
      phone: item.phone ?? '—',
      region: item.region ?? '—',
      categoriesLabel: item.categories.length ? item.categories.map((c) => c.name).join(', ') : '—',
      termsLabel: terms || '—',
      catalogCount: item.catalogCount,
      statusLabel: item.status.label,
      stBg: st.bg,
      stFg: st.fg
    };
  }
}
