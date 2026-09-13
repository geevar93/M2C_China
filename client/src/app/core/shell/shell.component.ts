import { Component, HostListener, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { MasterDataService } from '../services/master-data.service';
import { RefreshService } from '../services/refresh.service';
import { CommandPaletteComponent } from '../command-palette/command-palette.component';

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
 * App shell: sidebar + topbar around the routed screen.
 *
 * Desktop keeps the sidebar in the flow (collapsible to an icon rail); at
 * tablet width and below it becomes an off-canvas drawer opened from the
 * topbar hamburger and closed by the scrim, the close glyph or any navigation.
 *
 * The topbar refresh action broadcasts through RefreshService to the screen
 * on display (each screen registers the reload behind its Retry button) and
 * reloads shared master data. The command palette (⌘K / Ctrl+K, or the topbar
 * search trigger) is CommandPaletteComponent (ACTION_PLAN E12-05).
 */
@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, CommandPaletteComponent],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss'
})
export class ShellComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly activatedRoute = inject(ActivatedRoute);
  private readonly refreshService = inject(RefreshService);
  private readonly masterData = inject(MasterDataService);

  readonly sidebarOpen = signal(true);
  readonly mobileNavOpen = signal(false);
  readonly userMenuOpen = signal(false);
  readonly paletteOpen = signal(false);
  readonly refreshing = this.refreshService.busy;

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
        },
        {
          id: 'admin-company-settings',
          icon: '🏢',
          label: 'Company Settings',
          route: '/admin/company-settings',
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

  /**
   * The user menu's "Change password" entry. Read through a computed so the
   * template never calls the service directly, matching how `navGroups` gates
   * nav items. Permissions are fixed for the life of a token, so there is
   * nothing to react to here — the shell is re-created on login.
   */
  readonly canChangeOwnPassword = computed(() => this.auth.hasPermission('Account.ChangeOwnPassword'));

  /** ⌘K / Ctrl+K opens the palette from anywhere in the shell. */
  @HostListener('document:keydown', ['$event'])
  onDocumentKeydown(event: KeyboardEvent): void {
    if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      this.openPalette();
    }
  }

  openPalette(): void {
    this.closeUserMenu();
    this.closeMobileNav();
    this.paletteOpen.set(true);
  }

  closePalette(): void {
    this.paletteOpen.set(false);
  }

  toggleSidebar(): void {
    this.sidebarOpen.update((v) => !v);
  }

  openMobileNav(): void {
    this.mobileNavOpen.set(true);
  }

  closeMobileNav(): void {
    this.mobileNavOpen.set(false);
  }

  toggleUserMenu(): void {
    this.userMenuOpen.update((v) => !v);
  }

  closeUserMenu(): void {
    this.userMenuOpen.set(false);
  }

  /**
   * Reloads the current screen's data through RefreshService (each data screen
   * registers the reload behind its Retry control) and refreshes the shared
   * master-data lookups so admin edits show up in the current screen's pickers.
   */
  refresh(): void {
    this.masterData.reload().subscribe({ error: () => {} });
    this.refreshService.requestRefresh();
  }

  logout(): void {
    this.closeUserMenu();
    this.auth.logout();
  }
}
