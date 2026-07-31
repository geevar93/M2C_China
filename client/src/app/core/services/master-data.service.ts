import { Injectable, inject } from '@angular/core';
import {
  BehaviorSubject,
  Observable,
  catchError,
  defer,
  distinctUntilChanged,
  filter,
  map,
  of,
  shareReplay,
  switchMap,
  tap,
  throwError
} from 'rxjs';
import { ApiService } from './api.service';
import { extractErrorMessage } from './problem-details.util';
import {
  CategoryRow,
  DOCUMENT_SCOPE_SHIPMENT,
  DOCUMENT_SCOPE_VENDOR,
  LookupRow,
  MasterDataCollectionKey,
  MasterDataResponse
} from '../models/master-data.models';

export type MasterDataLoadStatus = 'idle' | 'loading' | 'loaded' | 'error';

export interface MasterDataState {
  status: MasterDataLoadStatus;
  /** Last-known-good payload. Kept across a failed reload so existing consumers keep serving stale-but-valid data rather than going blank (ACTION_PLAN E3-10 DoD: no blank screen on API failure). */
  data: MasterDataResponse | null;
  /** Human-facing message extracted from the RFC 7807 ProblemDetails body, or null when there is no active error. */
  error: string | null;
}

const EMPTY_STATE: MasterDataState = { status: 'idle', data: null, error: null };

/** Sorts a collection's active rows by `sortOrder` — the only ordering the API contract defines. Never mutates the input array (the cached copy in `state$` must keep its retired rows intact for `*ById` lookups). */
function activeSorted<T extends { isActive: boolean; sortOrder: number }>(rows: readonly T[]): T[] {
  return rows.filter((r) => r.isActive).sort((a, b) => a.sortOrder - b.sortOrder);
}

/**
 * Single source of every category/status/service-type lookup in the app
 * (ACTION_PLAN E3-10, closing DR-6: "no feature component hard-codes a
 * category, status or service-type list"). Loads the whole aggregate in one
 * `GET /api/v1/master-data?includeRetired=true` call, cached in-memory for
 * the session — every consumer shares the one copy (see the "single HTTP
 * call" unit test) rather than each triggering its own request.
 *
 * Retired-rows contract (E3-08 — rows are retired, not deleted):
 * - `*Options()` methods return **active rows only**, sorted by `sortOrder`.
 *   Use these to populate a `<select>` on a new/editable record — a retired
 *   row must never be selectable going forward.
 * - `*ById()` methods resolve **any** row, retired included, because a
 *   historical record may legitimately reference a value that has since been
 *   retired; excluding it would render a blank label or a bare id.
 * There is deliberately no single "give me a row" method that could silently
 * be used for either purpose — the two names make the choice explicit at the
 * call site.
 *
 * State handling: `masterData$`/`loading$`/`error$` expose the load lifecycle
 * so a future screen can show a spinner while loading and an error banner on
 * failure instead of leaving `*Options()`/`*ById()` pending forever — those
 * accessor observables only emit once data has successfully loaded at least
 * once; combine them with `loading$`/`error$` for full state handling.
 *
 * Distinct from `StatusStyleService` (shared/services): that service maps a
 * status **code** to a colour (presentation only, ported from the prototype).
 * This service maps an **id** to a database row (label + sortOrder). A screen
 * typically needs both — resolve id -> row here, then row.code -> colour there.
 */
@Injectable({ providedIn: 'root' })
export class MasterDataService {
  private readonly api = inject(ApiService);

  private readonly state$ = new BehaviorSubject<MasterDataState>(EMPTY_STATE);
  /** The in-flight/cached shared HTTP observable. Persists after completion (unlike AuthService's single-flight refresh) so it doubles as the session-long cache; cleared only on error (to allow retry) or by `reload()`. */
  private loadInFlight$: Observable<MasterDataResponse> | null = null;

  /** Full load-lifecycle state, for a screen that wants to render its own loading/error UI around lookup-dependent content. */
  readonly masterData$: Observable<MasterDataState> = this.state$.asObservable();
  readonly loading$: Observable<boolean> = this.state$.pipe(
    map((s) => s.status === 'loading'),
    distinctUntilChanged()
  );
  readonly error$: Observable<string | null> = this.state$.pipe(
    map((s) => s.error),
    distinctUntilChanged()
  );

  /**
   * Triggers the load if it hasn't happened yet this session and is safe to
   * call from as many places as needed — subsequent calls (concurrent or
   * later) reuse the same cached/in-flight request rather than issuing a new
   * one. Every `*Options()`/`*ById()` accessor calls this internally, so a
   * consumer never has to remember to "prime" the cache first.
   */
  ensureLoaded(): Observable<MasterDataResponse> {
    const current = this.state$.value;
    if (current.status === 'loaded' && current.data) {
      return of(current.data);
    }
    return this.load();
  }

  /**
   * Forces a fresh load from the server and pushes the new data to every
   * existing subscriber of `*Options()`/`*ById()` — the explicit refresh path
   * for "an admin edit is reflected without a full page reload."
   */
  reload(): Observable<MasterDataResponse> {
    this.loadInFlight$ = null;
    return this.load();
  }

  // ---- Categories ----------------------------------------------------

  /** Active categories only, sorted by `sortOrder`. Safe for a new/editable record's category picker. */
  categoryOptions(): Observable<CategoryRow[]> {
    return this.optionsOf('categories');
  }

  /** Resolves a category id to its row, retired included. Use this to render an existing record's category label. */
  categoryById(id: string | null | undefined): Observable<CategoryRow | undefined> {
    return this.byIdOf('categories', id);
  }

