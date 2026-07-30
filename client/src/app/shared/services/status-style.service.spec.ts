import { StatusStyleService } from './status-style.service';

/**
 * Regression coverage for the M5 live-API diff: the real
 * `serviceType.code` for the freight-only service type is `FREIGHT_ONLY`,
 * not the display label `Freight-only`. Every spec fixture across the app
 * previously used `code: 'Freight-only'`, which encoded the bug and let the
 * whole suite go green while `serviceType('FREIGHT_ONLY')` silently fell
 * through to the grey default in the real app.
 *
 * The expected colours are hardcoded here as literals, not read back off the
 * `SVC` map under test — asserting `serviceType('FREIGHT_ONLY')` against a
 * value pulled from the same map it's meant to guard would make this test
 * pass even if both were renamed together (ACTION_PLAN N-12: a seed test
 * deriving its expectation from the constant it protects is a known smell in
 * this codebase; this test deliberately avoids repeating it).
 */
describe('StatusStyleService', () => {
  let service: StatusStyleService;

  beforeEach(() => {
    service = new StatusStyleService();
  });

  it('resolves the real API code FREIGHT_ONLY to the orange freight chip, not the grey default', () => {
    const result = service.serviceType('FREIGHT_ONLY');
    expect(result).toEqual({ label: 'FREIGHT-ONLY', bg: '#fff3e0', fg: '#f57f17' });
  });

  it('resolves CIF to the blue chip', () => {
    const result = service.serviceType('CIF');
    expect(result).toEqual({ label: 'CIF', bg: '#e3f2fd', fg: '#1565c0' });
  });

  it('does NOT resolve the display label Freight-only — code is the only valid key', () => {
    const result = service.serviceType('Freight-only');
    expect(result.bg).toBe('#e5e7eb');
    expect(result.fg).toBe('#374151');
  });

  it('falls back to the grey default with the raw code as label for an unknown code', () => {
    const result = service.serviceType('SOMETHING_ELSE');
    expect(result).toEqual({ label: 'SOMETHING_ELSE', bg: '#e5e7eb', fg: '#374151' });
  });

  it('falls back to the em dash label for null/undefined', () => {
    expect(service.serviceType(null).label).toBe('—');
    expect(service.serviceType(undefined).label).toBe('—');
  });

  it('resolves the shipment status IN TRANSIT (with a space) to the orange chip — confirmed live, not to be "fixed"', () => {
    expect(service.status('IN TRANSIT')).toEqual({ bg: '#fff3e0', fg: '#f57f17' });
  });
});
