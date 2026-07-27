import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

const NO_TOKEN_ENDPOINTS = ['/auth/login', '/auth/refresh'];

/**
 * Attaches the bearer token to every API request and, on a 401, attempts one
 * refresh-and-retry before redirecting to /login (TECH_SPEC §5.3, ACTION_PLAN
 * E1-11). Token + refresh handling lives only here and in AuthService.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const isNoTokenEndpoint = NO_TOKEN_ENDPOINTS.some((p) => req.url.includes(p));

  const withAuth = (token: string | null) =>
    token && !isNoTokenEndpoint ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(withAuth(auth.accessToken)).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse && err.status === 401 && !isNoTokenEndpoint) {
        return auth.refreshShared().pipe(
          switchMap((refreshed) => next(withAuth(refreshed.accessToken))),
          catchError((refreshErr) => {
            auth.forceLogout();
            return throwError(() => refreshErr);
          })
        );
      }
      return throwError(() => err);
    })
  );
};
