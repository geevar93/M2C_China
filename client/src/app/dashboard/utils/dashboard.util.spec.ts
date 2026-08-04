import {
  axisLabelIndices,
  barWidthPct,
  donutDashArray,
  formatAxisDate,
  formatConversionRate,
  linePoints,
  maxOf,
  percentChange,
  pluralize,
  rangeSuffix,
  resolveDateRange,
  toPolylinePoints
} from './dashboard.util';

describe('dashboard.util', () => {
  describe('resolveDateRange', () => {
    const now = new Date(2026, 7, 15); // 15 Aug 2026 (month is 0-indexed)

    it('resolves "Last 7 days" to a 7-day inclusive window', () => {
      expect(resolveDateRange('Last 7 days', now)).toEqual({ fromDate: '2026-08-09', toDate: '2026-08-15' });
    });

    it('resolves "Last 30 days" to a 30-day inclusive window', () => {
      expect(resolveDateRange('Last 30 days', now)).toEqual({ fromDate: '2026-07-17', toDate: '2026-08-15' });
    });

    it('resolves "This quarter" to the 1st of the current calendar quarter', () => {
      // August is in Q3 (Jul-Sep) -> quarter starts 1 Jul.
      expect(resolveDateRange('This quarter', now)).toEqual({ fromDate: '2026-07-01', toDate: '2026-08-15' });
    });
  });

  describe('rangeSuffix', () => {
    it('maps each option to its KPI-label suffix', () => {
      expect(rangeSuffix('Last 7 days')).toBe('7d');
      expect(rangeSuffix('Last 30 days')).toBe('30d');
      expect(rangeSuffix('This quarter')).toBe('qtr');
    });
  });

  describe('percentChange', () => {
    it('computes a signed whole-percent change', () => {
      expect(percentChange(22, 18)).toBe(22);
      expect(percentChange(10, 20)).toBe(-50);
    });

    it('returns null when prior is 0, instead of Infinity or NaN', () => {
      expect(percentChange(5, 0)).toBeNull();
      expect(percentChange(0, 0)).toBeNull();
    });
  });

  describe('formatConversionRate', () => {
    it('renders a 0..1 rate as a one-decimal percentage, without double-converting', () => {
      expect(formatConversionRate(0.18)).toBe('18.0%');
      expect(formatConversionRate(0)).toBe('0.0%');
      expect(formatConversionRate(1)).toBe('100.0%');
    });
  });

  describe('barWidthPct', () => {
    it('computes a percentage of the max', () => {
      expect(barWidthPct(74, 128)).toBe(58);
      expect(barWidthPct(128, 128)).toBe(100);
    });

    it('guards an all-zero series (max === 0) to 0, not NaN/Infinity', () => {
      expect(barWidthPct(0, 0)).toBe(0);
    });

    it('guards a negative/zero max defensively to 0', () => {
      expect(barWidthPct(5, -1)).toBe(0);
    });
  });

  describe('maxOf', () => {
    it('finds the largest value via the selector', () => {
      expect(maxOf([{ n: 3 }, { n: 9 }, { n: 1 }], (x) => x.n)).toBe(9);
    });

    it('returns 0 for an empty list', () => {
      expect(maxOf([], (x: { n: number }) => x.n)).toBe(0);
    });
  });

  describe('donutDashArray', () => {
    it('computes the stroke-dasharray for a fraction of the ring', () => {
      // r=46 -> circumference = 2*PI*46 ~= 289.03
      expect(donutDashArray(60, 96)).toBe('181 289');
    });

    it('guards total === 0 to a zero-length arc, not NaN', () => {
      expect(donutDashArray(0, 0)).toBe('0 289');
    });
  });

  describe('linePoints / toPolylinePoints', () => {
    it('maps a series onto the 640x180 viewBox', () => {
      const points = linePoints([{ count: 0 }, { count: 50 }, { count: 100 }]);
      expect(points).toEqual([
        { x: 5, y: 175 },
        { x: 320, y: 98 },
        { x: 635, y: 20 }
      ]);
      expect(toPolylinePoints(points)).toBe('5,175 320,98 635,20');
    });

    it('guards an all-zero series to the flat baseline, not NaN', () => {
      const points = linePoints([{ count: 0 }, { count: 0 }]);
      expect(points.every((p) => p.y === 179)).toBe(true);
    });

    it('guards a single-point series (no length-1 division) to the horizontal centre', () => {
      const points = linePoints([{ count: 10 }]);
      expect(points).toEqual([{ x: 320, y: 20 }]);
    });

    it('returns an empty array for an empty series', () => {
      expect(linePoints([])).toEqual([]);
    });
  });

  describe('formatAxisDate', () => {
    it('formats an ISO date as a compact "D Mon" label', () => {
      expect(formatAxisDate('2026-08-03')).toBe('3 Aug');
    });

    it('returns an empty string for an unparseable date', () => {
      expect(formatAxisDate('not-a-date')).toBe('');
    });
  });

  describe('axisLabelIndices', () => {
    it('returns every index when the series is 5 points or fewer', () => {
      expect(axisLabelIndices(3)).toEqual([0, 1, 2]);
    });

    it('returns 5 evenly-spaced, de-duplicated indices for a longer series', () => {
      expect(axisLabelIndices(12)).toEqual([0, 3, 6, 8, 11]);
    });

    it('returns an empty array for an empty series', () => {
      expect(axisLabelIndices(0)).toEqual([]);
    });
  });

  describe('pluralize', () => {
    it('keeps the noun singular for exactly 1', () => {
      expect(pluralize(1, 'shipment')).toBe('1 shipment');
    });

    it('pluralizes for 0 and for >1', () => {
      expect(pluralize(0, 'shipment')).toBe('0 shipments');
      expect(pluralize(2, 'shipment')).toBe('2 shipments');
    });
  });
});
