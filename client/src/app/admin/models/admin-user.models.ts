/**
 * Wire contract for the existing `AdminUsersController` (M2; ACTION_PLAN
 * E11-01..E11-05). No backend change was needed for E0-04a — every shape
 * here mirrors `AdminUserDtos.cs` verbatim.
 */

/** One row of `GET /admin/users`. `roles` is an array of role **names**, not
 * ids — assigning roles (`PUT /roles`) takes ids, so a consumer must resolve
 * name -> id via `RoleDto[]` from `GET /admin/roles`. This asymmetry is real
 * (confirmed in the controller/DTOs), not an oversight to normalise away. */
export interface AdminUserDto {
  id: string;
  name: string;
  email: string;
  isActive: boolean;
  mustChangePassword: boolean;
  roles: string[];
  createdAt: string;
}

/** `GET /admin/roles` row. */
export interface RoleDto {
  id: string;
  name: string;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface AdminUsersListParams {
  search?: string;
  page: number;
  pageSize: number;
  includeInactive: boolean;
}

export interface CreateUserRequest {
  name: string;
  email: string;
  roleIds: string[] | null;
}

/**
 * `temporaryPassword` is returned exactly once, in this response body only —
 * no read endpoint ever echoes it back (verified live at M2). The one-time
 * modal in `AdminUsersComponent` is built around that fact.
 */
export interface CreateUserResult {
  user: AdminUserDto;
  temporaryPassword: string;
}

/** Same one-time-display rule as `CreateUserResult.temporaryPassword`. */
export interface ResetPasswordResult {
  temporaryPassword: string;
}
