import { stockBarWidth, stockLevelColor, stockLevelTextColor } from './stock-level.util';

describe('stockBarWidth', () => {
  it('clamps the ratio to 100 for a fully-stocked item', () => {
    expect(stockBarWidth(2000, 600)).toBe(100);
  });

  it('computes the prototype ratio for a partially-stocked item', () => {
    // ported formula: round((qty / (reorder * 2.5)) * 100)
    expect(stockBarWidth(300, 600)).toBe(20);
  });

  it('returns 0 for an empty item', () => {
    expect(stockBarWidth(0, 600)).toBe(0);
  });

  it('returns 100 for any negative quantity, regardless of the ratio', () => {
    expect(stockBarWidth(-40, 600)).toBe(100);
    expect(stockBarWidth(-1, 1)).toBe(100);
  });

  it('guards a zero reorder threshold instead of dividing by zero', () => {
    expect(stockBarWidth(50, 0)).toBe(100);
    expect(stockBarWidth(0, 0)).toBe(0);
  });
});

describe('stockLevelColor / stockLevelTextColor', () => {
  it('maps HEALTHY to the success tokens', () => {
    expect(stockLevelColor('HEALTHY')).toEqual({ bg: 'var(--color-success-bg)', fg: 'var(--color-success)' });
    expect(stockLevelTextColor('HEALTHY')).toBe('var(--color-text)');
  });

  it('maps LOW to the warning tokens', () => {
    expect(stockLevelColor('LOW')).toEqual({ bg: 'var(--color-warning-bg)', fg: 'var(--color-warning)' });
    expect(stockLevelTextColor('LOW')).toBe('var(--color-warning)');
  });

  it('maps NEGATIVE to the danger tokens', () => {
    expect(stockLevelColor('NEGATIVE')).toEqual({ bg: 'var(--color-danger-bg)', fg: 'var(--color-danger)' });
    expect(stockLevelTextColor('NEGATIVE')).toBe('var(--color-danger)');
  });
});
