export interface LoginRequest {
  email: string;
  password: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface AuthUser {
  id: string;
  name: string;
  email: string;
  roles: string[];
  permissions: string[];
}

/** Success payload (200) shared by /auth/login, /auth/refresh and /auth/change-password. */
export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAtUtc: string;
  mustChangePassword: boolean;
  user: AuthUser;
}

/** Decoded JWT claim set. Defensively parsed — see jwt.util.ts. */
export interface JwtClaims {
  sub: string;
  email: string;
  name: string;
  role: string[];
  permissions: string[];
  mustChangePassword: boolean;
  exp: number;
  iss?: string;
  aud?: string;
}

/** RFC 7807 problem details, as returned by every API error response. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  [key: string]: unknown;
}
