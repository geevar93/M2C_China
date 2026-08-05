import { Routes } from '@angular/router';
import { authGuard, guestGuard, mustChangePasswordGuard } from './core/guards/auth.guard';
import { permissionGuard } from './core/guards/permission.guard';

/**
 * Route table (ACTION_PLAN E1-10/E1-12/E1-13/E1-14). Every feature route
 * under the shell is permission-guarded from `data.permission`, matching the
 * codes in the TECH_SPEC §4.3 permission catalog. Feature screens themselves
 * are placeholders this pass — see each component for its target milestone.
 */
export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./auth/login/login.component').then((m) => m.LoginComponent),
    data: { title: 'Sign in' }
  },
  {
    path: 'force-change-password',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./auth/force-change-password/force-change-password.component').then((m) => m.ForceChangePasswordComponent),
    data: { title: 'Set a new password' }
  },
  {
    path: '',
    canActivate: [authGuard, mustChangePasswordGuard],
    loadComponent: () => import('./core/shell/shell.component').then((m) => m.ShellComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        canActivate: [permissionGuard],
        data: { title: 'Dashboard', permission: 'Analytics.View' },
        loadComponent: () => import('./dashboard/dashboard.component').then((m) => m.DashboardComponent)
      },
      {
        path: 'customers',
        canActivate: [permissionGuard],
        data: { title: 'Customers', permission: 'Customers.View' },
        loadComponent: () => import('./customers/customers.component').then((m) => m.CustomersComponent)
      },
      {
        path: 'customers/intake',
        canActivate: [permissionGuard],
        data: { title: 'New Lead Intake', permission: 'Customers.Edit' },
        loadComponent: () => import('./customers/intake.component').then((m) => m.CustomerIntakeComponent)
      },
      {
        // Must stay above 'customers/:id' — a literal segment declared after
        // the param route would never be reached, since ':id' matches first
        // and the follow-ups screen would 404 trying to load customer id
        // "follow-ups" (ACTION_PLAN E4-08).
        path: 'customers/follow-ups',
        canActivate: [permissionGuard],
        data: { title: 'Due Follow-ups', permission: 'Customers.View' },
        loadComponent: () => import('./customers/follow-ups/follow-ups.component').then((m) => m.FollowUpsComponent)
      },
      {
        path: 'customers/:id',
        canActivate: [permissionGuard],
        data: { title: 'Customer', permission: 'Customers.View' },
        loadComponent: () =>
          import('./customers/customer-detail/customer-detail.component').then((m) => m.CustomerDetailComponent)
      },
      {
        path: 'vendors',
        canActivate: [permissionGuard],
        data: { title: 'Vendors', permission: 'Vendors.View' },
        loadComponent: () => import('./vendors/vendors.component').then((m) => m.VendorsComponent)
      },
      {
        path: 'vendors/:id',
        canActivate: [permissionGuard],
        data: { title: 'Vendor', permission: 'Vendors.View' },
        loadComponent: () =>
          import('./vendors/vendor-detail/vendor-detail.component').then((m) => m.VendorDetailComponent)
      },
      {
        path: 'catalogs',
        canActivate: [permissionGuard],
        data: { title: 'Catalogs', permission: 'Catalogs.View' },
        loadComponent: () => import('./catalogs/catalogs.component').then((m) => m.CatalogsComponent)
      },
      {
        path: 'inventory',
        canActivate: [permissionGuard],
        data: { title: 'Inventory', permission: 'Inventory.View' },
        loadComponent: () => import('./inventory/inventory.component').then((m) => m.InventoryComponent)
      },
      {
        path: 'shipments',
        canActivate: [permissionGuard],
        data: { title: 'Shipments', permission: 'Shipments.View' },
        loadComponent: () => import('./shipments/shipments.component').then((m) => m.ShipmentsComponent)
      },
      {
        path: 'shipments/:id',
        canActivate: [permissionGuard],
        data: { title: 'Shipment', permission: 'Shipments.View' },
        loadComponent: () =>
          import('./shipments/shipment-detail/shipment-detail.component').then((m) => m.ShipmentDetailComponent)
      },
      // Invoicing (E8-09/E8-10) — LIVE against the M6 backend as of 2026-08-03.
      // The design-preview-on-mocked-data phase is over: `mock-invoices.ts` is
      // deleted and both screens read the real `/invoices` API. See ACTION_PLAN
      // §17.3 for the contract and §17.11 for the screen pass.
      //
      // NOTE: an invoice cannot be ISSUED until a Super Admin fills in the
      // company billing block (`legalEntityName` + `registeredAddress`), which
      // is FSD Q9c and still outstanding — the detail screen surfaces that as a
      // configuration gap rather than a failure. See §17.7.
      //
      // 'invoices/new' must stay above 'invoices/:id', or ':id' matches first
      // and the generate screen would try to load an invoice with id "new" —
      // the same ordering trap E4-08 hit with customers/follow-ups.
      {
        path: 'invoices/new',
        canActivate: [permissionGuard],
        data: { title: 'Generate Invoice', permission: 'Invoicing.Edit' },
        loadComponent: () =>
          import('./invoices/invoice-detail/invoice-detail.component').then((m) => m.InvoiceDetailComponent)
      },
      {
        path: 'invoices/:id',
        canActivate: [permissionGuard],
        data: { title: 'Invoice', permission: 'Invoicing.View' },
        loadComponent: () =>
          import('./invoices/invoice-detail/invoice-detail.component').then((m) => m.InvoiceDetailComponent)
      },
      {
        path: 'invoices',
        canActivate: [permissionGuard],
        data: { title: 'Invoices', permission: 'Invoicing.View' },
        loadComponent: () => import('./invoices/invoices.component').then((m) => m.InvoicesComponent)
      },
      // Admin area (E0-04 → E11-07/E11-08), against the existing M2
      // AdminUsersController / MasterDataController.
      {
        path: 'admin/users',
        canActivate: [permissionGuard],
        data: { title: 'Users', permission: 'Admin.ManageUsers' },
        loadComponent: () => import('./admin/users/admin-users.component').then((m) => m.AdminUsersComponent)
      },
      {
        path: 'admin/master-data',
        canActivate: [permissionGuard],
        data: { title: 'Master Data', permission: 'Admin.ManageMasterData' },
        loadComponent: () =>
          import('./admin/master-data/admin-master-data.component').then((m) => m.AdminMasterDataComponent)
      },
      // Self-service password change. Sits under the shell (not with the
      // /force-change-password screen) because it is voluntary — the forced
      // flow is the one that must render outside the shell. Gated on
      // Account.ChangeOwnPassword, which is seeded to SuperAdmin only.
      {
        path: 'account/password',
        canActivate: [permissionGuard],
        data: { title: 'Change password', permission: 'Account.ChangeOwnPassword' },
        loadComponent: () =>
          import('./account/change-password/change-password.component').then((m) => m.ChangePasswordComponent)
      }
    ]
  },
  { path: '**', redirectTo: 'login' }
];
