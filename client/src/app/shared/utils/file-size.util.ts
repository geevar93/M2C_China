/**
 * Formats `CatalogDocument.sizeBytes` the way the prototype's mock data shows
 * it (e.g. `8.4 MB`) — the real API returns a raw byte count instead of a
 * pre-formatted string, so the frontend formats it.
 */
export function formatFileSize(bytes: number | null | undefined): string {
  if (bytes == null || Number.isNaN(bytes) || bytes < 0) return '—';
  if (bytes < 1024) return `${bytes} B`;
  const kb = bytes / 1024;
  if (kb < 1024) return `${kb.toFixed(1)} KB`;
  const mb = kb / 1024;
  return `${mb.toFixed(1)} MB`;
}
