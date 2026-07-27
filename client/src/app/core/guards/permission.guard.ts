import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

/**
 * Route guard reading the permission set decoded from the JWT (TECH_SPEC
 * §5.3/§4.3, ACTION_PLAN E1-12). Attach via route `data: { permission: 'Customers.View' }`
 * (a single code) or `data: { permission: ['Customers.View', 'Customers.Edit'] }`
 * (any-of). This is defence in depth only — the server remains the authority.
 *
 * Known limitation: denial redirects to /dashboard, which itself requires
 * Analytics.View. Both seeded roles (SuperAdmin, Associate — TECH_SPEC §4.3)
 * hold that permission, so this is not reachable today; a future role with
 * zero permissions would need a permission-free landing page instead.
 */
export const permissionGuard: CanActivateFn = (route) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const required = route.data['permission'] as string | string[] | undefined;
  if (!required) return true;

  const requiredList = Array.isArray(required) ? required : [required];
  const allowed = requiredList.some((p) => auth.hasPermission(p));
  return allowed ? true : router.createUrlTree(['/dashboard']);
};
