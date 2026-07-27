import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import { AuthService } from '../services/auth.service';

interface NavItem {
  id: string;
  icon: string;
  label: string;
  route: string;
  permission?: string;
}

interface NavGroup {
  label: string;
  items: NavItem[];
}

/**
 * Ported 1:1 from the prototype's sidebar + topbar (Source/Sourcing Ops
 * Platform.dc.html, lines ~22-58) — same DOM structure, same computed style
 * values from docs/DESIGN_TOKENS.md. The prototype's `mobile` reference
 * screen is a design reference only (TECH_SPEC §5.2) and is not ported as a
 * nav entry / route. The command palette (⌘K) and the topbar refresh action
 * are visually ported but intentionally inert pending ACTION_PLAN E12-05.
 */
@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss'
})
export class ShellComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly activatedRoute = inject(ActivatedRoute);

  readonly sidebarOpen = signal(true);

  readonly pageTitle = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      startWith(null),
      map(() => {
        let route = this.activatedRoute.snapshot;
        while (route.firstChild) route = route.firstChild;
        return (route.data['title'] as string) ?? '';
      })
    ),
    { initialValue: '' }
  );

  private readonly allGroups: NavGroup[] = [
    { label: 'MAIN', items: [{ id: 'dash', icon: '📊', label: 'Dashboard', route: '/dashboard', permission: 'Analytics.View' }] },
    {
      label: 'CRM',
      items: [
        { id: 'customers', icon: '👥', label: 'Customers', route: '/customers', permission: 'Customers.View' },
        { id: 'intake', icon: '📝', label: 'New Lead Intake', route: '/customers/intake', permission: 'Customers.Edit' }
      ]
    },
    {
      label: 'SOURCING',
      items: [
        { id: 'vendors', icon: '🏭', label: 'Vendors', route: '/vendors', permission: 'Vendors.View' },
        { id: 'catalogs', icon: '📚', label: 'Catalogs', route: '/catalogs', permission: 'Catalogs.View' }
      ]
    },
    {
      label: 'OPERATIONS',
      items: [
        { id: 'inventory', icon: '📦', label: 'Inventory', route: '/inventory', permission: 'Inventory.View' },
        { id: 'shipments', icon: '🚚', label: 'Shipments', route: '/shipments', permission: 'Shipments.View' }
      ]
    },
    {
      // Invoicing is a DESIGN PREVIEW on mocked data this pass — there is no
      // invoicing backend (ACTION_PLAN §12.3). The nav entry exists so the
      // design is reachable for review; E8-09/E8-10 remain open.
      label: 'BILLING',
      items: [{ id: 'invoices', icon: '🧾', label: 'Invoices', route: '/invoices', permission: 'Invoicing.View' }]
    },
    {
      // Admin.* is SuperAdmin-only in the seeded role map, so this whole group
      // is invisible to an Associate via the existing permission filter below.
      label: 'ADMIN',
      items: [
        { id: 'admin-users', icon: '👤', label: 'Users', route: '/admin/users', permission: 'Admin.ManageUsers' },
        {
          id: 'admin-master-data',
          icon: '⚙️',
          label: 'Master Data',
          route: '/admin/master-data',
          permission: 'Admin.ManageMasterData'
        }
      ]
    }
  ];

  /** Nav entries render only for permissions present in the token (TECH_SPEC §5.3). */
  readonly navGroups = computed<NavGroup[]>(() =>
    this.allGroups
      .map((g) => ({ label: g.label, items: g.items.filter((it) => !it.permission || this.auth.hasPermission(it.permission)) }))
      .filter((g) => g.items.length > 0)
  );

  readonly userName = toSignal(this.auth.currentUser$.pipe(map((u) => u?.name ?? '')), { initialValue: '' });
  readonly userRoleLabel = toSignal(this.auth.currentUser$.pipe(map((u) => u?.roles?.[0] ?? '')), { initialValue: '' });

  readonly userInitials = computed(() => {
    const name = this.userName();
    if (!name) return '?';
    return name
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map((p) => p[0]?.toUpperCase())
      .join('');
  });

  toggleSidebar(): void {
    this.sidebarOpen.update((v) => !v);
  }

  logout(): void {
    this.auth.logout();
  }
}
