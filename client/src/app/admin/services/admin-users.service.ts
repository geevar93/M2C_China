import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import {
  AdminUserDto,
  AdminUsersListParams,
  CreateUserRequest,
  CreateUserResult,
  PagedResult,
  ResetPasswordResult,
  RoleDto
} from '../models/admin-user.models';

/**
 * Thin wrapper over the existing `/admin/users` and `/admin/roles` endpoints
 * (E0-04a, backed by the M2 `AdminUsersController` — no backend change).
 * All HTTP goes through the shared `ApiService`; the component never talks
 * to `HttpClient` directly (follows `CustomersService`'s convention).
 */
@Injectable({ providedIn: 'root' })
export class AdminUsersService {
  private readonly api = inject(ApiService);

  list(params: AdminUsersListParams): Observable<PagedResult<AdminUserDto>> {
    return this.api.get<PagedResult<AdminUserDto>>('/admin/users', {
      search: params.search,
      page: params.page,
      pageSize: params.pageSize,
      includeInactive: params.includeInactive
    });
  }

  /**
   * POST /admin/users. `temporaryPassword` on the response is shown exactly
   * once — never re-fetchable — so the caller must hand it to the one-time
   * modal immediately rather than discard it.
   */
  create(request: CreateUserRequest): Observable<CreateUserResult> {
    return this.api.post<CreateUserResult>('/admin/users', request);
  }

  /** Same one-time-display contract as `create()`. */
  resetPassword(id: string): Observable<ResetPasswordResult> {
    return this.api.post<ResetPasswordResult>(`/admin/users/${id}/reset-password`, {});
  }

  /** Deactivates (soft-deletes) the account — revokes sessions and the
   * already-issued access token (dd0f8cf). The API returns 400 with a
   * ProblemDetails `detail` if this would deactivate the last active
   * Super Admin; the caller surfaces that verbatim rather than treating it
   * as an unexpected failure. */
  deactivate(id: string): Observable<void> {
    return this.api.delete<void>(`/admin/users/${id}`);
  }

  restore(id: string): Observable<AdminUserDto> {
    return this.api.post<AdminUserDto>(`/admin/users/${id}/restore`, {});
  }

  assignRoles(id: string, roleIds: string[]): Observable<AdminUserDto> {
    return this.api.put<AdminUserDto>(`/admin/users/${id}/roles`, { roleIds });
  }

  listRoles(): Observable<RoleDto[]> {
    return this.api.get<RoleDto[]>('/admin/roles');
  }
}
