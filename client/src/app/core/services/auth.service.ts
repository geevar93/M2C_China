import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { BehaviorSubject, Observable, catchError, finalize, map, of, shareReplay, tap, throwError } from 'rxjs';
import { ApiService } from './api.service';
import { decodeJwt, isExpired } from './jwt.util';
import { AuthResponse, AuthUser, ChangePasswordRequest, JwtClaims, LoginRequest } from '../models/auth.models';

const STORAGE_KEY = 'sop.auth.v1';

interface StoredSession {
  accessToken: string;
  refreshToken: string;
  expiresAtUtc: string;
  user: AuthUser;
}

interface AuthState {
  claims: JwtClaims | null;
  accessToken: string | null;
  refreshToken: string | null;
  /** Convenience display info from the login/refresh response body — permission
   *  checks always go through `claims` (decoded from the JWT itself), never this. */
  user: AuthUser | null;
}

const EMPTY_STATE: AuthState = { claims: null, accessToken: null, refreshToken: null, user: null };

/**
 * Owns the access/refresh token pair and the decoded permission set — the
 * single place token + refresh handling lives (TECH_SPEC §5.3, ACTION_PLAN
 * E1-11). Guards and the auth interceptor both read from here.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  private readonly state$ = new BehaviorSubject<AuthState>(this.restore());
  /** Single-flight refresh so N concurrent 401s trigger one refresh call, not N. */
  private refreshInFlight$: Observable<AuthResponse> | null = null;

  /** Convenience display info (name/email) — not the permission source of truth. */
  readonly currentUser$: Observable<AuthUser | null> = this.state$.pipe(map((s) => s.user));

  get accessToken(): string | null {
    return this.state$.value.accessToken;
  }

  get permissions(): string[] {
    return this.state$.value.claims?.permissions ?? [];
  }

  hasPermission(permission: string): boolean {
    return this.permissions.includes(permission);
  }

  isAuthenticated(): boolean {
    const { claims } = this.state$.value;
    return !!claims && !isExpired(claims);
  }

  mustChangePassword(): boolean {
    return this.state$.value.claims?.mustChangePassword ?? false;
  }

  /** Attempts to rehydrate a session on app boot; refreshes if the access token has expired. */
  init(): Observable<unknown> {
    const stored = this.state$.value;
    if (stored.accessToken && !isExpired(stored.claims)) {
      return of(true);
    }
    if (stored.refreshToken) {
      return this.refresh().pipe(catchError(() => { this.clear(); return of(false); }));
    }
    this.clear();
    return of(false);
  }

  login(request: LoginRequest): Observable<AuthResponse> {
    return this.api.post<AuthResponse>('/auth/login', request).pipe(tap((res) => this.persist(res)));
  }

  refresh(): Observable<AuthResponse> {
    const refreshToken = this.state$.value.refreshToken;
    if (!refreshToken) {
      return throwError(() => new Error('No refresh token available'));
    }
    return this.api.post<AuthResponse>('/auth/refresh', { refreshToken }).pipe(tap((res) => this.persist(res)));
  }

  changePassword(request: ChangePasswordRequest): Observable<AuthResponse> {
    return this.api.post<AuthResponse>('/auth/change-password', request).pipe(tap((res) => this.persist(res)));
  }

  logout(): void {
    const refreshToken = this.state$.value.refreshToken;
    const finish = () => {
      this.clear();
      this.router.navigate(['/login']);
    };
    if (refreshToken) {
      this.api.post('/auth/logout', { refreshToken }).subscribe({ next: finish, error: finish });
    } else {
      finish();
    }
  }

  /** Called by the auth interceptor when a refresh attempt fails — clears state and redirects. */
  forceLogout(): void {
    this.clear();
    this.router.navigate(['/login']);
  }

  /** Used by the auth interceptor so concurrent 401s share one refresh call. */
  refreshShared(): Observable<AuthResponse> {
    if (!this.refreshInFlight$) {
      this.refreshInFlight$ = this.refresh().pipe(
        finalize(() => { this.refreshInFlight$ = null; }),
        shareReplay(1)
      );
    }
    return this.refreshInFlight$;
  }

  private persist(res: AuthResponse): void {
    const claims = decodeJwt(res.accessToken);
    this.state$.next({ claims, accessToken: res.accessToken, refreshToken: res.refreshToken, user: res.user });
    const stored: StoredSession = {
      accessToken: res.accessToken,
      refreshToken: res.refreshToken,
      expiresAtUtc: res.expiresAtUtc,
      user: res.user
    };
    localStorage.setItem(STORAGE_KEY, JSON.stringify(stored));
  }

  private clear(): void {
    localStorage.removeItem(STORAGE_KEY);
    this.state$.next(EMPTY_STATE);
  }

  private restore(): AuthState {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return EMPTY_STATE;
      const stored = JSON.parse(raw) as StoredSession;
      const claims = decodeJwt(stored.accessToken);
      return { claims, accessToken: stored.accessToken, refreshToken: stored.refreshToken, user: stored.user };
    } catch {
      return EMPTY_STATE;
    }
  }
}
