/**
 * GST state codes (the first two digits of a GSTIN) for every Indian state and
 * union territory — the options behind every "State" selector in the app.
 *
 * MIRRORS `SourcingOps.Domain.Constants.IndianStateCodes` on the server, which
 * is the authority: it validates every code on write and resolves the display
 * name on read, so a drift here can only ever produce a rejected save, never a
 * wrong tax treatment. Kept as a client constant rather than fetched because
 * this list is set by statute, not by the business — unlike the FSD §3.3
 * master-data collections, there is nothing here for a Super Admin to edit.
 *
 * Ordered by code, matching the server's dictionary, so the two read as one
 * list when compared side by side.
 */
export interface IndianState {
  code: string;
  name: string;
}

export const INDIAN_STATES: readonly IndianState[] = [
  { code: '01', name: 'Jammu and Kashmir' },
  { code: '02', name: 'Himachal Pradesh' },
  { code: '03', name: 'Punjab' },
  { code: '04', name: 'Chandigarh' },
  { code: '05', name: 'Uttarakhand' },
  { code: '06', name: 'Haryana' },
  { code: '07', name: 'Delhi' },
  { code: '08', name: 'Rajasthan' },
  { code: '09', name: 'Uttar Pradesh' },
  { code: '10', name: 'Bihar' },
  { code: '11', name: 'Sikkim' },
  { code: '12', name: 'Arunachal Pradesh' },
  { code: '13', name: 'Nagaland' },
  { code: '14', name: 'Manipur' },
  { code: '15', name: 'Mizoram' },
  { code: '16', name: 'Tripura' },
  { code: '17', name: 'Meghalaya' },
  { code: '18', name: 'Assam' },
  { code: '19', name: 'West Bengal' },
  { code: '20', name: 'Jharkhand' },
  { code: '21', name: 'Odisha' },
  { code: '22', name: 'Chhattisgarh' },
  { code: '23', name: 'Madhya Pradesh' },
  { code: '24', name: 'Gujarat' },
  { code: '26', name: 'Dadra and Nagar Haveli and Daman and Diu' },
  { code: '27', name: 'Maharashtra' },
  { code: '29', name: 'Karnataka' },
  { code: '30', name: 'Goa' },
  { code: '31', name: 'Lakshadweep' },
  { code: '32', name: 'Kerala' },
  { code: '33', name: 'Tamil Nadu' },
  { code: '34', name: 'Puducherry' },
  { code: '35', name: 'Andaman and Nicobar Islands' },
  { code: '36', name: 'Telangana' },
  { code: '37', name: 'Andhra Pradesh' },
  { code: '38', name: 'Ladakh' },
  { code: '97', name: 'Other Territory' }
];

/**
 * The statutory GST slabs, for the rate selector on an inventory item.
 * Expressed as percents (`18` = 18%), matching the wire format.
 */
export const GST_RATES: readonly number[] = [0, 0.25, 3, 5, 12, 18, 28];

/** The state code embedded in a GSTIN's first two digits, or null when it is not a real one. */
export function stateCodeFromGstin(gstin: string | null | undefined): string | null {
  const trimmed = gstin?.trim();
  if (!trimmed || trimmed.length < 2) {
    return null;
  }
  const prefix = trimmed.slice(0, 2);
  return INDIAN_STATES.some((s) => s.code === prefix) ? prefix : null;
}
