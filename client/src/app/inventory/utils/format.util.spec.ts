import { formatQty, formatStockValue } from './format.util';
import { formatInr } from '../../shared/utils/money.util';

describe('formatStockValue', () => {
  it('renders an em-dash for null, never ₹0 (D-30: null means "not costed", not "worth zero")', () => {
    expect(formatStockValue(null)).toBe('—');
  });

  it('renders formatted INR for a real value, including zero itself', () => {
    expect(formatStockValue(0)).toBe(formatInr(0));
    expect(formatStockValue(220800)).toBe(formatInr(220800));
  });
});

describe('formatQty', () => {
  it('formats a decimal quantity without assuming a whole number', () => {
    expect(formatQty(10)).toBe('10');
    expect(formatQty(10.5)).toBe('10.5');
    expect(formatQty(1840)).toBe('1,840');
  });
});
