import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { Observable, Subject } from 'rxjs';

/**
 * Broadcasts the topbar "Refresh" action to whichever screen is on display.
 *
 * Screens register the reload they already expose for their Retry buttons via
 * `onRefresh(() => this.retry())`; the shell calls `requestRefresh()`. The
 * shell also reloads shared master data so an admin edit to a lookup shows up
 * in the current screen's pickers without a full page reload.
 *
 * `busy` is a short-lived flag the shell uses to spin the icon and disable the
 * control while a refresh is in flight — screens do not report completion, so
 * the flag clears on a fixed delay rather than tracking every request.
 */
@Injectable({ providedIn: 'root' })
export class RefreshService {
  private readonly refreshSubject = new Subject<void>();

  /** Emits once per refresh request. */
  readonly refresh$: Observable<void> = this.refreshSubject.asObservable();

  readonly busy = signal(false);

  /**
   * Subscribe a screen's reload to the topbar action for the life of the
   * calling component. Must be called from an injection context (constructor
   * or field initialiser).
   */
  onRefresh(reload: () => void): void {
    const destroyRef = inject(DestroyRef);
    const sub = this.refresh$.subscribe(() => reload());
    destroyRef.onDestroy(() => sub.unsubscribe());
  }

  requestRefresh(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.refreshSubject.next();
    setTimeout(() => this.busy.set(false), 700);
  }
}