  // ---- Service types ---------------------------------------------------

  serviceTypeOptions(): Observable<LookupRow[]> {
    return this.optionsOf('serviceTypes');
  }

  serviceTypeById(id: string | null | undefined): Observable<LookupRow | undefined> {
    return this.byIdOf('serviceTypes', id);
  }

  // ---- Lead statuses ----------------------------------------------------

  leadStatusOptions(): Observable<LookupRow[]> {
    return this.optionsOf('leadStatuses');
  }

  leadStatusById(id: string | null | undefined): Observable<LookupRow | undefined> {
    return this.byIdOf('leadStatuses', id);
  }

  // ---- Shipment statuses --------------------------------------------------

  shipmentStatusOptions(): Observable<LookupRow[]> {
    return this.optionsOf('shipmentStatuses');
  }

  shipmentStatusById(id: string | null | undefined): Observable<LookupRow | undefined> {
    return this.byIdOf('shipmentStatuses', id);
  }

  // ---- Invoice statuses --------------------------------------------------

  invoiceStatusOptions(): Observable<LookupRow[]> {
    return this.optionsOf('invoiceStatuses');
  }

  invoiceStatusById(id: string | null | undefined): Observable<LookupRow | undefined> {
    return this.byIdOf('invoiceStatuses', id);
  }

  // ---- Vendor statuses --------------------------------------------------

  vendorStatusOptions(): Observable<LookupRow[]> {
    return this.optionsOf('vendorStatuses');
  }

  vendorStatusById(id: string | null | undefined): Observable<LookupRow | undefined> {
    return this.byIdOf('vendorStatuses', id);
  }

  // ---- Document types (N-20(a)/(c); D-34/D-44/D-45) ----------------------

  /**
   * Active document-type rows only, sorted by `sortOrder`, filtered to the
   * given `scope` — the API serialises `documentTypes` rows for BOTH
   * `Vendor` and `Shipment` scopes in one collection, so an unscoped caller
   * would otherwise get a vendor document type back for a shipment upload
   * dropdown (or vice versa) and offer a type that can never actually attach.
   *
   * `scope` is deliberately a **required** parameter, not optional/defaulted,
   * unlike every other `*Options()` accessor above. ACTION_PLAN N-20(b)
   * exists precisely because a document type silently defaults to `Vendor`
   * server-side when a caller omits scope on create — an unscoped accessor
   * here would let the frontend repeat that same silent-default mistake one
   * layer up, for a caller who simply forgot to ask. Making the parameter
   * required and narrowly typed (`'Vendor' | 'Shipment'`) means a screen
   * cannot compile a call that leaves it out; a caller is forced to state
   * which module it is populating a dropdown for.
   */
  documentTypeOptions(scope: 'Vendor' | 'Shipment'): Observable<LookupRow[]> {
    return this.optionsOf('documentTypes').pipe(map((rows) => rows.filter((r) => r.scope === scope)));
  }

  /** Resolves a document-type id to its row, retired included, scope included as-is on the row. */
  documentTypeById(id: string | null | undefined): Observable<LookupRow | undefined> {
    return this.byIdOf('documentTypes', id);
  }

  // ---- Internals ----------------------------------------------------------

  private load(): Observable<MasterDataResponse> {
    if (this.loadInFlight$) {
      return this.loadInFlight$;
    }
    this.state$.next({ status: 'loading', data: this.state$.value.data, error: null });
    this.loadInFlight$ = this.api.get<MasterDataResponse>('/master-data', { includeRetired: true }).pipe(
      tap((data) => this.state$.next({ status: 'loaded', data, error: null })),
      catchError((err: unknown) => {
        // Clear the cached failure so the *next* ensureLoaded() call (a retry,
        // or simply the next screen that needs a lookup) attempts the request
        // again instead of silently replaying the same error forever.
        this.loadInFlight$ = null;
        const message = extractErrorMessage(err, 'Could not load master data. Please try again.');
        this.state$.next({ status: 'error', data: this.state$.value.data, error: message });
        return throwError(() => err);
      }),
      shareReplay(1)
    );
    return this.loadInFlight$;
  }

  /** All rows of one collection, retired included — only emits once a load has succeeded at least once. */
  private rowsOf<K extends MasterDataCollectionKey>(key: K): Observable<MasterDataResponse[K]> {
    return defer(() => this.ensureLoaded()).pipe(
      // The failure is already recorded in state$/error$ above; don't let it
      // tear down this stream too — a later reload()/retry can still succeed.
      catchError(() => of(null)),
      switchMap(() =>
        this.state$.pipe(
          map((s) => s.data),
          filter((data): data is MasterDataResponse => data !== null),
          map((data) => data[key])
        )
      )
    );
  }

  private optionsOf<K extends MasterDataCollectionKey>(key: K): Observable<MasterDataResponse[K]> {
    return this.rowsOf(key).pipe(
      map(
        (rows) =>
          activeSorted(rows as unknown as Array<{ isActive: boolean; sortOrder: number }>) as unknown as MasterDataResponse[K]
      )
    );
  }

  private byIdOf<K extends MasterDataCollectionKey>(
    key: K,
    id: string | null | undefined
  ): Observable<MasterDataResponse[K][number] | undefined> {
    if (!id) {
      return of(undefined);
    }
    return this.rowsOf(key).pipe(
      map((rows) => (rows as ReadonlyArray<{ id: string }>).find((r) => r.id === id) as MasterDataResponse[K][number] | undefined)
    );
  }
}
