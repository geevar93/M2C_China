import { formatInr, formatMoneyOrDash } from './money.util';

describe('money.util', () => {
  it('groups in the Indian lakh/crore style, not the Western thousands style', () => {
    // 250000 is ₹2,50,000 in en-IN and ₹250,000 in en-US — this assertion is
    // the whole point of pinning the locale, so it must not be relaxed.
    expect(formatInr(250000)).toContain('2,50,000');
    expect(formatInr(920000)).toContain('9,20,000');
  });

  it('renders an em-dash for a not-costed value, never a zero amount (D-30)', () => {
    // "not costed" and "worth zero" are different facts about the business.
    // Blurring them is exactly what this helper exists to prevent.
    expect(formatMoneyOrDash(null)).toBe('—');
    expect(formatMoneyOrDash(undefined)).toBe('—');
    expect(formatMoneyOrDash(0)).not.toBe('—');
    expect(formatMoneyOrDash(0)).toContain('0');
  });

  it('formats a real zero as a zero amount', () => {
    expect(formatMoneyOrDash(0)).toBe(formatInr(0));
  });

  it('keeps two fraction digits for decimal wire values', () => {
    // Quantities and money arrive as decimals, not integers (live-verified
    // against the M5 API — see inventory.models.ts).
    expect(formatInr(1420.5)).toContain('1,420.50');
  });
});
