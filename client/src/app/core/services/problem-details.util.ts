import { HttpErrorResponse } from '@angular/common/http';
import { ProblemDetails } from '../models/auth.models';

/**
 * Extracts a human-facing message from an RFC 7807 ProblemDetails error body
 * (TECH_SPEC §4.8/§8) instead of surfacing a raw HTTP/network dump to the
 * user. Falls back to a generic message when the body isn't ProblemDetails
 * shaped (e.g. the API is unreachable, or a proxy/500 returned plain text).
 */
export function extractErrorMessage(err: unknown, fallback = 'Something went wrong. Please try again.'): string {
  if (err instanceof HttpErrorResponse) {
    const body = err.error as ProblemDetails | undefined;
    if (body && typeof body === 'object') {
      if (body.detail) return body.detail;
      if (body.title) return body.title;
    }
    if (err.status === 0) {
      return 'Could not reach the server. Check your connection and try again.';
    }
  }
  return fallback;
}
