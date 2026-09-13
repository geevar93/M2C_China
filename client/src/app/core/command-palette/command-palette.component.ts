import { Component, ElementRef, computed, effect, inject, output, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { Observable, Subject, debounceTime, distinctUntilChanged, forkJoin, map, of, switchMap, catchError } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { CustomersService } from '../../customers/services/customers.service';
import { VendorsService } from '../../vendors/services/vendors.service';
import { CatalogsService } from '../../catalogs/services/catalogs.service';
import { ShipmentsService } from '../../shipments/services/shipments.service';
import { InventoryService } from '../../inventory/services/inventory.service';

export interface PaletteResult {
  icon: string;
  label: string;
  sub: string;
  kind: 'Customer' | 'Vendor' | 'Catalog' | 'Shipment' | 'Item' | 'Screen';
  route: string[];
  queryParams?: Record<string, string>;
}

interface ScreenEntry {
  icon: string;
  label: string;
  sub: string;
  route: string;
  permission: string;
  keywords?: string;
}

const SCREENS: ScreenEntry[] = [
  { icon: '📊', label: 'Dashboard', sub: 'Leads, service split, shipments', route: '/dashboard', permission: 'Analytics.View' },
  { icon: '👥', label: 'Customers', sub: 'All customers and leads', route: '/customers', permission: 'Customers.View' },
  { icon: '📝', label: 'New Lead Intake', sub: 'Log a WhatsApp or phone enquiry', route: '/customers/intake', permission: 'Customers.Edit' },
  { icon: '⏰', label: 'Due Follow-ups', sub: 'Reminders that are due', route: '/customers/follow-ups', permission: 'Customers.View' },
  { icon: '🏭', label: 'Vendors', sub: 'Supplier directory', route: '/vendors', permission: 'Vendors.View' },
  { icon: '📚', label: 'Catalogs', sub: 'Catalog sections and documents', route: '/catalogs', permission: 'Catalogs.View' },
  { icon: '📦', label: 'Inventory', sub: 'Stock on hand and reorder levels', route: '/inventory', permission: 'Inventory.View', keywords: 'stock' },
  { icon: '🚚', label: 'Shipments', sub: 'Outbound shipments and status', route: '/shipments', permission: 'Shipments.View' },
  { icon: '🧾', label: 'Invoices', sub: 'Billing', route: '/invoices', permission: 'Invoicing.View' },
  { icon: '👤', label: 'Users', sub: 'Admin · manage accounts', route: '/admin/users', permission: 'Admin.ManageUsers' },
  { icon: '⚙️', label: 'Master Data', sub: 'Admin · lookups and statuses', route: '/admin/master-data', permission: 'Admin.ManageMasterData' },
  { icon: '🏢', label: 'Company Settings', sub: 'Admin · invoicing identity', route: '/admin/company-settings', permission: 'Admin.ManageMasterData' },
  { icon: '🔑', label: 'Change password', sub: 'Your account', route: '/account/password', permission: 'Account.ChangeOwnPassword' }
];

/** Results per entity source. Five keeps the fan-out cheap and the list scannable. */
const PER_SOURCE = 5;

/**
 * Global command palette (ACTION_PLAN E12-05, ⌘K / Ctrl+K).
 *
 * There is no dedicated search endpoint, so the palette fans a debounced query
 * out to the existing list endpoints — customers, vendors, catalog sections,
 * shipments, inventory — each of which already accepts `search`, and merges the
 * first few hits from each with the matching screens. Sources the user lacks
 * the permission for are skipped, mirroring the sidebar's gating.
 *
 * Opening the palette is the shell's job; this component owns the query,
 * results and keyboard handling, and emits `closed` when it should go away.
 */
@Component({
  selector: 'app-command-palette',
  standalone: true,
  templateUrl: './command-palette.component.html',
  styleUrl: './command-palette.component.scss'
})
export class CommandPaletteComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly customers = inject(CustomersService);
  private readonly vendors = inject(VendorsService);
  private readonly catalogs = inject(CatalogsService);
  private readonly shipments = inject(ShipmentsService);
  private readonly inventory = inject(InventoryService);

  readonly closed = output<void>();

  readonly query = signal('');
  readonly loading = signal(false);
  readonly activeIndex = signal(0);
  private readonly remote = signal<PaletteResult[]>([]);

  private readonly input = viewChild<ElementRef<HTMLInputElement>>('paletteInput');
  private readonly query$ = new Subject<string>();

  private readonly screens: PaletteResult[] = SCREENS.filter((s) => this.auth.hasPermission(s.permission)).map((s) => ({
    icon: s.icon,
    label: s.label,
    sub: s.sub,
    kind: 'Screen',
    route: [s.route]
  }));

  readonly results = computed<PaletteResult[]>(() => {
    const q = this.query().trim().toLowerCase();
    if (!q) return this.screens;
    const screenHits = this.screens.filter((s) => {
      const entry = SCREENS.find((e) => e.label === s.label);
      return `${s.label} ${s.sub} ${entry?.keywords ?? ''}`.toLowerCase().includes(q);
    });
    return [...this.remote(), ...screenHits];
  });

  readonly empty = computed(() => !this.loading() && this.query().trim() !== '' && this.results().length === 0);

  constructor() {
    this.query$
      .pipe(
        map((q) => q.trim()),
        debounceTime(220),
        distinctUntilChanged(),
        switchMap((q) => {
          if (!q) {
            this.loading.set(false);
            return of<PaletteResult[]>([]);
          }
          this.loading.set(true);
          return this.searchAll(q);
        }),
        takeUntilDestroyed()
      )
      .subscribe((items) => {
        this.remote.set(items);
        this.loading.set(false);
        this.activeIndex.set(0);
      });

    effect(() => {
      const el = this.input()?.nativeElement;
      if (el) queueMicrotask(() => el.focus());
    });
  }

  onInput(value: string): void {
    this.query.set(value);
    this.activeIndex.set(0);
    if (!value.trim()) this.remote.set([]);
    this.query$.next(value);
  }

  onKeydown(event: KeyboardEvent): void {
    const count = this.results().length;
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        if (count) this.activeIndex.set((this.activeIndex() + 1) % count);
        break;
      case 'ArrowUp':
        event.preventDefault();
        if (count) this.activeIndex.set((this.activeIndex() - 1 + count) % count);
        break;
      case 'Enter': {
        event.preventDefault();
        const item = this.results()[this.activeIndex()];
        if (item) this.go(item);
        break;
      }
      case 'Escape':
        event.preventDefault();
        this.close();
        break;
    }
  }

  setActive(index: number): void {
    this.activeIndex.set(index);
  }

  go(item: PaletteResult): void {
    this.router.navigate(item.route, { queryParams: item.queryParams });
    this.close();
  }

  close(): void {
    this.closed.emit();
  }

  private searchAll(q: string): Observable<PaletteResult[]> {
    const sources: Observable<PaletteResult[]>[] = [];
    const page = { search: q, page: 1, pageSize: PER_SOURCE };

    if (this.auth.hasPermission('Customers.View')) {
      sources.push(
        this.customers.list(page).pipe(
          map((r) =>
            r.items.map<PaletteResult>((c) => ({
              icon: '👥',
              label: c.businessName || c.name,
              sub: [c.name, c.city, c.phone].filter(Boolean).join(' · '),
              kind: 'Customer',
              route: ['/customers', c.id]
            }))
          )
        )
      );
    }
    if (this.auth.hasPermission('Vendors.View')) {
      sources.push(
        this.vendors.list(page).pipe(
          map((r) =>
            r.items.map<PaletteResult>((v) => ({
              icon: '🏭',
              label: v.name,
              sub: [v.region, v.categories.map((c) => c.name).join(', ')].filter(Boolean).join(' · '),
              kind: 'Vendor',
              route: ['/vendors', v.id]
            }))
          )
        )
      );
    }
    if (this.auth.hasPermission('Catalogs.View')) {
      sources.push(
        this.catalogs.list(page).pipe(
          map((r) =>
            r.items.map<PaletteResult>((s) => ({
              icon: '📚',
              label: s.title,
              sub: [s.vendorName, s.category?.name, s.documents[0]?.originalFilename].filter(Boolean).join(' · '),
              kind: 'Catalog',
              route: ['/catalogs'],
              queryParams: { q: s.title }
            }))
          )
        )
      );
    }
    if (this.auth.hasPermission('Shipments.View')) {
      sources.push(
        this.shipments.list(page).pipe(
          map((r) =>
            r.items.map<PaletteResult>((s) => ({
              icon: '🚚',
              label: s.reference ?? 'Shipment',
              sub: [s.customer?.name, s.destination, s.status?.label].filter(Boolean).join(' · '),
              kind: 'Shipment',
              route: ['/shipments', s.id]
            }))
          )
        )
      );
    }
    if (this.auth.hasPermission('Inventory.View')) {
      sources.push(
        this.inventory.list(page).pipe(
          map((r) =>
            r.items.map<PaletteResult>((i) => ({
              icon: '📦',
              label: i.name,
              sub: [i.sku, i.category?.name, `${i.onHandQty} ${i.unit} on hand`].filter(Boolean).join(' · '),
              kind: 'Item',
              route: ['/inventory'],
              queryParams: { q: i.sku || i.name }
            }))
          )
        )
      );
    }

    if (sources.length === 0) return of([]);
    // A failing source must not blank the others — it just contributes nothing.
    return forkJoin(sources.map((s) => s.pipe(catchError(() => of<PaletteResult[]>([]))))).pipe(map((groups) => groups.flat()));
  }
}
