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
        path: 'vendors',
        canActivate: [permissionGuard],
        data: { title: 'Vendors', permission: 'Vendors.View' },
        loadComponent: () => import('./vendors/vendors.component').then((m) => m.VendorsComponent)
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
      }
    ]
  },
  { path: '**', redirectTo: 'login' }
];
