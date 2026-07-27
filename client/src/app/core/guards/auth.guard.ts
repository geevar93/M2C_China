import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

/** Blocks any route unless the user holds a valid (unexpired) session. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.isAuthenticated()) return true;
  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/**
 * Blocks the shell unless the password-change flag is clear (TECH_SPEC §4.2).
 * A user who must change their password can authenticate but is routed to
 * the force-change screen instead of anywhere in the app shell.
 */
export const mustChangePasswordGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.mustChangePassword()) {
    return router.createUrlTree(['/force-change-password']);
  }
  return true;
};

/** Keeps an already-authenticated user off the login screen. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isAuthenticated()) return true;
  if (auth.mustChangePassword()) return router.createUrlTree(['/force-change-password']);
  return router.createUrlTree(['/dashboard']);
};
