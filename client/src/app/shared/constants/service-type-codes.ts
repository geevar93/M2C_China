/**
 * The seeded service-type **codes**, mirroring `SeedDefaults.ServiceTypeCif` /
 * `SeedDefaults.ServiceTypeFreightOnly` in `SourcingOps.Domain`.
 *
 * These are codes, not labels. The distinction cost a real defect: the whole
 * client previously compared against `'Freight-only'`, which is the seeded
 * **label** (and the approved prototype's raw `svc` value), while the API
 * serialises `code: 'FREIGHT_ONLY'`. Nothing caught it because every spec
 * fixture hard-coded the same wrong string it was meant to protect — the N-12
 * defect class, on a value that reaches four screens.
 *
 * Branching on `code` rather than `label` is deliberate and required: codes are
 * immutable after creation (D-12) precisely so behaviour can key off them,
 * whereas a Super Admin may relabel `Freight-only` to anything at any time.
 * Import from here rather than re-typing the literal, so a future code change
 * is one edit and a compile error everywhere else.
 *
 * **Do not import these into a test that is asserting what they equal** (ACTION_PLAN N-12).
 * The rule across this codebase: a test may reference a constant to prove *behaviour*, but
 * never to prove *identity* — an identity assertion terminates in a literal or it proves
 * nothing. `status-style.service.spec.ts` deliberately spells `'FREIGHT_ONLY'` out for exactly
 * this reason, and would have failed against the original bug; a version importing the
 * constant would have passed. The server-side counterpart is
 * `DbSeederTests.ExpectedPermissionCodes`.
 */
export const SERVICE_TYPE_CIF = 'CIF';
export const SERVICE_TYPE_FREIGHT_ONLY = 'FREIGHT_ONLY';
