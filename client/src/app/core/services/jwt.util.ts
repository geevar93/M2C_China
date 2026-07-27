import { JwtClaims } from '../models/auth.models';

/**
 * Decodes a JWT's payload segment without any external dependency (keeps the
 * bundle lightweight per C6 — a full jwt-decode/jose package is unnecessary
 * for reading a payload we never verify client-side; the server remains the
 * signature authority).
 */
function decodeBase64Url(segment: string): string {
  const base64 = segment.replace(/-/g, '+').replace(/_/g, '/');
  const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=');
  // atob is available in every browser target this app ships to.
  const binary = atob(padded);
  // Decode UTF-8 bytes correctly (claim values may contain non-ASCII names).
  const bytes = Uint8Array.from(binary, (c) => c.charCodeAt(0));
  return new TextDecoder('utf-8').decode(bytes);
}

/**
 * Normalizes a claim that the spec guarantees as a JSON array but that .NET's
 * token serialization is known to collapse into a bare string when there is
 * exactly one value (single-element repeated claim -> scalar). Tolerating
 * both shapes here means a one-permission or one-role user never silently
 * ends up with an empty permission/role set. See the auth contract note in
 * the coordinator's task brief — this is a required defensive-parsing rule,
 * not a hypothetical.
 */
function toStringArray(value: unknown): string[] {
  if (Array.isArray(value)) {
    return value.filter((v): v is string => typeof v === 'string');
  }
  if (typeof value === 'string' && value.length > 0) {
    return [value];
  }
  return [];
}

function toBoolean(value: unknown): boolean {
  if (typeof value === 'boolean') return value;
  if (typeof value === 'string') return value.toLowerCase() === 'true';
  return false;
}

/**
 * Decodes and defensively normalizes the claims of an access token. Returns
 * null if the token is malformed (caller should treat that as "not
 * authenticated" rather than throw).
 */
export function decodeJwt(token: string): JwtClaims | null {
  try {
    const parts = token.split('.');
    if (parts.length !== 3) return null;
    const payload = JSON.parse(decodeBase64Url(parts[1])) as Record<string, unknown>;

    return {
      sub: String(payload['sub'] ?? ''),
      email: String(payload['email'] ?? ''),
      name: String(payload['name'] ?? ''),
      role: toStringArray(payload['role']),
      permissions: toStringArray(payload['permissions']),
      mustChangePassword: toBoolean(payload['must_change_password']),
      exp: typeof payload['exp'] === 'number' ? (payload['exp'] as number) : 0,
      iss: typeof payload['iss'] === 'string' ? (payload['iss'] as string) : undefined,
      aud: typeof payload['aud'] === 'string' ? (payload['aud'] as string) : undefined
    };
  } catch {
    return null;
  }
}

/** True if the token's `exp` claim is in the past (or unreadable). */
export function isExpired(claims: JwtClaims | null): boolean {
  if (!claims || !claims.exp) return true;
  return Date.now() >= claims.exp * 1000;
}
