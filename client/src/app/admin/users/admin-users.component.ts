import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, Subject, debounceTime, distinctUntilChanged, of } from 'rxjs';
import { tap } from 'rxjs/operators';
import { extractErrorMessage } from '../../core/services/problem-details.util';
import { AdminUsersService } from '../services/admin-users.service';
import { AdminUserDto, RoleDto } from '../models/admin-user.models';
import { RefreshService } from '../../core/services/refresh.service';

const PAGE_SIZE = 20;

/**
 * User management (ACTION_PLAN E0-04a), against the existing M2
 * `AdminUsersController` — no backend change. docs/SCREEN_DESIGNS.md's E0-04a
 * section is binding; the load-bearing behaviour is the one-time temporary
 * password modal: `POST /admin/users` and `POST /reset-password` return
 * `temporaryPassword` exactly once and no read endpoint ever echoes it back,
 * so `tempPassword` below can only ever be populated from those two response
 * bodies, never re-fetched.
 *
 * `roles` on `AdminUserDto` is an array of role **names**; assigning roles
 * takes ids (`PUT /roles`). `roles` (this component's `RoleDto[]` cache) is
 * loaded lazily — only once a screen actually needs to resolve name -> id
 * (opening the create or roles modal) — and reused after that.
 */
@Component({
  selector: 'app-admin-users',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './admin-users.component.html',
  styleUrl: './admin-users.component.scss'
})
export class AdminUsersComponent {
  private readonly usersService = inject(AdminUsersService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly actionError = signal<string | null>(null);
  private readonly items = signal<AdminUserDto[]>([]);
  readonly totalCount = signal(0);

  readonly search = signal('');
  readonly includeInactive = signal(false);
  readonly page = signal(1);

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)));
  readonly rows = computed(() => this.items());
  readonly noResults = computed(() => !this.loading() && !this.error() && this.rows().length === 0);

  private readonly search$ = new Subject<string>();

  // ---- Roles cache (lazy — only fetched once a modal needs id<->name resolution) ----
  readonly roles = signal<RoleDto[]>([]);
  private rolesLoaded = false;

  // ---- Create-user modal ----
  readonly createOpen = signal(false);
  readonly createName = signal('');
  readonly createEmail = signal('');
  readonly createSelectedRoleIds = signal<string[]>([]);
  readonly creating = signal(false);
  readonly createError = signal<string | null>(null);

  // ---- One-time temporary-password modal. Deliberately no backdrop (click)
  // handler and no (keydown.escape) binding anywhere on it — the design
  // requires this dialog to be dismissible ONLY via the explicit
  // "I have copied it" button (acknowledgeTempPassword()). ----
  readonly tempPassword = signal<string | null>(null);
  readonly tempPasswordCopied = signal(false);

  // ---- Deactivate confirmation ----
  readonly deactivateTarget = signal<AdminUserDto | null>(null);
  readonly deactivating = signal(false);
  readonly deactivateError = signal<string | null>(null);

  // ---- Roles-assignment modal ----
  readonly roleModalUser = signal<AdminUserDto | null>(null);
  readonly roleModalSelectedIds = signal<string[]>([]);
  readonly savingRoles = signal(false);
  readonly roleModalError = signal<string | null>(null);

  readonly resettingUserId = signal<string | null>(null);
  readonly restoringUserId = signal<string | null>(null);

  private readonly refreshService = inject(RefreshService);

  constructor() {
    // Topbar "Refresh" reloads this screen the same way its Retry control does.
    this.refreshService.onRefresh(() => this.retry());

    this.search$.pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed()).subscribe((value) => {
      this.search.set(value);
      this.page.set(1);
      this.fetch();
    });

    this.fetch();
  }

  onSearchInput(value: string): void {
    this.search$.next(value);
  }

  toggleIncludeInactive(): void {
    this.includeInactive.update((v) => !v);
    this.page.set(1);
    this.fetch();
  }

  prevPage(): void {
    if (this.page() <= 1) return;
    this.page.update((p) => p - 1);
    this.fetch();
  }

  nextPage(): void {
    if (this.page() >= this.totalPages()) return;
    this.page.update((p) => p + 1);
    this.fetch();
  }

  retry(): void {
    this.fetch();
  }

  // ---- Create user ----

  openCreate(): void {
    this.createOpen.set(true);
    this.createName.set('');
    this.createEmail.set('');
    this.createSelectedRoleIds.set([]);
    this.createError.set(null);
    this.loadRolesIfNeeded().subscribe({ error: () => {} });
  }

  cancelCreate(): void {
    this.createOpen.set(false);
  }

  toggleCreateRole(id: string): void {
    this.createSelectedRoleIds.update((ids) => (ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]));
  }

  submitCreate(): void {
    if (this.creating()) return;
    const name = this.createName().trim();
    const email = this.createEmail().trim();
    if (!name || !email) {
      this.createError.set('Name and email are required.');
      return;
    }
    this.creating.set(true);
    this.createError.set(null);
    this.usersService
      .create({ name, email, roleIds: this.createSelectedRoleIds().length ? this.createSelectedRoleIds() : null })
      .subscribe({
        next: (res) => {
          this.creating.set(false);
          this.createOpen.set(false);
          this.openTempPassword(res.temporaryPassword);
          this.fetch();
        },
        error: (err: unknown) => {
          this.creating.set(false);
          this.createError.set(extractErrorMessage(err, 'Could not create this user. Please try again.'));
        }
      });
  }

  // ---- Reset password ----

  resetPassword(user: AdminUserDto): void {
    if (this.resettingUserId()) return;
    this.resettingUserId.set(user.id);
    this.actionError.set(null);
    this.usersService.resetPassword(user.id).subscribe({
      next: (res) => {
        this.resettingUserId.set(null);
        this.openTempPassword(res.temporaryPassword);
        this.fetch();
      },
      error: (err: unknown) => {
        this.resettingUserId.set(null);
        this.actionError.set(extractErrorMessage(err, 'Could not reset the password. Please try again.'));
      }
    });
  }

  private openTempPassword(password: string): void {
    this.tempPasswordCopied.set(false);
    this.tempPassword.set(password);
  }

  copyTempPassword(): void {
    const pw = this.tempPassword();
    if (!pw) return;
    navigator.clipboard?.writeText(pw).then(
      () => this.tempPasswordCopied.set(true),
      () => {
        /* clipboard permission denied — the value is still selectable/visible in .secret-value */
      }
    );
  }

  /** The ONLY way this modal closes — no backdrop click, no Escape. */
  acknowledgeTempPassword(): void {
    this.tempPassword.set(null);
    this.tempPasswordCopied.set(false);
  }

  // ---- Deactivate (confirmed, not immediate) ----

  openDeactivateConfirm(user: AdminUserDto): void {
    this.deactivateTarget.set(user);
    this.deactivateError.set(null);
  }

  cancelDeactivate(): void {
    this.deactivateTarget.set(null);
    this.deactivateError.set(null);
  }

  confirmDeactivate(): void {
    const user = this.deactivateTarget();
    if (!user || this.deactivating()) return;
    this.deactivating.set(true);
    this.deactivateError.set(null);
    this.usersService.deactivate(user.id).subscribe({
      next: () => {
        this.deactivating.set(false);
        this.deactivateTarget.set(null);
        this.fetch();
      },
      error: (err: unknown) => {
        this.deactivating.set(false);
        // The last-active-Super-Admin guard (D-11) returns 400 here — render
        // the ProblemDetails detail verbatim rather than a generic failure.
        this.deactivateError.set(extractErrorMessage(err, 'Could not deactivate this user. Please try again.'));
      }
    });
  }

  // ---- Restore (a visible button, not API-only) ----

  restore(user: AdminUserDto): void {
    if (this.restoringUserId()) return;
    this.restoringUserId.set(user.id);
    this.actionError.set(null);
    this.usersService.restore(user.id).subscribe({
      next: () => {
        this.restoringUserId.set(null);
        this.fetch();
      },
      error: (err: unknown) => {
        this.restoringUserId.set(null);
        this.actionError.set(extractErrorMessage(err, 'Could not restore this user. Please try again.'));
      }
    });
  }

  // ---- Roles modal ----

  openRoles(user: AdminUserDto): void {
    this.roleModalUser.set(user);
    this.roleModalError.set(null);
    this.roleModalSelectedIds.set([]);
    this.loadRolesIfNeeded().subscribe({
      next: (roles) => {
        const ids = user.roles.map((name) => roles.find((r) => r.name === name)?.id).filter((id): id is string => !!id);
        this.roleModalSelectedIds.set(ids);
      },
      error: (err: unknown) => {
        this.roleModalError.set(extractErrorMessage(err, 'Could not load roles. Please try again.'));
      }
    });
  }

  cancelRoles(): void {
    this.roleModalUser.set(null);
  }

  toggleRoleSelection(id: string): void {
    this.roleModalSelectedIds.update((ids) => (ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]));
  }

  saveRoles(): void {
    const user = this.roleModalUser();
    if (!user || this.savingRoles()) return;
    this.savingRoles.set(true);
    this.roleModalError.set(null);
    this.usersService.assignRoles(user.id, this.roleModalSelectedIds()).subscribe({
      next: () => {
        this.savingRoles.set(false);
        this.roleModalUser.set(null);
        this.fetch();
      },
      error: (err: unknown) => {
        this.savingRoles.set(false);
        this.roleModalError.set(extractErrorMessage(err, 'Could not update roles. Please try again.'));
      }
    });
  }

  private loadRolesIfNeeded(): Observable<RoleDto[]> {
    if (this.rolesLoaded) return of(this.roles());
    return this.usersService.listRoles().pipe(
      tap((roles) => {
        this.roles.set(roles);
        this.rolesLoaded = true;
      })
    );
  }

  private fetch(): void {
    this.loading.set(true);
    this.error.set(null);
    this.usersService
      .list({
        search: this.search() || undefined,
        page: this.page(),
        pageSize: PAGE_SIZE,
        includeInactive: this.includeInactive()
      })
      .subscribe({
        next: (res) => {
          this.items.set(res.items);
          this.totalCount.set(res.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.error.set(extractErrorMessage(err, 'Could not load users. Please try again.'));
        }
      });
  }
}
