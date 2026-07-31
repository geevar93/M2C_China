import { TestBed } from '@angular/core/testing';
import { StatusStyleService } from './status-style.service';
import { SERVICE_TYPE_CIF, SERVICE_TYPE_FREIGHT_ONLY } from '../constants/service-type-codes';

/**
 * Guards the app-wide status/service-type colour source (DR-6, TECH_SPEC §5.1).
 *
 * It had no spec until the M5 screen pass, which is how it shipped keyed by the
 * service-type *label* while every caller passed the *code* — freight-only chips
 * silently fell through to the grey default on four screens. The assertions below
 * deliberately spell the expected code out as a literal rather than reading it back
 * from the constant they protect (the N-12 defect class): a test that imports the
 * value it is checking would have passed against the bug too.
 */
describe('StatusStyleService', () => {
  let styles: StatusStyleService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    styles = TestBed.inject(StatusStyleService);
  });

  describe('serviceType()', () => {
    it('resolves the freight-only code the API actually serialises', () => {
      const chip = styles.serviceType('FREIGHT_ONLY');

      expect(chip.label).toBe('FREIGHT-ONLY');
      expect(chip.fg).toBe('#f57f17');
      expect(chip.bg).toBe('#fff3e0');
    });

    it('does NOT resolve the seeded label — that string is not a code', () => {
      // 'Freight-only' is what `serviceTypes[1].label` holds and what the prototype
      // used as its raw value. Reaching this map means a caller passed a label.
      const chip = styles.serviceType('Freight-only');

      expect(chip.label).not.toBe('FREIGHT-ONLY');
    });

    it('resolves CIF', () => {
      const chip = styles.serviceType('CIF');

      expect(chip.label).toBe('CIF');
      expect(chip.fg).toBe('#1565c0');
    });

    it('falls back to a neutral chip for an unknown code rather than throwing', () => {
      const chip = styles.serviceType('SOMETHING_NEW');

      expect(chip.label).toBe('SOMETHING_NEW');
      expect(chip.bg).toBe('#e5e7eb');
    });

    it('keeps the exported constants in step with the seeder', () => {
      expect(SERVICE_TYPE_CIF).toBe('CIF');
      expect(SERVICE_TYPE_FREIGHT_ONLY).toBe('FREIGHT_ONLY');
    });
  });

  describe('status()', () => {
    it('resolves every seeded shipment status code, including the one with a space', () => {
      expect(styles.status('PACKED').fg).toBe('#374151');
      expect(styles.status('DISPATCHED').fg).toBe('#1565c0');
      expect(styles.status('IN TRANSIT').fg).toBe('#f57f17');
      expect(styles.status('DELIVERED').fg).toBe('#2e7d32');
    });
  });
});
