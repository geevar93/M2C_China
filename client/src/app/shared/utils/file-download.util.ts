/**
 * Turns an already-fetched `Blob` (from the authenticated
 * `GET /catalog-documents/{id}/download` endpoint, TECH_SPEC §8) into a saved
 * file. Deliberately takes a `Blob`, never a URL — the whole point of this
 * helper existing is that the document was never reachable at a plain,
 * unauthenticated `<a href>` in the first place.
 */
export function saveBlobAs(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

/**
 * Opens an already-fetched `Blob` in a new tab so the browser's built-in PDF
 * viewer previews it (E6-04's "can be previewed before sending"), instead of
 * forcing a download. The object URL is intentionally not revoked
 * synchronously — the new tab needs it to load the document; it is released
 * when that tab is closed/navigated away, which is an acceptable trade-off
 * for a short-lived preview.
 */
export function previewBlob(blob: Blob): void {
  const url = URL.createObjectURL(blob);
  window.open(url, '_blank');
}
